using CivicBudget.Web.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;

namespace CivicBudget.Web.Tests.Startup;

/// <summary>
/// The product site's wake call: it starts the database check only when one is due (after a quiet
/// spell, or after a check that gave up), once however many calls arrive, and never while the app
/// is fresh or still starting, so it cannot keep the free database awake.
/// </summary>
public class WakeEndpointTests
{
    private sealed class CountingWaker : IDatabaseWaker
    {
        public int Calls { get; private set; }

        public Task WakeAsync()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void After_a_quiet_spell_the_first_call_starts_one_check_and_the_rest_wait_for_it()
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        state.MarkReady();
        clock.Advance(StartupState.QuietSpell + TimeSpan.FromMinutes(1));
        var waker = new CountingWaker();

        Assert.IsType<Accepted>(WakeEndpoint.Wake(state, waker));
        Assert.IsType<NoContent>(WakeEndpoint.Wake(state, waker));
        Assert.IsType<NoContent>(WakeEndpoint.Wake(state, waker));

        Assert.Equal(1, waker.Calls);
        Assert.False(state.IsReady); // pages get the waiting screen until the check marks it ready
    }

    [Fact]
    public void Does_nothing_while_the_database_was_used_recently()
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        state.MarkReady();
        clock.Advance(StartupState.QuietSpell - TimeSpan.FromMinutes(1));
        var waker = new CountingWaker();

        Assert.IsType<NoContent>(WakeEndpoint.Wake(state, waker));

        Assert.Equal(0, waker.Calls);
        Assert.True(state.IsReady);
    }

    [Fact]
    public void Does_nothing_while_startup_is_still_preparing_the_database()
    {
        var waker = new CountingWaker();

        Assert.IsType<NoContent>(WakeEndpoint.Wake(new StartupState(new FakeTimeProvider()), waker));

        Assert.Equal(0, waker.Calls);
    }

    [Fact]
    public void After_a_check_gives_up_one_call_starts_another()
    {
        var clock = new FakeTimeProvider();
        var state = new StartupState(clock);
        state.MarkReady();
        clock.Advance(StartupState.QuietSpell + TimeSpan.FromMinutes(1));
        Assert.True(state.ClaimWake());
        state.MarkWakeFailed(); // what DatabaseWaker does when the database never answers
        var waker = new CountingWaker();

        Assert.IsType<Accepted>(WakeEndpoint.Wake(state, waker));
        Assert.IsType<NoContent>(WakeEndpoint.Wake(state, waker));

        Assert.Equal(1, waker.Calls);
    }

    [Fact]
    public void Is_a_health_path_so_the_waiting_screen_lets_it_through()
    {
        Assert.StartsWith("/health/", WakeEndpoint.Path, StringComparison.Ordinal);
        Assert.True(new PathString(WakeEndpoint.Path).StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase));
    }
}
