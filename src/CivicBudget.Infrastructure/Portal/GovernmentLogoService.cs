using CivicBudget.Application.Common;
using CivicBudget.Application.Portal;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Application.Tenancy;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Portal;

public sealed class GovernmentLogoService(
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IPublishedSnapshotCacheInvalidator cache,
    TimeProvider clock) : IGovernmentLogoService
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "image/svg+xml" };

    public async Task<PortalLogoDto?> GetAsync(CancellationToken ct = default)
    {
        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GovernmentLogos
            .Where(l => l.GovernmentId == governmentId)
            .Select(l => new PortalLogoDto(l.Data, l.ContentType, l.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result> SetAsync(byte[] data, string contentType, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin))
        {
            return Result.Failure("Only an Administrator can change the government's logo.");
        }

        if (!AllowedTypes.Contains(contentType))
        {
            return Result.Failure("Choose a PNG, JPEG, WebP, or SVG image.");
        }

        if (data.Length == 0 || data.Length > GovernmentLogo.MaxBytes)
        {
            return Result.Failure($"The logo must be under {GovernmentLogo.MaxBytes / 1024} KB.");
        }

        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        DateTimeOffset now = clock.GetUtcNow();
        GovernmentLogo? logo = await db.GovernmentLogos.FirstOrDefaultAsync(l => l.GovernmentId == governmentId, ct);
        if (logo is null)
        {
            db.GovernmentLogos.Add(new GovernmentLogo { GovernmentId = governmentId, ContentType = contentType, Data = data, UpdatedAtUtc = now });
        }
        else
        {
            logo.ContentType = contentType;
            logo.Data = data;
            logo.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        await EvictPortalAsync(db, governmentId, ct);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin))
        {
            return Result.Failure("Only an Administrator can change the government's logo.");
        }

        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        GovernmentLogo? logo = await db.GovernmentLogos.FirstOrDefaultAsync(l => l.GovernmentId == governmentId, ct);
        if (logo is not null)
        {
            db.GovernmentLogos.Remove(logo);
            await db.SaveChangesAsync(ct);
            await EvictPortalAsync(db, governmentId, ct);
        }

        return Result.Success();
    }

    /// <summary>Portal pages are output-cached by government; the header carries the logo, so a change must evict them.</summary>
    private async Task EvictPortalAsync(CivicBudgetDbContext db, Guid governmentId, CancellationToken ct)
    {
        string? slug = await db.Governments.Where(g => g.Id == governmentId).Select(g => g.PublicSlug).FirstOrDefaultAsync(ct);
        if (slug is not null)
        {
            await cache.InvalidateAsync(slug, ct);
        }
    }

    private Guid RequireTenant() =>
        tenant.GovernmentId ?? throw new InvalidOperationException("No current government; the logo requires a signed-in administrator.");
}
