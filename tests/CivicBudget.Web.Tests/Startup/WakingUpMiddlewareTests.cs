using CivicBudget.Web.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace CivicBudget.Web.Tests.Startup;

/// <summary>
/// Until the database is ready, page requests get the waiting screen (a 503 with its own body) while
/// the probes and static assets pass; the moment the state flips, everything passes.
/// </summary>
public class WakingUpMiddlewareTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/transparency/maple-ridge-oh")]
    [InlineData("/_blazor")]
    public async Task Serves_the_waiting_screen_while_the_database_is_preparing(string path)
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        clock.Advance(TimeSpan.FromSeconds(42));
        var http = new DefaultHttpContext { Request = { Path = path }, Response = { Body = new MemoryStream() } };
        bool reachedNext = false;

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state).InvokeAsync(http);

        Assert.False(reachedNext);
        Assert.Equal(503, http.Response.StatusCode);
        Assert.Equal("5", http.Response.Headers.RetryAfter);
        Assert.Equal("no-store", http.Response.Headers.CacheControl);
        Assert.StartsWith("text/html", http.Response.ContentType);
        http.Response.Body.Position = 0;
        string body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        Assert.Contains("Waking up the demo", body);
        Assert.Contains("/health/startup", body);
        Assert.Contains("<span id=\"t\">42</span>", body); // the counter continues from the process's own clock
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/startup")]
    [InlineData("/health/ready")]
    [InlineData("/favicon.svg")]
    [InlineData("/app.abc123.css")]
    public async Task Lets_probes_and_static_assets_through(string path)
    {
        var state = new StartupState(new FakeTimeProvider());
        var http = new DefaultHttpContext { Request = { Path = path } };
        bool reachedNext = false;

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state).InvokeAsync(http);

        Assert.True(reachedNext);
        Assert.Equal(200, http.Response.StatusCode);
    }

    [Fact]
    public async Task Passes_everything_once_ready()
    {
        var state = new StartupState(new FakeTimeProvider());
        state.MarkReady();
        var http = new DefaultHttpContext { Request = { Path = "/admin" } };
        bool reachedNext = false;

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state).InvokeAsync(http);

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task Startup_health_check_follows_the_state()
    {
        var state = new StartupState(new FakeTimeProvider());
        var check = new StartupHealthCheck(state);

        HealthCheckResult before = await check.CheckHealthAsync(new HealthCheckContext());
        state.MarkReady();
        HealthCheckResult after = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, before.Status);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }
}
