// Show/hide buttons on password fields (the PasswordInput component). One listener on the document
// handles every button, including ones Blazor renders after this script runs. The "cb-js" class
// reveals the buttons, which stay hidden without script so a page never offers a dead control.
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
        button.setAttribute("aria-label", show ? "Hide password" : "Show password");
        var icon = button.querySelector(".bi");
        if (icon) { icon.className = "bi " + (show ? "bi-eye-slash" : "bi-eye"); }
        input.focus();
    });
})();
