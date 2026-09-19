using CivicBudget.Application.Security;

namespace CivicBudget.Web.Security;

/// <summary>
/// Keeps a user whose password is temporary on the change-password page. The flag arrives as a
/// claim (<see cref="ClaimNames.MustChangePassword"/>) set by the claims factory; the page clears
/// the flag and refreshes the sign-in, which reissues the cookie without the claim. Account pages,
/// sign-out, and static assets are left alone so the user can actually complete the change.
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public const string ChangePasswordPath = "/Account/Manage/ChangePassword";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(ClaimNames.MustChangePassword, "1")
            && !IsExempt(context.Request.Path))
        {
            context.Response.Redirect(ChangePasswordPath + "?forced=true");
            return Task.CompletedTask;
        }

        return next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/transparency", StringComparison.OrdinalIgnoreCase)
        || path.Value?.Contains('.', StringComparison.Ordinal) == true; // app.css, favicon.svg, fingerprinted assets
}
