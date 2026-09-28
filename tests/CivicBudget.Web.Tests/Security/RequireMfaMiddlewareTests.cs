using System.Security.Claims;
using CivicBudget.Application.Security;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Http;

namespace CivicBudget.Web.Tests.Security;

/// <summary>Where two-step sign-in is required, a user without it goes to set it up and nowhere else.</summary>
public class RequireMfaMiddlewareTests
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/budgets")]
    [InlineData("/admin/export/setup/funds.xlsx")]
    public async Task Sends_a_flagged_user_to_set_it_up(string path)
    {
        DefaultHttpContext http = Context(path, flagged: true);
        bool reachedNext = false;

        await new RequireMfaMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.False(reachedNext);
        Assert.Equal("/Account/Manage/EnableAuthenticator?required=true", http.Response.Headers.Location);
    }

    [Theory]
    [InlineData("/Account/Manage/EnableAuthenticator")]
    [InlineData("/Account/Logout")]
    [InlineData("/_blazor/negotiate")]
    [InlineData("/app.css")]
    public async Task Lets_a_flagged_user_reach_the_setup_and_sign_out(string path)
    {
        bool reachedNext = false;

        await new RequireMfaMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(Context(path, flagged: true));

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task Ignores_everyone_else()
    {
        int reached = 0;
        var middleware = new RequireMfaMiddleware(_ => { reached++; return Task.CompletedTask; });

        await middleware.InvokeAsync(Context("/admin", flagged: false));
        await middleware.InvokeAsync(new DefaultHttpContext { Request = { Path = "/admin" } });

        Assert.Equal(2, reached);
    }

    private static DefaultHttpContext Context(string path, bool flagged)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        if (flagged)
        {
            identity.AddClaim(new Claim(ClaimNames.MfaSetupRequired, "1"));
        }

        return new DefaultHttpContext { User = new ClaimsPrincipal(identity), Request = { Path = path } };
    }
}
