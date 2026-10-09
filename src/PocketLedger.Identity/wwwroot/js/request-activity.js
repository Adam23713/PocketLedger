(() => {
    const form = document.getElementById("request-activity-filters");
    if (!form) return;

    const state = document.getElementById("request-activity-state");
    const results = document.getElementById("request-activity-results");
    const summary = document.getElementById("request-activity-summary");
    const tableBody = document.getElementById("request-activity-table-body");
    const canvas = document.getElementById("request-activity-chart");
    const userSelect = form.elements.userId;
    let chart;

    const formatUtc = value => `${new Date(value).toISOString().slice(0, 16).replace("T", " ")} UTC`;

    function showMessage(message, kind = "info") {
        state.replaceChildren();
        const alert = document.createElement("div");
        alert.className = `alert alert-${kind}`;
        alert.textContent = message;
        state.appendChild(alert);
    }

    function renderTable(data) {
        const fragment = document.createDocumentFragment();
        for (const point of data.points) {
            const row = document.createElement("tr");
            const values = [formatUtc(point.startedAtUtc), point.averageRequestsPerMinute.toFixed(2), point.totalRequests, point.authenticatedRequests, point.anonymousRequests];
            for (const value of values) {
                const cell = document.createElement("td");
                cell.textContent = value;
                row.appendChild(cell);
            }
            fragment.appendChild(row);
        }
        tableBody.replaceChildren(fragment);
    }

    function renderChart(data) {
        chart?.destroy();
        chart = new Chart(canvas, {
            type: "line",
            data: {
                labels: data.points.map(point => formatUtc(point.startedAtUtc)),
                datasets: [{ label: `${data.seriesLabel} — average requests/minute`, data: data.points.map(point => point.averageRequestsPerMinute), borderColor: "#0d6efd", backgroundColor: "rgba(13, 110, 253, 0.12)", fill: true, pointRadius: 0, pointHitRadius: 8, tension: 0.15 }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { intersect: false, mode: "index" },
                scales: {
                    x: { ticks: { maxTicksLimit: 12 } },
                    y: { beginAtZero: true, title: { display: true, text: "Average requests/minute" } }
                }
            }
        });
    }

    function renderUsers(users) {
        const selectedUserId = userSelect.value;
        userSelect.length = 1;
        for (const user of users) userSelect.add(new Option(user.username, user.id, false, user.id === selectedUserId));
    }

    async function loadActivity() {
        state.innerHTML = '<span class="text-muted">Loading request activity…</span>';
        results.hidden = true;
        const parameters = new URLSearchParams();
        parameters.set("hours", form.elements.hours.value);
        if (form.elements.userId.value) parameters.set("userId", form.elements.userId.value);

        try {
            const response = await fetch(`${form.dataset.endpoint}?${parameters}`, { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(`Request activity returned ${response.status}.`);
            const data = await response.json();
            renderUsers(data.users);
            if (!data.isAvailable) {
                chart?.destroy();
                showMessage("Request telemetry is currently unavailable. The rest of the admin dashboard remains available.");
                return;
            }

            if (data.isPartial) showMessage("Only part of the requested request-activity interval is available.", "warning");
            else if (data.isEmpty) showMessage("No requests matched the selected activity view during this interval.");
            else state.replaceChildren();

            summary.textContent = `${data.seriesLabel}: ${data.seriesRequests} matching requests; ${data.totalRequests} total requests in the interval; ${data.anonymousRequests} anonymous or unattributed.`;
            renderTable(data);
            renderChart(data);
            results.hidden = false;
        } catch (error) {
            chart?.destroy();
            showMessage("Request activity could not be loaded. The rest of the admin dashboard remains available.", "warning");
            console.warn(error);
        }
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        loadActivity();
    });
    loadActivity();
})();
