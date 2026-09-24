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

        if ((state.MayBeAsleep && state.BeginWaiting()) || state.TakeWakeRetry())
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
/// The waiting screen as one string: the public header, a card in the app's tokens, the mark inline
/// (no request for it), and a few lines of script that count the seconds and poll for ready. The
/// noscript fallback is a plain meta refresh, so a browser without script still gets through.
/// </summary>
public static class WakingUpPage
{
    public static string Render(int elapsedSeconds)
    {
        string elapsed = elapsedSeconds.ToString(CultureInfo.InvariantCulture);
        return Template.Replace("{{elapsed}}", elapsed, StringComparison.Ordinal);
    }

    private const string Template = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>Starting up · CivicBudget</title>
            <link rel="icon" type="image/svg+xml" href="/favicon.svg">
            <noscript><meta http-equiv="refresh" content="5"></noscript>
            <style>
                :root { --navy: #0F2A44; --teal: #0B7285; --teal-2: #1798A8; --text: #1F2937; --muted: #5B6B7B; --canvas: #F6F8FA; --border: #E3E8EF; }
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100vh; display: flex; flex-direction: column; background: var(--canvas); color: var(--text);
                       font-family: "Segoe UI", -apple-system, "Helvetica Neue", Roboto, system-ui, sans-serif; line-height: 1.5; }
                header { background: var(--navy); color: #fff; height: 56px; display: flex; align-items: center; padding: 0 24px; gap: 10px; font-size: 1.25rem; }
                header svg { flex: none; }
                main { flex: 1; display: grid; place-items: center; padding: 32px 16px; }
                .card { background: #fff; border: 1px solid var(--border); border-radius: 12px; padding: 32px; width: 100%; max-width: 440px;
                        box-shadow: 0 1px 2px rgba(15, 42, 68, .06), 0 8px 24px rgba(15, 42, 68, .06); }
                .eyebrow { color: var(--muted); text-transform: uppercase; letter-spacing: .06em; font-size: .75rem; font-weight: 600; margin: 0 0 8px; }
                h1 { font-size: 1.375rem; font-weight: 600; letter-spacing: -0.01em; margin: 0 0 12px; }
                p { margin: 0 0 12px; color: var(--muted); font-size: .9375rem; }
                .bar { height: 6px; border-radius: 3px; background: var(--border); overflow: hidden; margin: 20px 0 10px; }
                .bar i { display: block; height: 100%; width: 40%; border-radius: 3px; background: linear-gradient(90deg, var(--teal-2), var(--teal)); animation: slide 1.6s ease-in-out infinite; }
                @keyframes slide { 0% { transform: translateX(-100%); } 100% { transform: translateX(260%); } }
                @media (prefers-reduced-motion: reduce) { .bar i { animation: none; width: 100%; opacity: .6; } }
                .status { display: flex; justify-content: space-between; font-size: .8125rem; color: var(--muted); font-variant-numeric: tabular-nums; }
                .slow { display: none; margin-top: 16px; padding-top: 16px; border-top: 1px solid var(--border); }
                .slow a { color: var(--teal); }
            </style>
        </head>
        <body>
            <header>
                <svg width="28" height="28" viewBox="0 0 40 40" role="presentation" aria-hidden="true">
                    <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#1798A8"/><stop offset="1" stop-color="#0B7285"/></linearGradient></defs>
                    <rect width="40" height="40" rx="10" fill="url(#g)"/><path d="M20 8.5 8.5 16h23z" fill="#fff"/><rect x="10" y="17.5" width="20" height="2" rx="1" fill="#fff"/>
                    <rect x="11.5" y="21" width="3.5" height="8" rx="0.8" fill="#fff"/><rect x="18.25" y="21" width="3.5" height="8" rx="0.8" fill="#fff"/><rect x="25" y="21" width="3.5" height="8" rx="0.8" fill="#fff"/>
                    <rect x="9" y="30.5" width="22" height="2.5" rx="1" fill="#fff"/>
                </svg>
                <span>CivicBudget</span>
            </header>
            <main>
                <div class="card" role="status" aria-live="polite">
                    <p class="eyebrow">Starting up</p>
                    <h1>Waking up the demo</h1>
                    <p>This demo environment sleeps when nobody is using it. The database is starting now, which usually takes under a minute.</p>
                    <p>You will be taken to the site automatically. After that, pages load normally.</p>
                    <div class="bar"><i></i></div>
                    <div class="status"><span>Preparing the database</span><span><span id="t">{{elapsed}}</span>s</span></div>
                    <p class="slow" id="slow">Taking longer than usual. It is still trying; you can also <a href="">refresh</a>.</p>
                </div>
            </main>
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
        </body>
        </html>
        """;
}
