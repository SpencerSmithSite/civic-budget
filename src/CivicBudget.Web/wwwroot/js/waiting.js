// The waiting screen's clock and poll (WakingUpPage), in a file so the Content-Security-Policy, which
// allows this site's scripts and no inline ones, lets it run. It counts on from the seconds the
// server had already waited (data-elapsed), shows the "taking longer" note after two minutes, and
// reloads as soon as /health/startup says the database is ready. Without script, the page's
// <noscript> refresh does the same job more slowly.
(() => {
    const counter = document.getElementById("t");
    const slow = document.getElementById("slow");
    if (!counter) { return; }
    const started = Date.now() - (Number(counter.dataset.elapsed) || 0) * 1000;

    setInterval(() => {
        const seconds = Math.floor((Date.now() - started) / 1000);
        counter.textContent = seconds;
        if (slow && seconds > 120) { slow.style.display = "block"; }
    }, 1000);

    const poll = () => {
        fetch("/health/startup", { cache: "no-store" })
            .then(r => { if (r.ok) { location.reload(); } else { setTimeout(poll, 2000); } })
            .catch(() => setTimeout(poll, 2000));
    };
    setTimeout(poll, 2000);
})();
