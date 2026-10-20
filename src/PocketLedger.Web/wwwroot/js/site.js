(() => {
    const themeMenu = document.getElementById("theme-menu");
    const themeButton = document.getElementById("theme-menu-button");

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

    PocketLedgerTheme.apply(PocketLedgerTheme.read());
    if (themeButton && themeMenu) {
        setupMenu(themeButton, themeMenu);
        themeMenu.addEventListener("click", event => {
            const item = event.target.closest("[data-theme-value]");
            if (!item) return;
            PocketLedgerTheme.select(item.dataset.themeValue);
            closeMenu(themeMenu, themeButton, true);
        });
    }

    const profileButton = document.getElementById("profile-menu-button");
    const profileMenu = document.getElementById("profile-menu");
    if (profileButton && profileMenu) setupMenu(profileButton, profileMenu);

    const dashboardPrivacyButton = document.getElementById("dashboard-privacy-button");
    if (dashboardPrivacyButton) {
        const privacyStorageKey = "pocketledger-dashboard-amounts-hidden";
        const applyDashboardPrivacy = hidden => {
            if (hidden) document.documentElement.dataset.dashboardAmountsHidden = "true";
            else delete document.documentElement.dataset.dashboardAmountsHidden;
            const label = hidden ? "Show dashboard amounts" : "Hide dashboard amounts";
            dashboardPrivacyButton.setAttribute("aria-label", label);
            dashboardPrivacyButton.setAttribute("aria-pressed", String(hidden));
            dashboardPrivacyButton.title = label;
        };

        applyDashboardPrivacy(localStorage.getItem(privacyStorageKey) === "true");
        dashboardPrivacyButton.addEventListener("click", () => {
            const hidden = document.documentElement.dataset.dashboardAmountsHidden !== "true";
            localStorage.setItem(privacyStorageKey, String(hidden));
            applyDashboardPrivacy(hidden);
        });
    }

    const accountFilters = document.getElementById("dashboard-account-filters");
    if (accountFilters) {
        const storageKey = "pocketledger-dashboard-account-types";
        const filterButtons = [...accountFilters.querySelectorAll("[data-account-filter]")];
        const typeButtons = filterButtons.filter(button => button.dataset.accountFilter !== "all");
        const accountCards = [...document.querySelectorAll("[data-account-card]")];
        const emptyState = document.getElementById("dashboard-account-filter-empty");
        const availableTypes = new Set(typeButtons.map(button => button.dataset.accountFilter));
        let selectedTypes = new Set(availableTypes);

        try {
            const storedTypes = JSON.parse(localStorage.getItem(storageKey));
            if (Array.isArray(storedTypes)) selectedTypes = new Set(storedTypes.filter(type => availableTypes.has(type)));
        } catch {
            localStorage.removeItem(storageKey);
        }

        const applyAccountFilters = () => {
            let visibleCount = 0;
            accountCards.forEach(card => {
                card.hidden = !selectedTypes.has(card.dataset.accountType);
                if (!card.hidden) visibleCount++;
            });
            typeButtons.forEach(button => {
                const active = selectedTypes.has(button.dataset.accountFilter);
                button.classList.toggle("active", active);
                button.setAttribute("aria-pressed", String(active));
            });
            const allButton = filterButtons.find(button => button.dataset.accountFilter === "all");
            const allActive = selectedTypes.size === availableTypes.size;
            allButton.classList.toggle("active", allActive);
            allButton.setAttribute("aria-pressed", String(allActive));
            emptyState.hidden = visibleCount > 0;
        };

        accountFilters.addEventListener("click", event => {
            const button = event.target.closest("[data-account-filter]");
            if (!button) return;
            const filter = button.dataset.accountFilter;
            if (filter === "all") selectedTypes = new Set(availableTypes);
            else if (selectedTypes.has(filter)) selectedTypes.delete(filter);
            else selectedTypes.add(filter);
            localStorage.setItem(storageKey, JSON.stringify([...selectedTypes]));
            applyAccountFilters();
        });

        applyAccountFilters();
    }
})();
