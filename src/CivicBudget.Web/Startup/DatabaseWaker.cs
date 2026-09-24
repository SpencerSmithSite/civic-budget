using CivicBudget.Infrastructure.Persistence;

namespace CivicBudget.Web.Startup;

/// <summary>Checks that the database answers, waiting while it resumes, then marks the app ready again.</summary>
public interface IDatabaseWaker
{
    /// <summary>Starts the check, or joins the one already running, so a burst of requests opens one connection.</summary>
    Task WakeAsync();
}

/// <summary>
/// The check behind <see cref="StartupState.MayBeAsleep"/>. It runs on its own, not on the request that
/// started it, so a visitor who gives up does not cancel the wake for the next one. If the database
/// never answers, the state stays not ready and the next page request starts a fresh attempt.
/// </summary>
public sealed class DatabaseWaker(IServiceProvider services, StartupState state, ILogger<DatabaseWaker> logger) : IDatabaseWaker
{
    private readonly Lock gate = new();
    private Task? running;

    public Task WakeAsync()
    {
        lock (gate)
        {
            if (running is null || running.IsCompleted)
            {
                running = Task.Run(WakeOnceAsync);
            }

            return running;
        }
    }

    private async Task WakeOnceAsync()
    {
        try
        {
            await DatabaseInitializer.WaitForDatabaseAsync(services);
            state.MarkReady();
            logger.LogInformation("Database answered after {Seconds}s; serving pages.", state.ElapsedSeconds);
        }
        catch (Exception ex)
        {
            // Without this the state would stay not ready with nothing left to flip it back: the
            // quiet-spell check only starts from ready, so every page would get the waiting screen
            // until the process restarted.
            state.MarkWakeFailed();
            logger.LogError(ex, "The database did not answer; the next page request will try again.");
        }
    }
}
