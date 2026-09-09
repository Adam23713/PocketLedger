(() => {
    const form = document.getElementById("planner-form");
    if (!form) return;
    const field = id => document.getElementById(id);
    const type = field("planner-type"), account = field("planner-account"), target = field("planner-target"), currency = field("planner-currency");
    const amount = field("planner-amount"), accountAmount = field("planner-account-amount"), targetAmount = field("planner-target-amount");
    const accountDisplay = field("planner-account-amount-display"), targetDisplay = field("planner-target-amount-display");
    const accountCurrency = () => account.selectedOptions[0]?.dataset.currency ?? "";
    const targetCurrency = () => target.selectedOptions[0]?.dataset.currency ?? "";
    function update() {
        const transfer = type.value === "Transfer";
        if (accountCurrency() && currency.value !== accountCurrency()) currency.value = accountCurrency();
        field("planner-currency-field").hidden = true;
        field("planner-conversion-preview").hidden = !transfer;
        const conversion = !transfer && accountCurrency() && currency.value !== accountCurrency();
        field("planner-target-field").hidden = !transfer;
        target.disabled = !transfer;
        target.required = transfer;
        for (const option of target.options) {
            const disabled = !!option.value && option.value === account.value;
            if (option.disabled !== disabled) option.disabled = disabled;
        }
        field("PlannedDate").required = transfer;
        field("planner-date-help").hidden = transfer;
        field("planner-conversion-field").hidden = !conversion;
        accountDisplay.disabled = !conversion;
        accountDisplay.required = !!conversion;
        field("planner-target-amount-field").hidden = !transfer;
        targetDisplay.disabled = !transfer;
        targetDisplay.required = transfer;
        targetAmount.disabled = !transfer;
        targetDisplay.readOnly = transfer && accountCurrency() === targetCurrency();
        field("planner-account-currency").textContent = accountCurrency();
        field("planner-target-currency").textContent = targetCurrency();
        field("planner-category-field").hidden = transfer;
        for (const item of form.querySelectorAll("[data-category-type]")) {
            item.hidden = transfer || item.dataset.categoryType !== type.value;
            const radio = item.querySelector("input");
            radio.disabled = item.hidden;
            if (radio.disabled) radio.checked = false;
        }
        sync();
    }
    function sync() {
        const transfer = type.value === "Transfer";
        if (transfer || currency.value === accountCurrency()) accountAmount.value = amount.value;
        if (transfer && accountCurrency() === targetCurrency()) {
            targetAmount.value = amount.value;
            targetDisplay.value = field("planner-amount-display").value;
        }
        if (!transfer) targetAmount.value = "";
        const verb = type.value === "Income" ? "Expected credit" : "Expected debit";
        field("planner-conversion-preview").textContent = transfer
            ? (accountAmount.value || "0") + " " + accountCurrency() + " → " + (targetAmount.value || "0") + " " + targetCurrency() + " · planned transfer"
            : verb + ": " + (accountAmount.value || "0") + " " + accountCurrency() + " · original amount: " + (amount.value || "0") + " " + currency.value;
    }
    for (const input of [type, account, target, currency]) input.addEventListener("change", update);
    for (const input of [amount, accountAmount, targetAmount]) input.addEventListener("change", sync);
    form.addEventListener("submit", sync);
    if (type.value !== "Transfer" && accountCurrency() && currency.value !== accountCurrency()) {
        amount.value = accountAmount.value;
        field("planner-amount-display").dispatchEvent(new Event("money:refresh"));
    }
    update();
})();
