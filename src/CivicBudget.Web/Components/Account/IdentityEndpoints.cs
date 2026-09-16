using System.Security.Claims;
using CivicBudget.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CivicBudget.Web.Components.Account;

internal static class IdentityEndpoints
{
    /// <summary>
    /// Logout is a POST endpoint rather than a page so it is protected by the antiforgery token and
    /// cannot be triggered by a link in an email. The Account pages need nothing else: there is no
    /// self-registration, external login, or two-factor flow in this application.
    /// </summary>
    public static IEndpointConventionBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        return accountGroup;
    }
}
