(() => {
    const modal = document.getElementById("delete-account-modal");
    if (!modal) return;

    const form = document.getElementById("delete-account-form");
    const confirmationInput = form.elements.confirmation;
    const submitButton = modal.querySelector("[data-delete-account-submit]");

    function reset() {
        confirmationInput.value = "";
        submitButton.disabled = true;
    }

    modal.addEventListener("shown.bs.modal", () => confirmationInput.focus());
    modal.addEventListener("hidden.bs.modal", reset);
    confirmationInput.addEventListener("input", () => submitButton.disabled = confirmationInput.value !== "DELETE");
    form.addEventListener("submit", event => {
        if (confirmationInput.value === "DELETE") return;
        event.preventDefault();
        confirmationInput.focus();
    });
})();
