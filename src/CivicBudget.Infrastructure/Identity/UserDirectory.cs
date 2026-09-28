using CivicBudget.Application.Notifications;
using CivicBudget.Application.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// Who notices go to, read from Identity's tables. Scoped by government explicitly, like
/// <see cref="UserAdminService"/>, because Identity tables sit outside the tenant filter. Locked
/// accounts get nothing.
/// </summary>
public sealed class UserDirectory(IDbContextFactory<CivicBudgetDbContext> dbFactory, TimeProvider clock) : IUserDirectory
{
    public async Task<IReadOnlyList<EmailRecipient>> FiscalAuthorityAsync(Guid governmentId, CancellationToken ct = default)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        string[] roles = [Roles.Admin, Roles.FinanceDirector];
        DateTimeOffset now = clock.GetUtcNow();
        return await (from user in db.Users
                      where user.GovernmentId == governmentId && user.Email != null && (user.LockoutEnd == null || user.LockoutEnd <= now)
                            && db.UserRoles.Any(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && roles.Contains(r.Name!)))
                      orderby user.DisplayName
                      select new EmailRecipient(user.Id, user.Email!, user.DisplayName)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmailRecipient>> DepartmentUsersAsync(Guid governmentId, Guid departmentId, CancellationToken ct = default)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        DateTimeOffset now = clock.GetUtcNow();
        return await (from user in db.Users
                      where user.GovernmentId == governmentId && user.Email != null && (user.LockoutEnd == null || user.LockoutEnd <= now)
                            && db.UserDepartments.Any(ud => ud.UserId == user.Id && ud.DepartmentId == departmentId)
                      orderby user.DisplayName
                      select new EmailRecipient(user.Id, user.Email!, user.DisplayName)).ToListAsync(ct);
    }

    public async Task<int> CountAsync(Guid governmentId, CancellationToken ct = default)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.CountAsync(u => u.GovernmentId == governmentId, ct);
    }
}
