using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;
using CivicBudget.Application.Users;
using CivicBudget.Domain.Auditing;
using CivicBudget.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// Implements <see cref="IUserAdminService"/> with Identity's UserManager. Every read is scoped by
/// the current government because Identity tables are not covered by the tenant query filter
/// (see <see cref="ApplicationUser"/>). Two guard rails protect the acting admin from themselves:
/// they cannot lock their own account or remove their own Admin role.
/// </summary>
public sealed class UserAdminService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    TimeProvider clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);

        return await QueryUsers(db, governmentId, userId: null).ToListAsync(ct);
    }

    public async Task<UserSummaryDto?> GetAsync(string id, CancellationToken ct = default)
    {
        Guid governmentId = RequireTenant();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await QueryUsers(db, governmentId, userId: id).FirstOrDefaultAsync(ct);
    }

    public async Task<Result<string>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (await createValidator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return Result.Failure<string>(invalid.Errors);
        }

        Guid governmentId = RequireTenant();
        if (await DepartmentsOutsideTenantAsync(request.DepartmentIds, ct))
        {
            return Result.Failure<string>(nameof(request.DepartmentIds), "One or more departments do not belong to this government.");
        }

        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Result.Failure<string>(nameof(request.Email), "A user with that email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true, // no email flow in this app; admins create accounts directly
            DisplayName = request.DisplayName.Trim(),
            GovernmentId = governmentId,
            MustChangePassword = true, // the administrator's password is temporary
        };
        foreach (Guid departmentId in request.DepartmentIds.Distinct())
        {
            user.Departments.Add(new UserDepartment { DepartmentId = departmentId });
        }

        IdentityResult created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return Result.Failure<string>(ToErrors(created, nameof(request.Password)));
        }

        IdentityResult roleAdded = await userManager.AddToRoleAsync(user, request.Role);
        if (!roleAdded.Succeeded)
        {
            return Result.Failure<string>(ToErrors(roleAdded, nameof(request.Role)));
        }

        await AuditAsync(user, $"Created user {user.Email} as {Roles.DisplayName(request.Role)}{DepartmentsNote(request.DepartmentIds)}", ct);
        return Result.Success(user.Id);
    }

    public async Task<Result> UpdateAsync(UpdateUserRequest request, CancellationToken ct = default)
    {
        if (await updateValidator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return invalid;
        }

        if (await DepartmentsOutsideTenantAsync(request.DepartmentIds, ct))
        {
            return Result.Failure(nameof(request.DepartmentIds), "One or more departments do not belong to this government.");
        }

        ApplicationUser? user = await FindInTenantAsync(request.Id, ct);
        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        IList<string> currentRoles = await userManager.GetRolesAsync(user);
        bool removingOwnAdmin = user.Id == currentUser.UserId
            && currentRoles.Contains(Roles.Admin)
            && request.Role != Roles.Admin;
        if (removingOwnAdmin)
        {
            return Result.Failure(nameof(request.Role), "You cannot remove your own Administrator role.");
        }

        user.DisplayName = request.DisplayName.Trim();
        IdentityResult updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result.Failure(ToErrors(updated, nameof(request.DisplayName)));
        }

        if (!currentRoles.SequenceEqual([request.Role]))
        {
            // Checked, not assumed: a failed add after a successful remove would leave the user with no
            // role at all while the trail said "Updated ... as Fiscal Officer".
            IdentityResult removed = await userManager.RemoveFromRolesAsync(user, currentRoles);
            IdentityResult added = removed.Succeeded ? await userManager.AddToRoleAsync(user, request.Role) : removed;
            if (!added.Succeeded)
            {
                return Result.Failure(ToErrors(added, nameof(request.Role)));
            }
        }

        await ReplaceDepartmentsAsync(user.Id, request.Role == Roles.DepartmentHead ? request.DepartmentIds : [], ct);

        // Changing the stamp makes existing sessions re-sign-in, so new claims take effect promptly.
        await userManager.UpdateSecurityStampAsync(user);
        await AuditAsync(user, $"Updated user {user.Email}: {Roles.DisplayName(request.Role)}{DepartmentsNote(request.DepartmentIds)}", ct);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        if (await resetValidator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return invalid;
        }

        ApplicationUser? user = await FindInTenantAsync(request.Id, ct);
        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        // The token path is Identity's supported way to set a password without knowing the old one.
        string token = await userManager.GeneratePasswordResetTokenAsync(user);
        IdentityResult reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return Result.Failure(ToErrors(reset, nameof(request.NewPassword)));
        }

        // The administrator knows this password, so it is temporary; the stamp change ends any open session.
        user.MustChangePassword = true;
        IdentityResult flagged = await userManager.UpdateAsync(user);
        if (!flagged.Succeeded)
        {
            // The password did change; if the flag did not save, the administrator must know it is not temporary.
            return Result.Failure(ToErrors(flagged, nameof(request.NewPassword)));
        }

        await userManager.UpdateSecurityStampAsync(user);
        await AuditAsync(user, $"Reset the password for {user.Email} (temporary, must be changed at sign-in)", ct);
        return Result.Success();
    }

    public async Task<Result> SetLockedOutAsync(string id, bool isLockedOut, CancellationToken ct = default)
    {
        ApplicationUser? user = await FindInTenantAsync(id, ct);
        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        if (isLockedOut && user.Id == currentUser.UserId)
        {
            return Result.Failure("You cannot lock your own account.");
        }

        await userManager.SetLockoutEnabledAsync(user, true);
        IdentityResult result = await userManager.SetLockoutEndDateAsync(user, isLockedOut ? DateTimeOffset.MaxValue : null);
        if (!result.Succeeded)
        {
            return Result.Failure(ToErrors(result, string.Empty));
        }

        if (isLockedOut)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        await AuditAsync(user, isLockedOut ? $"Locked {user.Email}; they can no longer sign in" : $"Unlocked {user.Email}", ct);

        return Result.Success();
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// User accounts are not [Audited] entities (Identity owns them), so administration is recorded
    /// as named events on the government's trail, in the acting administrator's name.
    /// </summary>
    private async Task AuditAsync(ApplicationUser user, string description, CancellationToken ct)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditEntries.Add(AuditEntry.Event(user.GovernmentId, "User", Guid.TryParse(user.Id, out Guid id) ? id : Guid.Empty, description,
            currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    private static string DepartmentsNote(IReadOnlyList<Guid> departmentIds) =>
        departmentIds.Count == 0 ? "" : $", {departmentIds.Count} department{(departmentIds.Count == 1 ? "" : "s")}";

    /// <summary>
    /// One query, projected to the DTO with the role name and department codes as correlated
    /// sub-queries. Filtering and ordering happen on the entity, before the projection: EF Core
    /// cannot order by a constructed DTO that contains collection sub-queries.
    /// </summary>
    private static IQueryable<UserSummaryDto> QueryUsers(CivicBudgetDbContext db, Guid governmentId, string? userId) =>
        from user in db.Users
        where user.GovernmentId == governmentId && (userId == null || user.Id == userId)
        orderby user.DisplayName
        select new UserSummaryDto(
            user.Id,
            user.Email!,
            user.DisplayName,
            (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Name!).FirstOrDefault() ?? string.Empty,
            (from ud in db.UserDepartments where ud.UserId == user.Id select ud.DepartmentId).ToList(),
            (from ud in db.UserDepartments join d in db.Departments on ud.DepartmentId equals d.Id where ud.UserId == user.Id orderby d.Code select d.Code).ToList(),
            user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.AvatarUpdatedAtUtc == null ? null : user.AvatarUpdatedAtUtc.Value.UtcTicks);

    private async Task<ApplicationUser?> FindInTenantAsync(string id, CancellationToken ct)
    {
        Guid governmentId = RequireTenant();
        return await userManager.Users.FirstOrDefaultAsync(u => u.Id == id && u.GovernmentId == governmentId, ct);
    }

    /// <summary>True if any requested department is not visible through the tenant filter.</summary>
    private async Task<bool> DepartmentsOutsideTenantAsync(IReadOnlyList<Guid> departmentIds, CancellationToken ct)
    {
        if (departmentIds.Count == 0)
        {
            return false;
        }

        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int visible = await db.Departments.CountAsync(d => departmentIds.Contains(d.Id), ct);
        return visible != departmentIds.Distinct().Count();
    }

    private async Task ReplaceDepartmentsAsync(string userId, IReadOnlyList<Guid> departmentIds, CancellationToken ct)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.UserDepartments.RemoveRange(await db.UserDepartments.Where(ud => ud.UserId == userId).ToListAsync(ct));
        db.UserDepartments.AddRange(departmentIds.Distinct().Select(id => new UserDepartment { UserId = userId, DepartmentId = id }));
        await db.SaveChangesAsync(ct);
    }

    private Guid RequireTenant() =>
        tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set; user administration requires a signed-in administrator.");

    private static List<ValidationError> ToErrors(IdentityResult result, string propertyName) =>
        result.Errors.Select(e => new ValidationError(propertyName, e.Description)).ToList();
}
