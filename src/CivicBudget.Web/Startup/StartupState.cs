namespace CivicBudget.Web.Startup;

/// <summary>
/// Whether the database is ready for requests. The web host starts listening the moment the process
/// is up; migrations and seeding run behind it (<see cref="DatabaseStartupService"/>), and until
/// they finish every page request gets the waking-up screen instead of a connection that hangs.
/// On the free Azure tier the serverless database resumes from auto-pause in about a minute, and
/// a browser that sees nothing for that long assumes the site is down.
/// </summary>
public sealed class StartupState
{
    private readonly TimeProvider clock;
    private readonly DateTimeOffset startedAt;

    public StartupState(TimeProvider clock)
    {
        this.clock = clock;
        startedAt = clock.GetUtcNow();
    }

    public bool IsReady { get; private set; }

    /// <summary>Seconds since the process started, so the waiting page can keep its counter across reloads.</summary>
    public int ElapsedSeconds => (int)(clock.GetUtcNow() - startedAt).TotalSeconds;

    public void MarkReady() => IsReady = true;
}
