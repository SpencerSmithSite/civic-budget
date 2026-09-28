// The browser's idle clock for a signed-in page (see SessionPolicy). Runs only where the page says
// how long a session lasts (data-idle-minutes on <body>): not for a remembered device, and not on the
// public portal. Activity in any tab of the app counts, through localStorage.
(() => {
    const minutes = Number(document.body.dataset.idleMinutes);
    if (!minutes) { return; }
    const limit = minutes * 60_000, warnAt = limit - 120_000, pingEvery = 5 * 60_000;
    const key = "civicBudget.lastActivity";
    const read = () => { try { return Number(localStorage.getItem(key)) || Date.now(); } catch { return lastActivity; } };
    let lastActivity = Date.now(), lastPing = Date.now(), banner = null;

    const touch = () => {
        lastActivity = Date.now();
        try { localStorage.setItem(key, String(lastActivity)); } catch { /* private mode: this tab only */ }
        if (banner) { banner.remove(); banner = null; }
        if (lastActivity - lastPing > pingEvery) {
            lastPing = lastActivity;
            fetch("Account/KeepAlive", { credentials: "same-origin" }).catch(() => { });
        }
    };
    let throttle = 0;
    const onActivity = () => { const now = Date.now(); if (now - throttle > 15_000) { throttle = now; touch(); } };
    ["keydown", "pointerdown", "wheel", "touchstart"].forEach(e => document.addEventListener(e, onActivity, { passive: true, capture: true }));
    document.addEventListener("pointermove", onActivity, { passive: true });

    const warn = (left) => {
        if (!banner) {
            banner = document.createElement("div");
            banner.className = "cb-idle-warning";
            banner.setAttribute("role", "alertdialog");
            banner.setAttribute("aria-live", "assertive");
            banner.innerHTML = '<span class="cb-idle-text"></span> <button type="button" class="btn btn-primary btn-sm">Stay signed in</button>';
            banner.querySelector("button").addEventListener("click", () => { throttle = 0; lastPing = 0; touch(); });
            document.body.appendChild(banner);
            banner.querySelector("button").focus();
        }
        const s = Math.max(0, Math.ceil(left / 1000));
        banner.querySelector(".cb-idle-text").textContent =
            `For your security, you will be signed out in ${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")} unless you continue.`;
    };

    setInterval(() => {
        const idle = Date.now() - Math.max(lastActivity, read());
        if (idle >= limit) { location.href = "Account/SessionExpired"; }
        else if (idle >= warnAt) { warn(limit - idle); }
        else if (banner) { banner.remove(); banner = null; }
    }, 1000);
    touch();
})();
