document.querySelectorAll("[data-copy-code]").forEach(button => {
    const copyLabel = button.textContent;
    button.addEventListener("click", async () => {
        const code = button.parentElement?.querySelector("code")?.textContent;
        if (!code) return;

        try {
            await navigator.clipboard.writeText(code);
            button.textContent = button.dataset.copied;
            button.classList.add("copied");
            window.setTimeout(() => {
                button.textContent = copyLabel;
                button.classList.remove("copied");
            }, 2000);
        } catch {
            button.textContent = button.dataset.copyFailed;
        }
    });
});


document.querySelectorAll('[data-ui-language]').forEach(select => {
    const currentLanguage = select.value;
    select.addEventListener('change', () => {
        if (select.value !== currentLanguage) {
            select.form.requestSubmit();
        }
    });
});
