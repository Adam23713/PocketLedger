(() => {
    const cookieName = "pocketledger-theme";
    const legacyStorageKey = "pocketledger-theme";
    const themes = ["horizon", "banking", "glass", "material"];
    const config = window.pocketLedgerThemeConfig ?? {};

    function isSupported(theme) {
        return themes.includes(theme);
    }

    function readCookie() {
        const prefix = `${cookieName}=`;
        const value = document.cookie.split("; ").find(cookie => cookie.startsWith(prefix))?.slice(prefix.length);
        if (!value) return null;
        try {
            return decodeURIComponent(value);
        } catch {
            return null;
        }
    }

    function writeCookie(theme) {
        const selectedTheme = isSupported(theme) ? theme : "horizon";
        const attributes = [`${cookieName}=${encodeURIComponent(selectedTheme)}`, "Path=/", "SameSite=Lax", "Max-Age=31536000"];
        if (config.cookieDomain) attributes.push(`Domain=${config.cookieDomain}`);
        if (config.secure) attributes.push("Secure");
        document.cookie = attributes.join("; ");
        return selectedTheme;
    }

    function apply(theme) {
        const selectedTheme = isSupported(theme) ? theme : "horizon";
        document.documentElement.dataset.theme = selectedTheme;
        document.documentElement.setAttribute("data-bs-theme", selectedTheme === "banking" || selectedTheme === "glass" ? "dark" : "light");
        document.querySelectorAll("[data-theme-value]").forEach(item => {
            item.setAttribute("aria-checked", String(item.dataset.themeValue === selectedTheme));
        });
        return selectedTheme;
    }

    function read() {
        const cookieTheme = readCookie();
        if (isSupported(cookieTheme)) return cookieTheme;

        if (!cookieTheme && config.migrateLegacyStorage) {
            const legacyTheme = localStorage.getItem(legacyStorageKey);
            if (isSupported(legacyTheme)) return writeCookie(legacyTheme);
        }

        return "horizon";
    }

    function select(theme) {
        return apply(writeCookie(theme));
    }

    window.PocketLedgerTheme = { apply, read, select };
    apply(read());
})();
