using System.Security.Cryptography;

namespace CivicBudget.Web.Security;

/// <summary>
/// The response headers every page and file gets, set as the response starts so they reach output
/// cache hits too (which is why this runs before <c>UseOutputCache</c>).
/// <list type="bullet">
/// <item>A Content-Security-Policy that allows scripts from this site only, plus the one inline script
/// Blazor needs (its import map), which carries a nonce made for this request. Styles allow inline
/// attributes, which the report bars and a few layouts use; nothing else is inline.</item>
/// <item>No framing by other sites, no MIME sniffing, a referrer that stops at this origin, and no
/// camera, microphone, location, or payment access.</item>
/// </list>
/// An uploaded image already carries a stricter policy (<see cref="ImageResponse.Harden"/>), which is kept.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string NonceItem = "cb-csp-nonce";

    /// <summary>This request's nonce, made on first use; the page renders it on its import map.</summary>
    public static string Nonce(HttpContext context)
    {
        if (context.Items[NonceItem] is string nonce)
        {
            return nonce;
        }

        string made = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItem] = made;
        return made;
    }

    public static string Policy(string nonce) =>
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        string nonce = Nonce(context);
        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] = Policy(nonce);
            }

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
