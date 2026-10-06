(() => {
    const themeMenu = document.getElementById("theme-menu");
    const themeButton = document.getElementById("theme-menu-button");
    PocketLedgerTheme.apply(PocketLedgerTheme.read());
    if (!themeButton || !themeMenu) return;

    const items = () => [...themeMenu.querySelectorAll('[role="menuitemradio"]')];
    const closeMenu = (restoreFocus = false) => {
        themeMenu.hidden = true;
        themeButton.setAttribute("aria-expanded", "false");
        if (restoreFocus) themeButton.focus();
    };

    themeButton.addEventListener("click", () => {
        const opening = themeMenu.hidden;
        themeMenu.hidden = !opening;
        themeButton.setAttribute("aria-expanded", String(opening));
        if (opening) requestAnimationFrame(() => items()[0]?.focus());
    });
    themeMenu.addEventListener("click", event => {
        const item = event.target.closest("[data-theme-value]");
        if (!item) return;
        PocketLedgerTheme.select(item.dataset.themeValue);
        closeMenu(true);
    });
    themeMenu.addEventListener("keydown", event => {
        const menuItems = items();
        const currentIndex = menuItems.indexOf(document.activeElement);
        if (event.key === "Escape") {
            event.preventDefault();
            closeMenu(true);
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
        if (!themeMenu.hidden && !themeMenu.contains(event.target) && !themeButton.contains(event.target)) closeMenu();
    });
})();
