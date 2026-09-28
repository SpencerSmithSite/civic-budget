// The admin app's own JavaScript, all of it. setCookie remembers a UI preference (the collapsed
// menu) so the server can prerender the page the way the user left it.
window.civicBudget = {
    setCookie: (name, value) => { document.cookie = `${name}=${value};path=/;max-age=31536000;SameSite=Lax`; },
    // Dialogs and drawers: while one is open, Tab and Shift+Tab cycle inside it (aria-modal says
    // the page behind is out of reach, so the keyboard must agree); when it closes, focus goes
    // back to the button that opened it. A stack, because a confirm dialog can open over the drawer.
    modals: [],
    openModal: (box) => {
        const cb = window.civicBudget;
        cb.modals.push({ box, opener: document.activeElement });
        if (!cb.onTab) {
            cb.onTab = (e) => {
                const top = cb.modals[cb.modals.length - 1];
                if (e.key !== "Tab" || !top || !top.box.isConnected) { return; }
                const items = [...top.box.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
                    .filter(el => el.offsetParent !== null);
                if (items.length === 0) { e.preventDefault(); top.box.focus(); return; }
                const first = items[0], last = items[items.length - 1], inside = top.box.contains(document.activeElement);
                if (e.shiftKey && (!inside || document.activeElement === first)) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && (!inside || document.activeElement === last)) { e.preventDefault(); first.focus(); }
            };
            document.addEventListener("keydown", cb.onTab, true);
        }
    },
    closeModal: () => {
        const closed = window.civicBudget.modals.pop();
        const el = closed && closed.opener;
        if (el && el.isConnected && typeof el.focus === "function") { el.focus(); }
    }
};
