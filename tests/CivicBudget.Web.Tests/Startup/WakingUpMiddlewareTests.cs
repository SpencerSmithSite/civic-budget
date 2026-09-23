using CivicBudget.Web.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace CivicBudget.Web.Tests.Startup;

/// <summary>
/// Until the database is ready, page requests get the waiting screen (a 503 with its own body) while
/// the probes and static assets pass; the moment the state flips, everything passes. After a quiet
/// spell the database may have paused behind a running container, so the next page request checks
/// it first and only shows the screen if the check is slow.
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

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state, new FakeWaker()).InvokeAsync(http);

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

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state, new FakeWaker()).InvokeAsync(http);

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

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state, new FakeWaker()).InvokeAsync(http);

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

    [Fact]
    public async Task After_a_quiet_spell_an_awake_database_is_checked_and_the_page_served()
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        state.MarkReady();
        clock.Advance(StartupState.QuietSpell + TimeSpan.FromMinutes(1));
        var waker = new FakeWaker(() => { state.MarkReady(); return Task.CompletedTask; });
        var http = new DefaultHttpContext { Request = { Path = "/admin" } };
        bool reachedNext = false;

        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state, waker).InvokeAsync(http);

        Assert.Equal(1, waker.Calls);
        Assert.True(reachedNext);
        Assert.False(state.MayBeAsleep);
    }

    [Fact]
    public async Task After_a_quiet_spell_a_sleeping_database_gets_the_waiting_screen_until_it_answers()
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        state.MarkReady();
        clock.Advance(StartupState.QuietSpell + TimeSpan.FromMinutes(1));
        var resuming = new TaskCompletionSource();
        var waker = new FakeWaker(() => resuming.Task);
        var middleware = new WakingUpMiddleware(_ => Task.CompletedTask, state, waker);

        var first = new DefaultHttpContext { Request = { Path = "/" }, Response = { Body = new MemoryStream() } };
        await middleware.InvokeAsync(first);
        var second = new DefaultHttpContext { Request = { Path = "/admin" }, Response = { Body = new MemoryStream() } };
        await middleware.InvokeAsync(second);

        Assert.Equal(503, first.Response.StatusCode);
        Assert.Equal(503, second.Response.StatusCode);
        Assert.Equal(1, waker.Calls); // a burst of requests shares one check
        first.Response.Body.Position = 0;
        Assert.Contains("<span id=\"t\">0</span>", await new StreamReader(first.Response.Body).ReadToEndAsync()); // the counter restarts for this wait

        state.MarkReady();
        resuming.SetResult();
        bool reachedNext = false;
        await new WakingUpMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, state, waker).InvokeAsync(new DefaultHttpContext { Request = { Path = "/admin" } });
        Assert.True(reachedNext);
    }

    [Fact]
    public void Serving_pages_keeps_the_database_counted_as_awake()
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        state.MarkReady();

        clock.Advance(StartupState.QuietSpell - TimeSpan.FromMinutes(1));
        Assert.False(state.MayBeAsleep);
        state.RecordPageServed();
        clock.Advance(StartupState.QuietSpell - TimeSpan.FromMinutes(1));
        Assert.False(state.MayBeAsleep);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(state.MayBeAsleep);
    }

    private sealed class FakeWaker(Func<Task>? wake = null) : IDatabaseWaker
    {
        public int Calls { get; private set; }

        public Task WakeAsync()
        {
            Calls++;
            return wake?.Invoke() ?? Task.CompletedTask;
        }
    }
}
