using CivicBudget.Application.Security;

namespace CivicBudget.Web.Security;

/// <summary>
/// Keeps a user whose government requires two-step sign-in on the page that sets it up, until they
/// have. The flag arrives as a claim (<see cref="ClaimNames.MfaSetupRequired"/>) from the claims
/// factory; turning two-step sign-in on refreshes the sign-in, which reissues the cookie without it.
/// The same pages are exempt as for a temporary password, so the user can finish.
/// </summary>
public sealed class RequireMfaMiddleware(RequestDelegate next)
{
    public const string SetUpPath = "/Account/Manage/EnableAuthenticator";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(ClaimNames.MfaSetupRequired, "1")
            && !MustChangePasswordMiddleware.IsExempt(context.Request.Path))
        {
            context.Response.Redirect(SetUpPath + "?required=true");
            return Task.CompletedTask;
        }

        return next(context);
    }
}
