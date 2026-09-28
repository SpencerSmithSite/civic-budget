using System.Security.Claims;
using System.Threading.RateLimiting;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicBudget.Web.Security;

/// <summary>
/// Limits on the requests an attacker would repeat. Lockout already stops guessing one account's
/// password; these stop one address trying many accounts, or flooding the reset form.
/// <list type="bullet">
/// <item>Sign-in forms (password, code, recovery code): 20 posts per address per 5 minutes.</item>
/// <item>Forgot and reset password: 5 posts per address per 15 minutes.</item>
/// <item>Exports: 60 files per signed-in user per minute, far above what a person clicks.</item>
/// </list>
/// Everything else is unlimited. A refusal is a security event and a plain page that says to wait.
/// </summary>
public static class RateLimits
{
    public const string SignIn = "sign-in";
    public const string PasswordReset = "password-reset";
    public const string Export = "export";

    private static readonly string[] SignInPaths = ["/Account/Login", "/Account/LoginWith2fa", "/Account/LoginWithRecoveryCode"];
    private static readonly string[] ResetPaths = ["/Account/ForgotPassword", "/Account/ResetPassword"];

    /// <summary>Which limit a request falls under, or null for none.</summary>
    public static string? PolicyFor(HttpContext context)
    {
        PathString path = context.Request.Path;
        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (SignInPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return SignIn;
            }

            if (ResetPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return PasswordReset;
            }
        }

        return HttpMethods.IsGet(context.Request.Method) && path.StartsWithSegments("/admin/export", StringComparison.OrdinalIgnoreCase) ? Export : null;
    }

    public static IServiceCollection AddCivicBudgetRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                string address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return PolicyFor(context) switch
                {
                    SignIn => RateLimitPartition.GetFixedWindowLimiter($"{SignIn}:{address}", _ => Window(20, TimeSpan.FromMinutes(5))),
                    PasswordReset => RateLimitPartition.GetFixedWindowLimiter($"{PasswordReset}:{address}", _ => Window(5, TimeSpan.FromMinutes(15))),
                    Export => RateLimitPartition.GetFixedWindowLimiter($"{Export}:{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? address}", _ => Window(60, TimeSpan.FromMinutes(1))),
                    _ => RateLimitPartition.GetNoLimiter("none"),
                };
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                HttpContext http = context.HttpContext;
                ICurrentUser user = http.RequestServices.GetRequiredService<ICurrentUser>();
                await http.RequestServices.GetRequiredService<ISecurityEventLog>().RecordAsync(
                    SecurityEventKind.RateLimited, user.GovernmentId, user.UserId, http.User.Identity?.Name, http.Request.Path.Value, ct);
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                http.Response.ContentType = "text/html; charset=utf-8";
                await http.Response.WriteAsync(
                    "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Too many attempts</title></head>" +
                    "<body style=\"font-family:system-ui,sans-serif;max-width:36rem;margin:4rem auto;padding:0 1rem\">" +
                    "<h1>Too many attempts</h1><p>For your security, this was refused. Wait a few minutes, then try again.</p>" +
                    "<p><a href=\"/Account/Login\">Back to sign in</a></p></body></html>", ct);
            };
        });

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) =>
        new() { PermitLimit = permits, Window = window, QueueLimit = 0 };
}
