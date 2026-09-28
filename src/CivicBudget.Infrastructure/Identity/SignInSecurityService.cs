using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Users;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Identity;

public sealed class SignInSecurityService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    CurrentUserContext context,
    IdentityAudit audit,
    TimeProvider clock) : ISignInSecurityService
{
    private const string NotAllowed = "Only an Administrator can change sign-in security.";

    public async Task<SignInSecurityDto> GetAsync(CancellationToken ct = default)
    {
        Guid governmentId = context.GovernmentId ?? throw new InvalidOperationException("No tenant is set.");
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        bool require = await db.Governments.Where(g => g.Id == governmentId).Select(g => g.RequireMfa).SingleAsync(ct);
        int with = await db.Users.CountAsync(u => u.GovernmentId == governmentId && u.TwoFactorEnabled, ct);
        int without = await db.Users.CountAsync(u => u.GovernmentId == governmentId && !u.TwoFactorEnabled, ct);
        return new SignInSecurityDto(require, with, without);
    }

    public async Task<Result> SetRequireMfaAsync(bool require, CancellationToken ct = default)
    {
        if (!context.IsInRole(Roles.Admin) || context.GovernmentId is not { } governmentId)
        {
            return Result.Failure(NotAllowed);
        }

        await using (CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct))
        {
            Domain.Governments.Government government = await db.Governments.SingleAsync(g => g.Id == governmentId, ct);
            if (government.RequireMfa == require)
            {
                return Result.Success();
            }

            government.SetRequireMfa(require);
            db.AuditEntries.Add(Domain.Auditing.AuditEntry.Event(governmentId, "Government", governmentId,
                require ? "Two-step sign-in is now required for every user" : "Two-step sign-in is no longer required",
                context.UserId ?? "system", context.DisplayName ?? "system", clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }

        if (require)
        {
            // Everyone without it is signed out, so their next sign-in carries the claim that sends
            // them to set it up. The administrator's own session is left alone to finish here.
            List<ApplicationUser> without = await userManager.Users
                .Where(u => u.GovernmentId == governmentId && !u.TwoFactorEnabled && u.Id != context.UserId).ToListAsync(ct);
            foreach (ApplicationUser user in without)
            {
                await userManager.UpdateSecurityStampAsync(user);
            }
        }

        return Result.Success();
    }

    public async Task<Result> ResetTwoFactorAsync(string userId, CancellationToken ct = default)
    {
        if (!context.IsInRole(Roles.Admin) || context.GovernmentId is not { } governmentId)
        {
            return Result.Failure(NotAllowed);
        }

        ApplicationUser? user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId && u.GovernmentId == governmentId, ct);
        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        // A new key makes the old phone's codes useless; turning it off lets them sign in with the
        // password and set up the new phone (at once, if the government requires it).
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
        await audit.RecordAsync(user, $"Reset two-step sign-in for {user.Email}", ct);
        return Result.Success();
    }
}
