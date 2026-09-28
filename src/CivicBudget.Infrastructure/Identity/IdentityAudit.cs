using CivicBudget.Domain.Auditing;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// Writes sign-in and account security events (two-step sign-in turned on or off, recovery codes
/// used or renewed) to the government's audit trail. Identity's users are not audited entities, so
/// these are named events, like the ones <see cref="UserAdminService"/> writes. The account pages
/// call this rather than touching a DbContext.
/// </summary>
public sealed class IdentityAudit(IDbContextFactory<CivicBudgetDbContext> dbFactory, CurrentUserContext context, TimeProvider clock)
{
    /// <summary>Records <paramref name="description"/> about <paramref name="user"/>, done by the signed-in user or, if none, by the user themselves.</summary>
    public async Task RecordAsync(ApplicationUser user, string description, CancellationToken ct = default)
    {
        context.SetTenant(user.GovernmentId);
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditEntries.Add(AuditEntry.Event(user.GovernmentId, "User", Guid.TryParse(user.Id, out Guid id) ? id : Guid.Empty, description,
            context.UserId ?? user.Id, context.DisplayName ?? user.DisplayName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }
}
