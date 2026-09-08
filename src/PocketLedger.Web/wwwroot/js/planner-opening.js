document.querySelectorAll(".planner-opening").forEach(form => {
    const toggle = form.querySelector('[role="switch"]');
    const amount = form.querySelector('[name="Amount"]');
    toggle.addEventListener("change", () => {
        if (toggle.checked) {
            amount.dataset.custom = amount.value;
            amount.value = amount.dataset.current;
        }
        else amount.value = amount.dataset.custom;
        amount.disabled = toggle.checked;
    });
});
