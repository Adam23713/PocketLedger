// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    const storageKey = "pocketledger-theme";
    const selector = document.getElementById("theme-selector");
    const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");

    function applyTheme(preference) {
        const theme = preference === "system" ? (colorScheme.matches ? "dark" : "light") : preference;
        document.documentElement.setAttribute("data-bs-theme", theme);
    }

    const savedTheme = localStorage.getItem(storageKey) ?? "system";
    if (selector) {
        selector.value = ["system", "light", "dark"].includes(savedTheme) ? savedTheme : "system";
        selector.addEventListener("change", () => {
            localStorage.setItem(storageKey, selector.value);
            applyTheme(selector.value);
        });
    }

    colorScheme.addEventListener("change", () => {
        if ((localStorage.getItem(storageKey) ?? "system") === "system") applyTheme("system");
    });
})();
