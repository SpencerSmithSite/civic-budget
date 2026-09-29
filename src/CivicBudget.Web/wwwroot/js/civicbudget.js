// The admin app's own JavaScript, all of it. setCookie remembers a UI preference (the collapsed
// menu) so the server can prerender the page the way the user left it.
window.civicBudget = {
    setCookie: (name, value) => { document.cookie = `${name}=${value};path=/;max-age=31536000;SameSite=Lax`; },
    // Dialogs and drawers: while one is open, Tab and Shift+Tab cycle inside it (aria-modal says
    // the page behind is out of reach, so the keyboard must agree); when it closes, focus goes
    // back to the button that opened it. A stack, because a confirm dialog can open over the drawer.
    // An item in a dropdown menu is hidden by the time the dialog it opened closes, so its menu's
    // toggle button is remembered instead. focusFirst moves focus in for boxes that do not do it
    // themselves (the phone menu).
    modals: [],
    focusable: 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    openModal: (box, focusFirst) => {
        const cb = window.civicBudget;
        // By the time a dialog opened from a dropdown item renders, Bootstrap has hidden the menu and
        // focus has fallen to the body, so the last element that had focus stands in.
        let opener = document.activeElement;
        if (!opener || opener === document.body) { opener = cb.lastFocus; }
        const menu = opener && opener.closest ? opener.closest(".dropdown-menu") : null;
        const toggle = menu && menu.parentElement ? menu.parentElement.querySelector('[data-bs-toggle="dropdown"]') : null;
        if (toggle) { opener = toggle; }
        cb.modals.push({ box, opener });
        if (focusFirst) {
            const first = [...box.querySelectorAll(cb.focusable)].find(el => el.offsetParent !== null);
            (first || box).focus();
        }
        if (!cb.onTab) {
            cb.onTab = (e) => {
                const top = cb.modals[cb.modals.length - 1];
                if (e.key !== "Tab" || !top || !top.box.isConnected) { return; }
                const items = [...top.box.querySelectorAll(cb.focusable)].filter(el => el.offsetParent !== null);
                if (items.length === 0) { e.preventDefault(); top.box.focus(); return; }
                const first = items[0], last = items[items.length - 1], inside = top.box.contains(document.activeElement);
                if (e.shiftKey && (!inside || document.activeElement === first)) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && (!inside || document.activeElement === last)) { e.preventDefault(); first.focus(); }
            };
            document.addEventListener("keydown", cb.onTab, true);
        }
    },
    focusId: (id) => { const el = document.getElementById(id); if (el) { el.focus(); } },
    focusInside: (box) => { if (box && !box.contains(document.activeElement)) { box.focus(); } },
    // Escape dismisses an info tip that is open by hover or focus (WCAG 1.4.13); it shows again once
    // the pointer or focus has left it. Set up once, for every tip, by the first call below.
    initTips: () => {
        if (window.civicBudget.tipsReady) { return; }
        window.civicBudget.tipsReady = true;
        document.addEventListener("keydown", (e) => {
            if (e.key !== "Escape") { return; }
            document.querySelectorAll(".cb-tip:hover, .cb-tip:focus").forEach(t => t.classList.add("cb-tip-dismissed"));
        });
        const reset = (e) => { const t = e.target.closest && e.target.closest(".cb-tip"); if (t) { t.classList.remove("cb-tip-dismissed"); } };
        document.addEventListener("mouseout", reset);
        document.addEventListener("focusout", reset);
    },
    // refocus is false when the box closed because the user navigated: the new page takes focus.
    // An opener that is gone or hidden hands focus to the page's heading rather than to nothing.
    closeModal: (refocus = true) => {
        const closed = window.civicBudget.modals.pop();
        if (!refocus) { return; }
        const el = closed && closed.opener;
        if (el && el.isConnected && el.offsetParent !== null && typeof el.focus === "function") { el.focus(); return; }
        const fallback = document.querySelector("#main h1") || document.getElementById("main");
        if (fallback) {
            if (!fallback.hasAttribute("tabindex")) { fallback.setAttribute("tabindex", "-1"); }
            fallback.focus();
        }
    }
};
// A table wider than its card scrolls inside its wrapper; a keyboard user can only scroll it if the
// wrapper can take focus (WCAG 2.1.1). Any wrapper that overflows becomes a named, focusable region,
// and stops being one when it no longer scrolls. Checked after Blazor changes the page and on resize.
// Two tables can share a name (each department's "1000 General Fund"); the second and later take
// their section's heading too, so every region is told apart.
window.civicBudget.scrollRegions = () => {
    const used = new Set([...document.querySelectorAll('[role="region"][aria-label]')].map(r => r.getAttribute("aria-label")));
    document.querySelectorAll(".cb-grid-wrap").forEach(wrap => {
        const scrolls = wrap.scrollWidth > wrap.clientWidth + 1;
        const ours = wrap.dataset.cbRegion === "1";
        if (scrolls && !wrap.hasAttribute("tabindex")) {
            const table = wrap.querySelector("table");
            const name = table && (table.getAttribute("aria-label") || (table.caption && table.caption.textContent.trim()));
            let label = name ? `Table: ${name}` : "Table";
            if (used.has(label)) {
                const heading = wrap.closest("section") && wrap.closest("section").querySelector("h2, h3");
                if (heading) { label = `${label}, ${heading.textContent.trim()}`; }
            }
            used.add(label);
            wrap.setAttribute("tabindex", "0");
            wrap.setAttribute("role", "region");
            wrap.setAttribute("aria-label", label);
            wrap.dataset.cbRegion = "1";
        } else if (!scrolls && ours) {
            wrap.removeAttribute("tabindex"); wrap.removeAttribute("role"); wrap.removeAttribute("aria-label");
            delete wrap.dataset.cbRegion;
        }
    });
};
{
    let pending = 0;
    const later = () => { cancelAnimationFrame(pending); pending = requestAnimationFrame(window.civicBudget.scrollRegions); };
    new MutationObserver(later).observe(document.documentElement, { childList: true, subtree: true });
    addEventListener("resize", later);
}
window.civicBudget.initTips();
document.addEventListener("focusin", (e) => { if (e.target !== document.body) { window.civicBudget.lastFocus = e.target; } });
