document.querySelectorAll("[data-money-value]").forEach(display => {
    const canonical = document.getElementById(display.dataset.moneyValue);
    const account = document.getElementById(display.dataset.moneyAccount);

    function options() {
        const selected = account?.selectedOptions[0];
        return {
            decimalPlaces: Number(selected?.dataset.decimalPlaces ?? display.dataset.decimalPlaces ?? 2),
            decimalSeparator: selected?.dataset.decimalSeparator ?? display.dataset.decimalSeparator ?? ".",
            thousandsSeparator: selected?.dataset.thousandsSeparator ?? display.dataset.thousandsSeparator ?? ","
        };
    }

    function parse(value, format) {
        if (!/\d/.test(value)) return { integer: "", fraction: "", hasDecimal: false };
        let normalized = value;
        if (format.thousandsSeparator) normalized = normalized.split(format.thousandsSeparator).join("");
        normalized = normalized.replace(/\s/g, "");
        const configuredIndex = normalized.lastIndexOf(format.decimalSeparator);
        const fallbackIndex = Math.max(normalized.lastIndexOf("."), normalized.lastIndexOf(","));
        const decimalIndex = configuredIndex >= 0 ? configuredIndex : fallbackIndex;
        const integer = (decimalIndex >= 0 ? normalized.slice(0, decimalIndex) : normalized).replace(/\D/g, "").replace(/^0+(?=\d)/, "") || "0";
        const fraction = decimalIndex >= 0 ? normalized.slice(decimalIndex + 1).replace(/\D/g, "").slice(0, format.decimalPlaces) : "";
        return { integer, fraction, hasDecimal: decimalIndex >= 0 && format.decimalPlaces > 0 };
    }

    function render(parts, format) {
        const integer = parts.integer.replace(/\B(?=(\d{3})+(?!\d))/g, format.thousandsSeparator);
        return integer + (parts.hasDecimal ? format.decimalSeparator + parts.fraction : "");
    }

    function formatInput() {
        const format = options();
        const caret = display.selectionStart ?? display.value.length;
        const digitsBeforeCaret = display.value.slice(0, caret).replace(/\D/g, "").length;
        const parts = parse(display.value, format);
        display.value = render(parts, format);
        canonical.value = parts.integer + (parts.fraction ? "." + parts.fraction : "");
        let nextCaret = 0, digitsSeen = 0;
        while (nextCaret < display.value.length && digitsSeen < digitsBeforeCaret) { if (/\d/.test(display.value[nextCaret])) digitsSeen++; nextCaret++; }
        display.setSelectionRange(nextCaret, nextCaret);
        canonical.dispatchEvent(new Event("change"));
    }

    function renderCanonical() {
        const value = canonical.value?.replace(",", ".");
        if (!value) { display.value = ""; return; }
        const [integer, fraction = ""] = value.split(".");
        const format = options();
        display.value = render({ integer: integer.replace(/\D/g, "") || "0", fraction: fraction.slice(0, format.decimalPlaces), hasDecimal: fraction.length > 0 && format.decimalPlaces > 0 }, format);
    }

    display.addEventListener("input", formatInput);
    display.addEventListener("focus", () => display.select());
    account?.addEventListener("change", renderCanonical);
    display.closest("form")?.addEventListener("submit", formatInput, { capture: true });
    renderCanonical();
});
