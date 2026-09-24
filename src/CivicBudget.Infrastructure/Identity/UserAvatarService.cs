using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;
using CivicBudget.Application.Users;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Identity;

public sealed class UserAvatarService(
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserAvatarService
{

    private readonly Dictionary<string, long?> _versions = [];

    public event Action? Changed;

    public async Task<UserAvatarDto?> GetAsync(string userId, CancellationToken ct = default)
    {
        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        // The join to Users is the tenant check: pictures are visible only inside the government.
        return await (from avatar in db.UserAvatars
                      join user in db.Users on avatar.UserId equals user.Id
                      where avatar.UserId == userId && user.GovernmentId == governmentId
                      select new UserAvatarDto(avatar.Data, avatar.ContentType, avatar.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<long?> GetVersionAsync(string userId, CancellationToken ct = default)
    {
        if (_versions.TryGetValue(userId, out long? cached))
        {
            return cached;
        }

        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        DateTimeOffset? updated = await db.Users
            .Where(u => u.Id == userId && u.GovernmentId == governmentId)
            .Select(u => u.AvatarUpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        long? version = updated?.UtcTicks;
        _versions[userId] = version;
        return version;
    }

    public async Task<Result> SetOwnAsync(byte[] data, string contentType, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure("Sign in to set a profile picture.");
        }

        Result valid = UploadedImage.Validate(data, contentType, "picture");
        if (valid.IsFailure)
        {
            return valid;
        }

        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        ApplicationUser? user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.GovernmentId == governmentId, ct);
        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        UserAvatar? avatar = await db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (avatar is null)
        {
            db.UserAvatars.Add(new UserAvatar { UserId = userId, ContentType = contentType, Data = data, UpdatedAtUtc = now });
        }
        else
        {
            avatar.ContentType = contentType;
            avatar.Data = data;
            avatar.UpdatedAtUtc = now;
        }

        user.AvatarUpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        Invalidate(userId, now.UtcTicks);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(string userId, CancellationToken ct = default)
    {
        bool own = currentUser.UserId == userId;
        if (!own && !currentUser.IsInRole(Roles.Admin))
        {
            return Result.Failure("Only an Administrator can remove another user's picture.");
        }

        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        ApplicationUser? user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.GovernmentId == governmentId, ct);
        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        UserAvatar? avatar = await db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (avatar is not null)
        {
            db.UserAvatars.Remove(avatar);
        }

        user.AvatarUpdatedAtUtc = null;
        await db.SaveChangesAsync(ct);
        Invalidate(userId, null);
        return Result.Success();
    }

    private void Invalidate(string userId, long? version)
    {
        _versions[userId] = version;
        Changed?.Invoke();
    }

    private Guid RequireTenant() =>
        tenant.GovernmentId ?? throw new InvalidOperationException("No current government; profile pictures require a signed-in user.");
}
