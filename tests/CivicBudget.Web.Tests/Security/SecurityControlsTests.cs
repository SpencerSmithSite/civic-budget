using System.Security.Claims;
using Bunit.TestDoubles;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Security;
using CivicBudget.Web.Components.Admin;
using CivicBudget.Web.Components.Admin.Notifications;
using CivicBudget.Web.Components.Common;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Security;

/// <summary>
/// The request-level controls: which requests are rate limited, how long a sign-in lasts, the
/// headers every response carries, and the security log page.
/// </summary>
public class SecurityControlsTests : BunitContext
{
    public SecurityControlsTests()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData("POST", "/Account/Login", RateLimits.SignIn)]
    [InlineData("POST", "/account/login", RateLimits.SignIn)]
    [InlineData("POST", "/Account/LoginWith2fa", RateLimits.SignIn)]
    [InlineData("POST", "/Account/LoginWithRecoveryCode", RateLimits.SignIn)]
    [InlineData("POST", "/Account/ForgotPassword", RateLimits.PasswordReset)]
    [InlineData("POST", "/Account/ResetPassword", RateLimits.PasswordReset)]
    [InlineData("GET", "/admin/export/government/all-data.zip", RateLimits.Export)]
    [InlineData("GET", "/Account/Login", null)]
    [InlineData("POST", "/Account/Logout", null)]
    [InlineData("GET", "/admin/budgets", null)]
    [InlineData("GET", "/transparency/maple-ridge-oh", null)]
    public void Limits_only_the_requests_an_attacker_would_repeat(string method, string path, string? expected)
    {
        var context = new DefaultHttpContext { Request = { Method = method, Path = path } };

        Assert.Equal(expected, RateLimits.PolicyFor(context));
    }

    [Fact]
    public async Task A_session_ends_after_thirty_idle_minutes_and_a_remembered_device_after_fourteen_days()
    {
        var options = new CookieAuthenticationOptions();
        SessionPolicy.Apply(options);

        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);

        AuthenticationProperties remembered = await SigningIn(options, persistent: true);
        Assert.InRange(remembered.ExpiresUtc!.Value - DateTimeOffset.UtcNow, TimeSpan.FromDays(14) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(14));

        var stamp = new Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions();
        SessionPolicy.Apply(stamp);
        Assert.Equal(TimeSpan.FromMinutes(5), stamp.ValidationInterval);

        // An ordinary session gets no fixed expiry of its own, so the 30-minute sliding window applies.
        Assert.Null((await SigningIn(options, persistent: false)).ExpiresUtc);
    }

    [Fact]
    public async Task Every_response_gets_the_security_headers_and_a_policy_with_this_requests_nonce()
    {
        (DefaultHttpContext http, StartableResponse response) = Request();

        await new SecurityHeadersMiddleware(_ => Task.CompletedTask).InvokeAsync(http);
        await response.StartAsync();

        string policy = http.Response.Headers.ContentSecurityPolicy.ToString();
        Assert.Contains($"script-src 'self' 'nonce-{SecurityHeadersMiddleware.Nonce(http)}'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", http.Response.Headers.XFrameOptions);
        Assert.Equal("strict-origin-when-cross-origin", http.Response.Headers["Referrer-Policy"]);
    }

    [Fact]
    public async Task Each_request_gets_its_own_nonce_and_a_stricter_policy_already_set_is_kept()
    {
        (DefaultHttpContext first, _) = Request();
        (DefaultHttpContext second, StartableResponse response) = Request();
        Assert.NotEqual(SecurityHeadersMiddleware.Nonce(first), SecurityHeadersMiddleware.Nonce(second));
        Assert.Equal(SecurityHeadersMiddleware.Nonce(first), SecurityHeadersMiddleware.Nonce(first));

        // An uploaded image sets its own sandboxed policy; the page policy must not replace it.
        await new SecurityHeadersMiddleware(context =>
        {
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Task.CompletedTask;
        }).InvokeAsync(second);
        await response.StartAsync();

        Assert.Equal("default-src 'none'; sandbox", second.Response.Headers.ContentSecurityPolicy);
    }

    [Fact]
    public void The_security_log_lists_events_and_offers_older_ones()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Services.AddSingleton<ISecurityLogService>(new FakeLog(new SecurityLogPageDto(51,
        [
            new SecurityEventDto(Guid.NewGuid(), SecurityEventKind.DataExported, now, "admin@mapleridge.example", "203.0.113.5", "/admin/export/government/all-data.zip"),
            new SecurityEventDto(Guid.NewGuid(), SecurityEventKind.LockedOut, now.AddMinutes(-5), "streets@mapleridge.example", "198.51.100.7", null),
        ])));
        AddAuthorization().SetAuthorized("admin").SetRoles(Roles.Admin).SetPolicies(Policies.CanManageUsers);

        IRenderedComponent<SecurityLog> page = Render<SecurityLog>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("table.cb-grid tbody tr").Count));
        Assert.Contains("Locked out", page.Find("table.cb-grid").TextContent, StringComparison.Ordinal);
        Assert.Contains("198.51.100.7", page.Markup, StringComparison.Ordinal);
        Assert.Equal(2, page.FindAll(".cb-cards .cb-list-card").Count);
        Assert.Contains("Show older events", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(page.FindAll("#logKind option"), o => o.TextContent is "Unknown account" or "Government removed" or "Old records removed");
    }

    private static async Task<AuthenticationProperties> SigningIn(CookieAuthenticationOptions options, bool persistent)
    {
        var properties = new AuthenticationProperties { IsPersistent = persistent };
        var scheme = new AuthenticationScheme("Identity.Application", null, typeof(CookieAuthenticationHandler));
        await options.Events.OnSigningIn(new CookieSigningInContext(new DefaultHttpContext(), scheme, options, new ClaimsPrincipal(), properties, new CookieOptions()));
        return properties;
    }

    private static (DefaultHttpContext, StartableResponse) Request()
    {
        var response = new StartableResponse();
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpResponseFeature>(response);
        return (http, response);
    }

    /// <summary>A response that runs its OnStarting callbacks when told, as the server does before the first byte.</summary>
    private sealed class StartableResponse : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach ((Func<object, Task> callback, object state) in _starting)
            {
                await callback(state);
            }
        }
    }

    private sealed class FakeLog(SecurityLogPageDto page) : ISecurityLogService
    {
        public Task<SecurityLogPageDto?> ListAsync(SecurityEventKind? kind, int skip, int take, CancellationToken ct = default) =>
            Task.FromResult<SecurityLogPageDto?>(page);
    }
}
