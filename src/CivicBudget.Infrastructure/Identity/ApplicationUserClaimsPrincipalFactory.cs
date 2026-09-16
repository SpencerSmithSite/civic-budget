using System.Security.Claims;
using CivicBudget.Application.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// Runs once at sign-in (and when the sign-in is refreshed) to build the cookie's claims. Adding the
/// government and departments as claims means every later request knows the tenant without a
/// database round trip, and the query filter can read it from <c>ICurrentUser</c> synchronously.
/// The base class already adds the user id, name, security stamp, and role claims.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    IDbContextFactory<CivicBudgetDbContext> dbFactory)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(ClaimNames.GovernmentId, user.GovernmentId.ToString()));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, user.DisplayName));

        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync();
        List<Guid> departmentIds = await db.UserDepartments
            .Where(ud => ud.UserId == user.Id)
            .Select(ud => ud.DepartmentId)
            .ToListAsync();

        foreach (Guid departmentId in departmentIds)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, departmentId.ToString()));
        }

        return identity;
    }
}
