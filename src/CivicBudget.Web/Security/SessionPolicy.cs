using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace CivicBudget.Web.Security;

/// <summary>
/// How long a sign-in lasts. A staff session ends after 30 minutes without activity; "Remember me"
/// keeps a trusted device signed in for 14 days. The server enforces both through the cookie's
/// expiry, which slides with each request. An open admin page talks over its circuit rather than
/// making requests, so it also keeps its own idle clock in the browser (js/session.js): it renews the
/// cookie while someone is working, warns two minutes before the end, and signs out when time is up.
/// </summary>
public static class SessionPolicy
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RememberedFor = TimeSpan.FromDays(14);

    /// <summary>
    /// How soon a deactivated user, a role change, or a password reset reaches sessions already open:
    /// the cookie and each open page re-check the user's security stamp this often. Each check is one
    /// small query for someone who is signed in, so the database is awake anyway.
    /// </summary>
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(5);

    /// <summary>Applies <see cref="RecheckEvery"/> to the cookie's security stamp check.</summary>
    public static void Apply(SecurityStampValidatorOptions options) => options.ValidationInterval = RecheckEvery;

    /// <summary>Applies the policy to Identity's application cookie.</summary>
    public static void Apply(CookieAuthenticationOptions options)
    {
        options.ExpireTimeSpan = IdleTimeout;
        options.SlidingExpiration = true;

        // Identity's defaults, stated here so the policy reads in one place: no script can read the
        // cookie, another site's form cannot carry it, and it is Secure whenever the request is https.
        // Behind the load balancer every request is https (X-Forwarded-Proto); "Always" would break
        // sign-in on http://localhost in Safari during development.
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnSigningIn = context =>
        {
            // Sliding renewal keeps the length a ticket was issued with, so a remembered device keeps
            // its 14 days and an ordinary session its 30 minutes.
            if (context.Properties.IsPersistent)
            {
                context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.Add(RememberedFor);
            }

            return Task.CompletedTask;
        };
    }
}
