using System.Security.Claims;
using CivicBudget.Application.Users;
using CivicBudget.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CivicBudget.Web.Components.Account;

internal static class IdentityEndpoints
{
    /// <summary>
    /// Logout is a POST endpoint rather than a page so it is protected by the antiforgery token and
    /// cannot be triggered by a link in an email. The idle sign-out is a GET, because the browser's
    /// idle clock navigates to it; the worst a forged link can do is sign someone out early.
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

        // The browser's idle clock renews the session while someone works on an open page (a circuit
        // makes no requests of its own) and ends it after the idle timeout. Neither returns content.
        accountGroup.MapGet("/KeepAlive", () => TypedResults.NoContent()).RequireAuthorization();

        accountGroup.MapGet("/SessionExpired", async (HttpContext context, [FromServices] SignInManager<ApplicationUser> signInManager) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Items[AuditingSignInManager.IdleSignOutItem] = true;
                await signInManager.SignOutAsync();
            }

            return TypedResults.LocalRedirect("~/Account/Login?expired=true");
        });

        // Profile pictures. Authenticated only, and the service refuses users of another government.
        // The URL carries the upload version, so the browser may cache the bytes for a year.
        accountGroup.MapGet("/Avatar/{userId}", async (
            string userId,
            [FromServices] IUserAvatarService avatars,
            HttpContext context,
            CancellationToken ct) =>
        {
            UserAvatarDto? avatar = await avatars.GetAsync(userId, ct);
            if (avatar is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            ImageResponse.Harden(context.Response);
            return Results.Bytes(avatar.Data, avatar.ContentType, lastModified: avatar.UpdatedAtUtc);
        }).RequireAuthorization();

        return accountGroup;
    }
}
