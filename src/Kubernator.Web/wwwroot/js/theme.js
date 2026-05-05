(function () {
    var STORAGE_KEY = "kubernator.theme";
    var html = document.documentElement;

    function currentTheme() {
        var stored = localStorage.getItem(STORAGE_KEY);
        if (stored) return stored;
        return matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
    }

    function applyTheme(t) {
        if (t === "light" || t === "dark") html.setAttribute("data-theme", t);
        else html.removeAttribute("data-theme");
        var btn = document.querySelector("[data-theme-toggle]");
        if (btn) {
            var current = currentTheme();
            var label = btn.querySelector("[data-theme-label]");
            if (label) label.textContent = current === "dark" ? "dark" : "light";
            var sun = btn.querySelector("[data-theme-icon-sun]");
            var moon = btn.querySelector("[data-theme-icon-moon]");
            if (sun) sun.style.display = current === "light" ? "" : "none";
            if (moon) moon.style.display = current === "dark" ? "" : "none";
        }
    }

    var initial = localStorage.getItem(STORAGE_KEY);
    if (initial) applyTheme(initial);
    else applyTheme(currentTheme());

    document.addEventListener("click", function (e) {
        var t = e.target.closest("[data-theme-toggle]");
        if (!t) return;
        var next = currentTheme() === "dark" ? "light" : "dark";
        localStorage.setItem(STORAGE_KEY, next);
        applyTheme(next);
    });

    document.addEventListener("DOMContentLoaded", function () { applyTheme(currentTheme()); });
    if (window.Blazor) {
        window.addEventListener("load", function () { applyTheme(currentTheme()); });
    }
})();
