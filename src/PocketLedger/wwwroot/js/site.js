(() => {
    const storageKey = "pocketledger-theme";
    const themes = ["apple", "banking", "glass", "material"];
    const themeMenu = document.getElementById("theme-menu");
    const themeButton = document.getElementById("theme-menu-button");

    function applyTheme(theme) {
        const selectedTheme = themes.includes(theme) ? theme : "apple";
        document.documentElement.dataset.theme = selectedTheme;
        document.documentElement.setAttribute("data-bs-theme", selectedTheme === "banking" || selectedTheme === "glass" ? "dark" : "light");
        document.querySelectorAll("[data-theme-value]").forEach(item => {
            item.setAttribute("aria-checked", String(item.dataset.themeValue === selectedTheme));
        });
    }

    function closeMenu(menu, button, restoreFocus = false) {
        menu.hidden = true;
        button.setAttribute("aria-expanded", "false");
        if (restoreFocus) button.focus();
    }

    function setupMenu(button, menu) {
        const items = () => [...menu.querySelectorAll('[role="menuitem"], [role="menuitemradio"]')];
        button.addEventListener("click", () => {
            const opening = menu.hidden;
            document.querySelectorAll(".app-dropdown:not([hidden])").forEach(openMenu => {
                if (openMenu !== menu) closeMenu(openMenu, document.querySelector(`[aria-controls="${openMenu.id}"]`));
            });
            menu.hidden = !opening;
            button.setAttribute("aria-expanded", String(opening));
            if (opening) requestAnimationFrame(() => items()[0]?.focus());
        });
        menu.addEventListener("keydown", event => {
            const menuItems = items();
            const currentIndex = menuItems.indexOf(document.activeElement);
            if (event.key === "Escape") {
                event.preventDefault();
                closeMenu(menu, button, true);
            } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                const direction = event.key === "ArrowDown" ? 1 : -1;
                menuItems[(currentIndex + direction + menuItems.length) % menuItems.length]?.focus();
            } else if (event.key === "Home" || event.key === "End") {
                event.preventDefault();
                menuItems[event.key === "Home" ? 0 : menuItems.length - 1]?.focus();
            }
        });
        document.addEventListener("pointerdown", event => {
            if (!menu.hidden && !menu.contains(event.target) && !button.contains(event.target)) closeMenu(menu, button);
        });
    }

    applyTheme(localStorage.getItem(storageKey) ?? "apple");
    if (themeButton && themeMenu) {
        setupMenu(themeButton, themeMenu);
        themeMenu.addEventListener("click", event => {
            const item = event.target.closest("[data-theme-value]");
            if (!item) return;
            localStorage.setItem(storageKey, item.dataset.themeValue);
            applyTheme(item.dataset.themeValue);
            closeMenu(themeMenu, themeButton, true);
        });
    }

    const profileButton = document.getElementById("profile-menu-button");
    const profileMenu = document.getElementById("profile-menu");
    if (profileButton && profileMenu) setupMenu(profileButton, profileMenu);
})();
