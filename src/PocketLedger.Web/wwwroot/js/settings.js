(() => {
    const list = document.querySelector("[data-currency-list]");
    const editors = document.querySelector("[data-currency-editors]");
    if (!list || !editors) return;
    const addSelect = document.querySelector("[data-currency-add]");
    const addButton = document.querySelector("[data-currency-add-button]");
    const defaultSelect = document.querySelector("[data-default-currency]");
    const template = document.getElementById("currency-format-template");
    const empty = editors.querySelector("[data-currency-empty]");
    let nextKey = editors.querySelectorAll("[data-currency-format]").length;

    const editorFor = code => editors.querySelector(`[data-currency-format][data-currency="${code}"]`);
    const itemFor = code => list.querySelector(`[data-currency-item="${code}"]`);
    const addOptionFor = code => addSelect.querySelector(`option[value="${code}"]`);

    function initializeEditor(editor) {
        const field = name => editor.querySelector(`[data-format-field="${name}"]`);
        function update() {
            const decimalPlaces = Number(field("decimals").value);
            const digits = "1234567".replace(/\B(?=(\d{3})+(?!\d))/g, field("thousands-separator").value) + (decimalPlaces ? field("decimal-separator").value + "89".padEnd(decimalPlaces, "0").slice(0, decimalPlaces) : "");
            const marker = field("display").value === "Symbol" ? editor.dataset.symbol : editor.dataset.currency;
            const space = field("space").checked ? " " : "";
            editor.querySelector("[data-format-preview]").textContent = field("position").value === "Before" ? marker + space + digits : digits + space + marker;
        }
        editor.querySelectorAll("[data-format-field]").forEach(input => input.addEventListener("input", update));
        editor.querySelector("[data-currency-remove]").addEventListener("click", () => remove(editor.dataset.currency));
        update();
    }

    function select(code) {
        list.querySelectorAll("[data-currency-item]").forEach(item => item.classList.toggle("active", item.dataset.currencyItem === code));
        editors.querySelectorAll("[data-currency-format]").forEach(editor => editor.hidden = editor.dataset.currency !== code);
        empty.hidden = Boolean(editorFor(code));
    }

    function refreshDefault() {
        list.querySelectorAll("[data-currency-item]").forEach(item => item.querySelector("[data-default-badge]").hidden = item.dataset.currencyItem !== defaultSelect.value);
        editors.querySelectorAll("[data-currency-format]").forEach(editor => editor.querySelector("[data-currency-remove]").hidden = editor.dataset.currency === defaultSelect.value);
    }

    function add(code) {
        if (!code || editorFor(code)) { select(code); return; }
        const option = addOptionFor(code);
        const escape = value => value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
        const html = template.innerHTML
            .replaceAll("__key__", String(nextKey++))
            .replaceAll("__code__", escape(code))
            .replaceAll("__name__", escape(option.dataset.name))
            .replaceAll("__symbol__", escape(option.dataset.symbol));
        editors.insertAdjacentHTML("beforeend", html);
        const editor = editors.lastElementChild;
        editor.querySelector('[data-format-field="decimals"]').value = option.dataset.decimals;

        const item = document.createElement("button");
        item.type = "button";
        item.className = "list-group-item list-group-item-action d-flex justify-content-between align-items-center gap-2";
        item.dataset.currencyItem = code;
        item.innerHTML = `<span><strong>${escape(code)}</strong> <small class="text-muted">${escape(option.dataset.name)}</small></span><span class="badge text-bg-primary" data-default-badge hidden>Default</span>`;
        item.addEventListener("click", () => select(code));
        list.append(item);

        option.hidden = true;
        addSelect.value = "";
        initializeEditor(editor);
        refreshDefault();
        select(code);
    }

    function remove(code) {
        if (code === defaultSelect.value) return;
        editorFor(code)?.remove();
        itemFor(code)?.remove();
        const option = addOptionFor(code);
        if (option) option.hidden = false;
        select(list.querySelector("[data-currency-item]")?.dataset.currencyItem);
    }

    editors.querySelectorAll("[data-currency-format]").forEach(initializeEditor);
    list.querySelectorAll("[data-currency-item]").forEach(item => item.addEventListener("click", () => select(item.dataset.currencyItem)));
    addButton.addEventListener("click", () => add(addSelect.value));
    addSelect.addEventListener("keydown", event => { if (event.key === "Enter") { event.preventDefault(); add(addSelect.value); } });
    defaultSelect.addEventListener("change", () => { add(defaultSelect.value); refreshDefault(); });

    refreshDefault();
    select(defaultSelect.value && editorFor(defaultSelect.value) ? defaultSelect.value : list.querySelector("[data-currency-item]")?.dataset.currencyItem);
})();
