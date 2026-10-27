document.querySelectorAll("[data-copy-code]").forEach(button => {
    button.addEventListener("click", async () => {
        const code = button.parentElement?.querySelector("code")?.textContent;
        if (!code) return;

        try {
            await navigator.clipboard.writeText(code);
            button.textContent = "Copied";
            button.classList.add("copied");
            window.setTimeout(() => {
                button.textContent = "Copy";
                button.classList.remove("copied");
            }, 2000);
        } catch {
            button.textContent = "Copy failed";
        }
    });
});
