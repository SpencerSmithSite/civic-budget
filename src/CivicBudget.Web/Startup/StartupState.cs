namespace CivicBudget.Web.Startup;

/// <summary>
/// Whether the database is ready for page requests. It starts not ready: the web host listens the
/// moment the process is up while migrations and seeding run behind it (<see cref="DatabaseStartupService"/>),
/// and until they finish every page request gets the waiting screen instead of a connection that
/// hangs. It can also go back to not ready: serverless SQL pauses after an hour without
/// connections even while the container stays up, so after a long quiet spell the next page
/// request checks the database before it is let through (<see cref="WakingUpMiddleware"/>).
/// </summary>
public sealed class StartupState
{
    /// <summary>
    /// How long without a page request before the database might be asleep. Serverless SQL pauses
    /// after sixty idle minutes; checking a little earlier costs one quick connection at most.
    /// </summary>
    public static readonly TimeSpan QuietSpell = TimeSpan.FromMinutes(55);

    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private DateTimeOffset waitingSince;
    private DateTimeOffset lastPageServed;

    public StartupState(TimeProvider clock)
    {
        this.clock = clock;
        waitingSince = clock.GetUtcNow();
    }

    public bool IsReady { get; private set; }

    /// <summary>Seconds since the current wait began, so the waiting page can keep its counter across reloads.</summary>
    public int ElapsedSeconds => (int)(clock.GetUtcNow() - waitingSince).TotalSeconds;

    /// <summary>Ready, but nobody has loaded a page for long enough that the database may have paused.</summary>
    public bool MayBeAsleep => IsReady && clock.GetUtcNow() - lastPageServed > QuietSpell;

    public void MarkReady()
    {
        lock (gate)
        {
            IsReady = true;
            lastPageServed = clock.GetUtcNow();
        }
    }

    public void RecordPageServed() => lastPageServed = clock.GetUtcNow();

    /// <summary>Back to not ready while the database is checked. Returns false if another request already did it.</summary>
    public bool BeginWaiting()
    {
        lock (gate)
        {
            if (!IsReady)
            {
                return false;
            }

            IsReady = false;
            waitingSince = clock.GetUtcNow();
            return true;
        }
    }
}
