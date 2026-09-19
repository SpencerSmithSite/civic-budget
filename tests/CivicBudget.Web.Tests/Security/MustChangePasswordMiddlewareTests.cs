using System.Security.Claims;
using CivicBudget.Application.Security;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Http;

namespace CivicBudget.Web.Tests.Security;

/// <summary>A temporary password keeps its owner on the change-password page and nowhere else.</summary>
public class MustChangePasswordMiddlewareTests
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/budgets")]
    [InlineData("/")]
    public async Task Redirects_a_flagged_user_away_from_the_app(string path)
    {
        DefaultHttpContext http = Context(path, flagged: true);
        bool reachedNext = false;

        await new MustChangePasswordMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.False(reachedNext);
        Assert.Equal(302, http.Response.StatusCode);
        Assert.Equal("/Account/Manage/ChangePassword?forced=true", http.Response.Headers.Location);
    }

    [Theory]
    [InlineData("/Account/Manage/ChangePassword")]
    [InlineData("/Account/Logout")]
    [InlineData("/_blazor/negotiate")]
    [InlineData("/app.css")]
    [InlineData("/transparency/maple-ridge-oh")]
    public async Task Lets_a_flagged_user_reach_the_pages_they_need(string path)
    {
        DefaultHttpContext http = Context(path, flagged: true);
        bool reachedNext = false;

        await new MustChangePasswordMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task Ignores_users_without_the_claim_and_anonymous_requests()
    {
        int reached = 0;
        var middleware = new MustChangePasswordMiddleware(_ => { reached++; return Task.CompletedTask; });

        await middleware.InvokeAsync(Context("/admin", flagged: false));
        await middleware.InvokeAsync(new DefaultHttpContext { Request = { Path = "/admin" } });

        Assert.Equal(2, reached);
    }

    private static DefaultHttpContext Context(string path, bool flagged)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "u1"));
        if (flagged)
        {
            identity.AddClaim(new Claim(ClaimNames.MustChangePassword, "1"));
        }

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        http.Request.Path = path;
        return http;
    }
}
