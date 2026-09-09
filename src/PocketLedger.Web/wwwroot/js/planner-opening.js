document.querySelectorAll(".planner-opening").forEach(form => {
    const toggle = form.closest("tr").querySelector('[role="switch"]');
    const amount = form.querySelector('[name="Amount"]');
    const display = form.querySelector("[data-money-value]");
    const edit = form.querySelector(".planner-opening-edit");
    const editor = form.querySelector(".planner-opening-editor");
    const original = { checked: toggle.checked, amount: amount.value, custom: amount.dataset.custom };
    function refresh() {
        amount.disabled = toggle.checked;
        display.disabled = toggle.checked;
        display.dispatchEvent(new Event("money:refresh"));
    }
    function showEditor() {
        edit.hidden = true;
        edit.setAttribute("aria-expanded", "true");
        editor.hidden = false;
        if (!display.disabled) display.focus();
    }
    function changeMode() {
        if (toggle.checked) {
            amount.dataset.custom = amount.value;
            amount.value = amount.dataset.current;
        } else amount.value = amount.dataset.custom;
        refresh();
        showEditor();
    }
    toggle.addEventListener("change", changeMode);
    edit.addEventListener("click", () => {
        if (toggle.checked) { toggle.checked = false; changeMode(); }
        else showEditor();
    });
    function cancel() {
        toggle.checked = original.checked;
        amount.value = original.amount;
        amount.dataset.custom = original.custom;
        refresh();
        editor.hidden = true;
        edit.hidden = false;
        edit.setAttribute("aria-expanded", "false");
        edit.focus();
    }
    form.querySelector(".planner-opening-cancel").addEventListener("click", cancel);
    form.addEventListener("keydown", event => { if (event.key === "Escape") { event.preventDefault(); cancel(); } });
});
