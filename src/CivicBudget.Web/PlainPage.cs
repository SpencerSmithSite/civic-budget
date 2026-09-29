namespace CivicBudget.Web;

/// <summary>
/// The shell of the pages the app answers without Blazor: the waiting screen while the database
/// starts (<see cref="Startup.WakingUpPage"/>) and the page for a form whose token no longer checks
/// out (<see cref="Security.ExpiredFormPage"/>). Neither can use the layout, one because the
/// database is not ready and the other because it stands in for an endpoint that refused the
/// request, so each is one self-contained string: the public header with the mark inline (no
/// request for it) and a card in the app's tokens. Callers pass trusted markup; anything that came
/// from the request must be encoded before it gets here.
/// </summary>
public static class PlainPage
{
    public static string Render(string title, string card, string styles = "", string head = "", string script = "") => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{title}}</title>
            <link rel="icon" type="image/svg+xml" href="/favicon.svg">
            {{head}}
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
                {{styles}}
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
                {{card}}
            </main>
            {{script}}
        </body>
        </html>
        """;
}
