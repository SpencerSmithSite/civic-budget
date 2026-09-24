namespace CivicBudget.Web.Startup;

/// <summary>
/// Whether the database is ready for page requests. It starts not ready: the web host listens the
/// moment the process is up while migrations and seeding run behind it (<see cref="DatabaseStartupService"/>),
/// and until they finish every page request gets the waiting screen instead of a connection that
/// hangs. It can also go back to not ready: serverless SQL pauses after an hour without
/// connections even while the container stays up, so after a long quiet spell the next page
/// request checks the database before it is let through (<see cref="WakingUpMiddleware"/>).
/// Every request thread reads it, so the fields are read and written atomically.
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
    private volatile bool isReady;
    private bool wakeFailed;
    private long waitingSinceTicks;
    private long lastPageServedTicks;

    public StartupState(TimeProvider clock)
    {
        this.clock = clock;
        waitingSinceTicks = NowTicks;
    }

    public bool IsReady => isReady;

    /// <summary>Seconds since the current wait began, so the waiting page can keep its counter across reloads.</summary>
    public int ElapsedSeconds => (int)TimeSpan.FromTicks(NowTicks - Interlocked.Read(ref waitingSinceTicks)).TotalSeconds;

    /// <summary>Ready, but nobody has loaded a page for long enough that the database may have paused.</summary>
    public bool MayBeAsleep => isReady && NowTicks - Interlocked.Read(ref lastPageServedTicks) > QuietSpell.Ticks;

    private long NowTicks => clock.GetUtcNow().UtcTicks;

    public void MarkReady()
    {
        lock (gate)
        {
            Interlocked.Exchange(ref lastPageServedTicks, NowTicks);
            wakeFailed = false;
            isReady = true;
        }
    }

    public void RecordPageServed() => Interlocked.Exchange(ref lastPageServedTicks, NowTicks);

    /// <summary>Back to not ready while the database is checked. Returns false if another request already did it.</summary>
    public bool BeginWaiting()
    {
        lock (gate)
        {
            if (!isReady)
            {
                return false;
            }

            isReady = false;
            Interlocked.Exchange(ref waitingSinceTicks, NowTicks);
            return true;
        }
    }

    /// <summary>A database check gave up. The next page request starts another rather than waiting on nothing.</summary>
    public void MarkWakeFailed()
    {
        lock (gate)
        {
            wakeFailed = true;
        }
    }

    /// <summary>Claims the retry after a failed check. True for exactly one caller per failure.</summary>
    public bool TakeWakeRetry()
    {
        lock (gate)
        {
            if (isReady || !wakeFailed)
            {
                return false;
            }

            wakeFailed = false;
            return true;
        }
    }
}
