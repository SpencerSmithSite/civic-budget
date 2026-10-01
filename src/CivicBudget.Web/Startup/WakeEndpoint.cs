namespace CivicBudget.Web.Startup;

/// <summary>
/// <c>POST /health/wake</c>: lets the product site start the demo waking while a visitor is still
/// reading about it. Any request starts a container that scaled to zero, and starting it wakes the
/// database too; this endpoint also covers a running container whose database paused, by starting
/// the same check the next page request would (<see cref="StartupState.ClaimWake"/>). It answers at
/// once and never opens a connection on the request. The check runs only when one is due, so calling
/// it often keeps the database awake no longer than page views already could (ADR-0031). A POST, so
/// crawlers and link previews never trigger it.
/// </summary>
public static class WakeEndpoint
{
    public const string Path = "/health/wake";

    /// <summary>202 when a database check started, 204 when there was nothing to wake.</summary>
    public static IResult Wake(StartupState state, IDatabaseWaker waker)
    {
        if (!state.ClaimWake())
        {
            return Results.NoContent();
        }

        // The check runs on its own task and handles its own failures, so the request does not wait.
        _ = waker.WakeAsync();
        return Results.Accepted();
    }
}
