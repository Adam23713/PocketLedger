(() => {
    async function update(form, data) {
        const response = await fetch(form.action, { method: "POST", body: data, headers: { "X-Requested-With": "XMLHttpRequest" }, credentials: "same-origin" });
        if (!response.ok) {
            const problem = response.headers.get("content-type")?.includes("application/json") ? await response.json() : null;
            throw new Error(problem?.message ?? "Could not save the account settings. Please try again.");
        }
        const updated = new DOMParser().parseFromString(await response.text(), "text/html");
        const rowId = form.closest("tr").id;
        const ids = [rowId, "planner-summary", "planner-chart", "planner-notices"];
        if (response.redirected || ids.some(id => !updated.getElementById(id))) throw new Error("Could not refresh the plan. Please reload the page to check the saved settings.");
        ids.forEach(id => document.getElementById(id)?.replaceWith(updated.getElementById(id)));
        document.dispatchEvent(new Event("money:initialize"));
        initialize();
        return rowId;
    }
    function initialize() {
        document.querySelectorAll(".planner-inclusion").forEach(form => {
            if (form.dataset.initialized) return;
            form.dataset.initialized = "true";
            const toggle = form.querySelector('[role="switch"]');
            const error = form.querySelector('[role="alert"]');
            form.addEventListener("submit", event => event.preventDefault());
            toggle.addEventListener("change", async () => {
                const data = new FormData(form);
                data.set("IncludeInBalance", String(toggle.checked));
                toggle.disabled = true;
                error.hidden = true;
                try { await update(form, data); }
                catch (exception) { toggle.checked = !toggle.checked; error.textContent = exception.message; error.hidden = false; }
                finally { toggle.disabled = false; }
            });
        });
        document.querySelectorAll(".planner-opening").forEach(form => {
            if (form.dataset.initialized) return;
            form.dataset.initialized = "true";
            const amount = form.querySelector('[name="Amount"]');
            const display = form.querySelector("[data-money-value]");
            const edit = form.querySelector(".planner-opening-edit");
            const editor = form.querySelector(".planner-opening-editor");
            const error = form.querySelector(".planner-opening-error");
            const original = { value: amount.value, disabled: amount.disabled };
            let saving = false;
            function refresh() { display.dispatchEvent(new Event("money:refresh")); }
            edit.addEventListener("click", () => {
                amount.value = amount.dataset.custom;
                amount.disabled = display.disabled = false;
                refresh();
                edit.hidden = true;
                edit.setAttribute("aria-expanded", "true");
                editor.hidden = false;
                display.focus();
            });
            function cancel() {
                if (saving) return;
                amount.value = original.value;
                amount.disabled = display.disabled = original.disabled;
                refresh();
                editor.hidden = true;
                error.hidden = true;
                edit.hidden = false;
                edit.setAttribute("aria-expanded", "false");
                edit.focus();
            }
            form.querySelector(".planner-opening-cancel").addEventListener("click", cancel);
            form.addEventListener("keydown", event => { if (event.key === "Escape") { event.preventDefault(); cancel(); } });
            form.addEventListener("submit", async event => {
                event.preventDefault();
                if (saving) return;
                const data = new FormData(form);
                const followCurrent = event.submitter?.hasAttribute("data-follow-current") ?? false;
                data.set("UseCurrentBalance", String(followCurrent));
                if (followCurrent) data.delete("Amount");
                saving = true;
                error.hidden = true;
                form.setAttribute("aria-busy", "true");
                const buttons = form.querySelectorAll("button");
                buttons.forEach(button => button.disabled = true);
                display.disabled = true;
                try {
                    const rowId = await update(form, data);
                    document.getElementById(rowId)?.querySelector(".planner-opening-edit")?.focus({ preventScroll: true });
                } catch (exception) {
                    error.textContent = exception.message;
                    error.hidden = false;
                } finally {
                    saving = false;
                    form.removeAttribute("aria-busy");
                    buttons.forEach(button => button.disabled = false);
                    display.disabled = false;
                }
            });
        });
    }
    initialize();
})();
