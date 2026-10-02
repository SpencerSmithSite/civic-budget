using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace CivicBudget.Web.Startup;

/// <summary>
/// While the database is not ready, answers every page request with a small self-contained screen
/// that explains the wait and reloads on its own once <c>/health/startup</c> reports ready. That
/// covers the first start and a database that paused while the container stayed up: after a quiet
/// spell the next page request checks the database first, and only gets the screen if the check
/// takes longer than a moment (an awake database answers in milliseconds). Health endpoints and
/// static assets pass through: the probes must see the process, and the screen carries its own
/// styles. It sends 503 with Retry-After, the honest status for "not yet", so crawlers and uptime
/// monitors do not cache the waiting screen as the site.
/// </summary>
public sealed class WakingUpMiddleware(RequestDelegate next, StartupState state, IDatabaseWaker waker)
{
    /// <summary>How long a page request waits on the database check before showing the screen instead.</summary>
    public static readonly TimeSpan CheckGrace = TimeSpan.FromSeconds(1);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (state.ClaimWake())
        {
            Task wake = waker.WakeAsync();
            await Task.WhenAny(wake, Task.Delay(CheckGrace, context.RequestAborted));
        }

        if (!state.IsReady)
        {
            HttpResponse response = context.Response;
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            response.Headers[HeaderNames.RetryAfter] = "5";
            response.Headers[HeaderNames.CacheControl] = "no-store";
            response.ContentType = "text/html; charset=utf-8";
            await response.WriteAsync(WakingUpPage.Render(state.ElapsedSeconds), context.RequestAborted);
            return;
        }

        state.RecordPageServed();
        await next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || StaticAssetPath.IsStaticAsset(path);
}

/// <summary>
/// The waiting screen on the shared <see cref="PlainPage"/> shell, plus a few lines of script that
/// count the seconds and poll for ready. The noscript fallback is a plain meta refresh, so a browser
/// without script still gets through.
/// </summary>
public static class WakingUpPage
{
    public static string Render(int elapsedSeconds)
    {
        string elapsed = elapsedSeconds.ToString(CultureInfo.InvariantCulture);
        return PlainPage.Render(
            "Starting up · CivicBudget",
            Card.Replace("{{elapsed}}", elapsed, StringComparison.Ordinal),
            Styles,
            head: """<noscript><meta http-equiv="refresh" content="5"></noscript>""",
            script: Script.Replace("{{elapsed}}", elapsed, StringComparison.Ordinal));
    }

    private const string Styles = """
        .bar { height: 6px; border-radius: 3px; background: var(--border); overflow: hidden; margin: 20px 0 10px; }
        .bar i { display: block; height: 100%; width: 40%; border-radius: 3px; background: linear-gradient(90deg, var(--teal-2), var(--teal)); animation: slide 1.6s ease-in-out infinite; }
        @keyframes slide { 0% { transform: translateX(-100%); } 100% { transform: translateX(260%); } }
        @media (prefers-reduced-motion: reduce) { .bar i { animation: none; width: 100%; opacity: .6; } }
        .status { display: flex; justify-content: space-between; font-size: .8125rem; color: var(--muted); font-variant-numeric: tabular-nums; }
        .slow { display: none; margin-top: 16px; padding-top: 16px; border-top: 1px solid var(--border); }
        .slow a { color: var(--teal); }
        """;

    private const string Card = """
        <div class="card" role="status" aria-live="polite">
            <p class="eyebrow">Starting up</p>
            <h1>Waking up the demo</h1>
            <p>This demo environment sleeps when nobody is using it. The database is starting now, which usually takes under a minute.</p>
            <p>You will be taken to the site automatically. After that, pages load normally.</p>
            <div class="bar"><i></i></div>
            <div class="status"><span>Preparing the database</span><span><span id="t">{{elapsed}}</span>s</span></div>
            <p class="slow" id="slow">Taking longer than usual. It is still trying; you can also <a href="">refresh</a>.</p>
        </div>
        """;

    private const string Script = """
        <script>
            (function () {
                var started = Date.now() - {{elapsed}} * 1000;
                var t = document.getElementById("t"), slow = document.getElementById("slow");
                setInterval(function () {
                    var s = Math.floor((Date.now() - started) / 1000);
                    t.textContent = s;
                    if (s > 120) { slow.style.display = "block"; }
                }, 1000);
                function poll() {
                    fetch("/health/startup", { cache: "no-store" })
                        .then(function (r) { if (r.ok) { location.reload(); } else { setTimeout(poll, 2000); } })
                        .catch(function () { setTimeout(poll, 2000); });
                }
                setTimeout(poll, 2000);
            })();
        </script>
        """;
}
