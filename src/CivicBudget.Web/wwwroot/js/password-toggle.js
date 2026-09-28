// The account pages' two small behaviors. Both are static pages, so a document-level script does
// this rather than Blazor.
//
// Show/hide on password fields (the PasswordInput component). One listener on the document handles
// every button, including ones Blazor renders after this script runs. The "cb-js" class reveals the
// buttons, which stay hidden without script so a page never offers a dead control. The button keeps
// one name, "Show password", and aria-pressed says whether it is on; changing the name as well would
// read as "Hide password, pressed" while the password is showing. Focus stays on the button so the
// new state is announced.
//
// A form posted back with errors (the FormErrors component) focuses its summary on load, so a screen
// reader starts with what went wrong instead of the top of the page.
(function () {
    document.documentElement.classList.add("cb-js");
    document.addEventListener("click", function (e) {
        var button = e.target.closest("[data-password-toggle]");
        if (!button) { return; }
        var input = document.getElementById(button.getAttribute("aria-controls"));
        if (!input) { return; }
        var show = input.type === "password";
        input.type = show ? "text" : "password";
        button.setAttribute("aria-pressed", show ? "true" : "false");
        var icon = button.querySelector(".bi");
        if (icon) { icon.className = "bi " + (show ? "bi-eye-slash" : "bi-eye"); }
    });

    var errors = document.querySelector("[data-focus-on-load]");
    if (errors) { errors.focus(); }
})();
