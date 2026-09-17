using CivicBudget.Infrastructure.Security;

namespace CivicBudget.Web.Security;

/// <summary>
/// Copies the authenticated principal of each HTTP request into the request scope's
/// <see cref="CurrentUserContext"/>. This covers static SSR pages, minimal API endpoints, and the
/// initial render of interactive pages. Interactive Server circuits get their own scope and are
/// handled by <see cref="CurrentUserCircuitHandler"/>.
/// Must run after UseAuthentication, otherwise the user is still anonymous.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUserContext currentUser)
    {
        currentUser.SetPrincipal(context.User);
        await next(context);
    }
}
