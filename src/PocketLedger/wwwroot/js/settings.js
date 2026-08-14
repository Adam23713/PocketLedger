document.querySelectorAll("[data-currency-format]").forEach(card => {
    const fields = card.querySelectorAll("[data-format-field]");
    const currency = card.querySelector('input[type="hidden"]').value;
    function update() {
        const decimalPlaces = Number(fields[0].value), decimalSeparator = fields[1].value, thousandsSeparator = fields[2].value;
        const digits = "1234567".replace(/\B(?=(\d{3})+(?!\d))/g, thousandsSeparator) + (decimalPlaces ? decimalSeparator + "89".padEnd(decimalPlaces, "0").slice(0, decimalPlaces) : "");
        const marker = fields[3].selectedOptions[0].text === "Symbol" ? card.dataset.symbol : currency;
        const space = fields[5].checked ? " " : "";
        card.querySelector("[data-format-preview]").textContent = fields[4].selectedOptions[0].text === "Before" ? marker + space + digits : digits + space + marker;
    }
    fields.forEach(field => field.addEventListener("input", update)); update();
});
