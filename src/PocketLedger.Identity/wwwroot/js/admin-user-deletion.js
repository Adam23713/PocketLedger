(() => {
    const modal = document.getElementById("delete-user-modal");
    if (!modal) return;

    const form = document.getElementById("delete-user-form");
    const userIdInput = form.elements.userId;
    const confirmationInput = form.elements.confirmation;
    const username = modal.querySelector("[data-delete-username]");
    const submitButton = modal.querySelector("[data-delete-submit]");

    function reset() {
        userIdInput.value = "";
        confirmationInput.value = "";
        submitButton.disabled = true;
    }

    modal.addEventListener("show.bs.modal", event => {
        const trigger = event.relatedTarget;
        reset();
        userIdInput.value = trigger.dataset.deleteUserId;
        username.textContent = trigger.dataset.deleteUsername;
    });
    modal.addEventListener("shown.bs.modal", () => confirmationInput.focus());
    modal.addEventListener("hidden.bs.modal", reset);
    confirmationInput.addEventListener("input", () => submitButton.disabled = confirmationInput.value !== "DELETE");
    form.addEventListener("submit", event => {
        if (confirmationInput.value === "DELETE") return;
        event.preventDefault();
        confirmationInput.focus();
    });
})();
