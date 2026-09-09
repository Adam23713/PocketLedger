(() => {
    let closeActive = () => {};
    for (const id of ["planner-account", "planner-target"]) {
        const select = document.getElementById(id);
        if (!select) continue;
        const wrapper = document.createElement("div");
        wrapper.className = "planner-select";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "form-select";
        button.id = `${id}-button`;
        button.setAttribute("role", "combobox");
        button.setAttribute("aria-haspopup", "listbox");
        button.setAttribute("aria-expanded", "false");
        const label = document.querySelector(`label[for="${id}"]`);
        if (label) label.htmlFor = button.id;
        button.setAttribute("aria-label", label?.textContent ?? id);
        if (select.required) button.setAttribute("aria-required", "true");
        const list = document.createElement("div");
        list.id = `${id}-options`;
        list.className = "planner-select-options";
        list.setAttribute("role", "listbox");
        list.hidden = true;
        button.setAttribute("aria-controls", list.id);
        select.before(wrapper);
        wrapper.append(button, list, select);
        select.hidden = true;
        let active = -1;
        let options = [];
        function close() {
            list.hidden = true;
            button.setAttribute("aria-expanded", "false");
            button.removeAttribute("aria-activedescendant");
        }
        function highlight(index) {
            active = index;
            [...list.children].forEach((item, i) => item.classList.toggle("active", i === active));
            if (list.children[active]) {
                button.setAttribute("aria-activedescendant", list.children[active].id);
                list.children[active].scrollIntoView({ block: "nearest" });
            }
        }
        function choose(index) {
            if (!options[index]) return;
            select.value = options[index].value;
            select.dispatchEvent(new Event("change", { bubbles: true }));
            close();
            button.focus();
        }
        function open() {
            closeActive();
            closeActive = close;
            options = [...select.options].filter(option => !option.disabled && !option.hidden);
            list.replaceChildren();
            options.forEach((option, index) => {
                const item = document.createElement("div");
                item.id = `${id}-option-${index}`;
                item.setAttribute("role", "option");
                item.setAttribute("aria-selected", String(option.selected));
                item.textContent = option.textContent;
                item.addEventListener("mousedown", event => event.preventDefault());
                item.addEventListener("click", () => choose(index));
                list.append(item);
            });
            list.hidden = false;
            button.setAttribute("aria-expanded", "true");
            highlight(Math.max(0, options.findIndex(option => option.selected)));
        }
        button.addEventListener("click", () => list.hidden ? open() : close());
        button.addEventListener("keydown", event => {
            if (event.key === "Escape") { event.preventDefault(); close(); return; }
            if (event.key === "Tab") { close(); return; }
            if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
                event.preventDefault();
                if (list.hidden) open();
                else highlight(event.key === "Home" ? 0 : event.key === "End" ? options.length - 1 : Math.max(0, Math.min(options.length - 1, active + (event.key === "ArrowDown" ? 1 : -1))));
            } else if (["Enter", " "].includes(event.key)) {
                event.preventDefault();
                if (list.hidden) open(); else choose(active);
            } else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
                if (list.hidden) open();
                const index = options.findIndex(option => option.textContent.toLocaleLowerCase().startsWith(event.key.toLocaleLowerCase()));
                if (index >= 0) highlight(index);
            }
        });
        document.addEventListener("pointerdown", event => { if (!wrapper.contains(event.target)) close(); });
        button.addEventListener("blur", close);
        function refresh() { button.textContent = select.selectedOptions[0]?.textContent ?? "Select"; }
        select.addEventListener("change", refresh);
        select.addEventListener("invalid", event => { event.preventDefault(); button.focus(); button.setAttribute("aria-invalid", "true"); });
        select.addEventListener("change", () => button.removeAttribute("aria-invalid"));
        refresh();
    }
})();
