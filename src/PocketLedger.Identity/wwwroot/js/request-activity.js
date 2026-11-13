(() => {
    const form = document.getElementById("request-activity-filters");
    if (!form) return;

    const translations = JSON.parse(document.getElementById("request-activity-translations").textContent);
    const t = (key, ...args) => (translations[key] ?? key).replace(/\{(\d+)\}/g, (token, index) => args[index] ?? token);

    const byId = id => document.getElementById(id);
    const state = byId("request-activity-state");
    const results = byId("request-activity-results");
    const summary = byId("request-activity-summary");
    const stats = byId("request-activity-stats");
    const timelineBody = byId("request-activity-table-body");
    const canvas = byId("request-activity-chart");
    const clientsPanel = byId("request-clients");
    const clientsBody = byId("request-clients-body");
    const clientsFilter = byId("request-clients-filter");
    const clientsNote = byId("request-clients-note");
    const details = byId("request-client-details");
    const banForm = byId("request-client-ban-form");
    const unbanForm = byId("request-client-unban-form");
    const userSelect = form.elements.userId;
    const ipInput = form.elements.ip;
    const number = new Intl.NumberFormat();
    const kindLabels = { Internal: t("Internal network"), CloudflareEdge: t("Cloudflare edge") };
    let chart;
    let clients = [];
    let detailsRequest = 0;

    const formatUtc = value => `${new Date(value).toISOString().slice(0, 16).replace("T", " ")} UTC`;
    const percent = (part, whole) => whole > 0 ? `${Math.round(part / whole * 100)}%` : "0%";

    function element(tag, properties = {}, ...children) {
        const node = Object.assign(document.createElement(tag), properties);
        node.append(...children);
        return node;
    }

    function alertBox(message, kind = "info") {
        return element("div", { className: `alert alert-${kind}`, textContent: message });
    }

    function showMessage(message, kind = "info") {
        state.replaceChildren(alertBox(message, kind));
    }

    function row(cells) {
        return element("tr", {}, ...cells.map(cell => element("td", {}, cell)));
    }

    function badge(text, kind) {
        return element("span", { className: `badge text-bg-${kind} ms-1`, textContent: text });
    }

    function inspectButton(address, text = t("Inspect")) {
        const button = element("button", { type: "button", className: "btn btn-link btn-sm p-0 align-baseline", textContent: text });
        button.dataset.inspectAddress = address;
        return button;
    }

    function renderStats(data) {
        const selected = data.clientIpAddress ? clients.find(client => client.address === data.clientIpAddress) : null;
        const tiles = [[t("Requests"), number.format(data.seriesRequests)]];
        if (data.userId) tiles.push([t("All requests in the interval"), number.format(data.totalRequests)]);
        else if (data.clientIpAddress) {
            tiles.push([t("Failed (4xx/5xx)"), `${number.format(data.seriesFailedRequests)} (${percent(data.seriesFailedRequests, data.seriesRequests)})`]);
            tiles.push([t("First seen"), selected ? formatUtc(selected.firstSeenUtc) : "—"], [t("Last seen"), selected ? formatUtc(selected.lastSeenUtc) : "—"]);
        } else {
            tiles.push([t("Failed (4xx/5xx)"), `${number.format(data.seriesFailedRequests)} (${percent(data.seriesFailedRequests, data.seriesRequests)})`]);
            tiles.push([t("Client addresses"), number.format(data.totalClients)], [t("Anonymous"), number.format(data.anonymousRequests)]);
        }
        const columnClass = tiles.length === 2 ? "col-6" : "col-6 col-lg-3";
        stats.replaceChildren(...tiles.map(([label, value]) => element("div", { className: columnClass },
            element("div", { className: "border rounded-3 p-3 h-100" },
                element("dt", { className: "text-muted fw-normal small", textContent: label }),
                element("dd", { className: "fs-5 fw-semibold mb-0", textContent: value })))));
    }

    function renderTimeline(data) {
        const active = data.points.filter(point => point.totalRequests > 0 && point.seriesRequests > 0).reverse();
        timelineBody.replaceChildren(...active.map(point => row([
            formatUtc(point.startedAtUtc), point.averageRequestsPerMinute.toFixed(2), number.format(point.seriesRequests), number.format(point.failedRequests),
            number.format(point.authenticatedRequests), number.format(point.anonymousRequests), number.format(point.distinctClients),
            point.topClientIpAddress ? element("span", {}, inspectButton(point.topClientIpAddress, point.topClientIpAddress), ` (${number.format(point.topClientRequests)})`) : "—"
        ])));
        if (active.length === 0) timelineBody.replaceChildren(element("tr", {}, element("td", { colSpan: 8, className: "text-muted", textContent: t("No traffic in this interval.") })));
    }

    function renderClients() {
        const search = clientsFilter.value.trim().toLowerCase();
        const visible = clients.filter(client => client.address.includes(search));
        clientsBody.replaceChildren(...visible.map(client => {
            const address = element("span", {}, element("code", { textContent: client.address }));
            if (client.isCurrentAdministrator) address.append(badge(t("You"), "info"));
            if (kindLabels[client.kind]) address.append(badge(kindLabels[client.kind], "secondary"));
            if (client.isBanned) address.append(badge(t("Banned"), "danger"));
            return row([address, number.format(client.requests), `${number.format(client.failedRequests)} (${percent(client.failedRequests, client.requests)})`,
                formatUtc(client.firstSeenUtc), formatUtc(client.lastSeenUtc), inspectButton(client.address)]);
        }));
        if (visible.length === 0) clientsBody.replaceChildren(element("tr", {}, element("td", { colSpan: 6, className: "text-muted", textContent: clients.length ? t("No address matches.") : t("No client addresses were recorded in this interval.") })));
    }

    function renderChart(data) {
        chart?.destroy();
        const datasets = [{ label: t("{0}: requests/minute", data.seriesLabel), data: data.points.map(point => point.averageRequestsPerMinute), borderColor: "#0d6efd", backgroundColor: "rgba(13, 110, 253, 0.12)", borderWidth: 2, fill: true, pointRadius: 0, pointHitRadius: 8, tension: 0.15 }];
        // Failures are stored per address, so they cannot be split by account.
        if (!data.userId) datasets.push({ label: t("Failed (4xx/5xx): requests/minute"), data: data.points.map(point => point.failedRequests / 5), borderColor: "#c2410c", backgroundColor: "#c2410c", borderWidth: 2, borderDash: [6, 3], fill: false, pointRadius: 0, pointHitRadius: 8, tension: 0.15 });
        chart = new Chart(canvas, {
            type: "line",
            data: { labels: data.points.map(point => formatUtc(point.startedAtUtc)), datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { intersect: false, mode: "index" },
                plugins: { legend: { labels: { boxHeight: 0 } } },
                scales: {
                    x: { ticks: { maxTicksLimit: 12 } },
                    y: { beginAtZero: true, title: { display: true, text: t("Requests/minute") } }
                }
            }
        });
    }

    function renderUsers(users) {
        const selectedUserId = userSelect.value;
        userSelect.length = 1;
        for (const user of users) userSelect.add(new Option(user.username, user.id, false, user.id === selectedUserId));
    }

    function renderBanArea(data) {
        const banStatus = byId("request-client-ban-status");
        const banList = byId("request-client-ban-list");
        banList.replaceChildren(...data.bans.map(ban => element("li", { textContent: `${ban.address}: ${ban.scenario} (${ban.origin})${ban.remainingText ? ", " + t("{0} left", ban.remainingText) : ""}` })));
        const hasExactBan = data.bans.some(ban => ban.address === data.address);
        if (!data.areBansAvailable) banStatus.textContent = t("CrowdSec cannot be reached, so the ban status of this address is unknown.");
        else if (data.bans.length > 0) banStatus.textContent = t("This address is banned on every PocketLedger site.");
        else if (data.banRefusal) banStatus.textContent = data.banRefusal;
        else if (data.users.length > 0) banStatus.textContent = t("Not banned. Signed-in accounts used this address, so a ban also locks out those people while they are on it.");
        else banStatus.textContent = t("Not banned. A ban blocks this address on every PocketLedger site.");

        if (banForm) {
            banForm.elements.ip.value = data.address;
            banForm.hidden = !data.areBansAvailable || data.bans.length > 0 || Boolean(data.banRefusal);
        }
        if (unbanForm) {
            unbanForm.elements.address.value = data.address;
            unbanForm.hidden = !hasExactBan;
        }
    }

    async function loadClientDetails(address, focus) {
        const request = ++detailsRequest;
        const clientState = byId("request-client-state");
        const endpoints = byId("request-client-endpoints");
        details.hidden = false;
        byId("request-client-address").textContent = address;
        byId("request-client-badges").replaceChildren();
        byId("request-client-ban-area").hidden = true;
        byId("request-client-users").textContent = "";
        byId("request-client-note").textContent = "";
        endpoints.replaceChildren();
        clientState.replaceChildren(element("span", { className: "text-muted", textContent: t("Loading address details…") }));
        if (focus) details.focus({ preventScroll: true });
        if (focus) details.scrollIntoView({ block: "start", behavior: "smooth" });

        try {
            const parameters = new URLSearchParams({ ip: address, hours: form.elements.hours.value });
            const response = await fetch(`${form.dataset.clientEndpoint}?${parameters}`, { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(`Request client returned ${response.status}.`);
            const data = await response.json();
            if (request !== detailsRequest) return;

            clientState.replaceChildren();
            if (!data.isAvailable) clientState.replaceChildren(alertBox(t("The endpoints of this address could not be loaded."), "warning"));
            const badges = byId("request-client-badges");
            if (data.isCurrentAdministrator) badges.append(badge(t("You"), "info"));
            if (kindLabels[data.kind]) badges.append(badge(kindLabels[data.kind], "secondary"));
            if (data.bans.length > 0) badges.append(badge(t("Banned"), "danger"));
            byId("request-client-ban-area").hidden = false;
            renderBanArea(data);

            byId("request-client-users").textContent = data.users.length > 0
                ? t("Signed-in accounts seen from this address: {0}.", data.users.map(user => user.username).join(", "))
                : t("No signed-in account was seen from this address.");
            endpoints.replaceChildren(...data.endpoints.map(endpoint => row([endpoint.method, element("code", { textContent: endpoint.path, className: "text-break" }), endpoint.application,
                String(endpoint.statusCode), number.format(endpoint.requests), formatUtc(endpoint.lastSeenHourUtc)])));
            if (data.endpoints.length === 0) endpoints.replaceChildren(element("tr", {}, element("td", { colSpan: 6, className: "text-muted", textContent: t("No endpoints were recorded for this address in this interval.") })));

            const notes = [];
            if (data.omittedRequests > 0) notes.push(t("{0} requests are not itemized because this address reached the limit of 200 different endpoints in an hour.", number.format(data.omittedRequests)));
            if (data.isPossiblyIncomplete) notes.push(t("The list may be incomplete because the hourly storage limit was reached during this interval."));
            byId("request-client-note").textContent = notes.join(" ");
        } catch (error) {
            if (request !== detailsRequest) return;
            clientState.replaceChildren(alertBox(t("The details of this address could not be loaded."), "warning"));
            console.warn(error);
        }
    }

    async function loadActivity({ focusDetails = false } = {}) {
        state.replaceChildren(element("span", { className: "text-muted", textContent: t("Loading request activity…") }));
        results.hidden = true;
        const parameters = new URLSearchParams({ hours: form.elements.hours.value });
        if (userSelect.value) parameters.set("userId", userSelect.value);
        const address = ipInput.value.trim();
        if (address) parameters.set("ip", address);

        try {
            const response = await fetch(`${form.dataset.endpoint}?${parameters}`, { headers: { Accept: "application/json" } });
            if (response.status === 400) {
                const problem = await response.json();
                chart?.destroy();
                showMessage(problem.error ?? t("The filter is not valid."), "warning");
                return;
            }
            if (!response.ok) throw new Error(`Request activity returned ${response.status}.`);
            const data = await response.json();
            renderUsers(data.users);
            if (!data.isAvailable) {
                chart?.destroy();
                showMessage(t("Request telemetry is currently unavailable. The rest of the admin dashboard remains available."));
                return;
            }

            if (data.isPartial) showMessage(t("Only part of the requested request-activity interval is available."), "warning");
            else if (!data.userId && !data.isClientActivityAvailable) showMessage(t("Client address data is currently unavailable; totals below are still accurate."), "warning");
            else if (data.isEmpty) showMessage(data.clientIpAddress ? t("No requests from {0} during this interval.", data.clientIpAddress) : t("No requests matched the selected activity view during this interval."));
            else state.replaceChildren();

            clients = data.clients ?? [];
            summary.textContent = data.userId
                ? t("{0}: {1} of {2} requests in the interval.", data.seriesLabel, number.format(data.seriesRequests), number.format(data.totalRequests))
                : data.clientIpAddress
                    ? t("{0}: {1} of {2} requests in the interval.", data.seriesLabel, number.format(data.seriesRequests), number.format(data.totalRequests))
                    : t("{0} requests from {1} addresses; {2} were not signed in.", number.format(data.totalRequests), number.format(data.totalClients), number.format(data.anonymousRequests));
            renderStats(data);
            renderTimeline(data);
            clientsPanel.hidden = Boolean(data.userId);
            if (!data.userId) renderClients();
            const notes = [];
            if (data.totalClients > clients.length) notes.push(t("Showing the {0} busiest of {1} addresses.", number.format(clients.length), number.format(data.totalClients)));
            if (data.omittedClientRequests > 0) notes.push(t("{0} requests are in the totals but not listed per address because too many addresses were active in an interval.", number.format(data.omittedClientRequests)));
            clientsNote.textContent = notes.join(" ");
            results.hidden = false;
            renderChart(data);

            if (data.clientIpAddress) {
                ipInput.value = data.clientIpAddress;
                loadClientDetails(data.clientIpAddress, focusDetails);
            } else {
                detailsRequest++;
                details.hidden = true;
            }
        } catch (error) {
            chart?.destroy();
            showMessage(t("Request activity could not be loaded. The rest of the admin dashboard remains available."), "warning");
            console.warn(error);
        }
    }

    function inspect(address) {
        ipInput.value = address;
        userSelect.value = "";
        loadActivity({ focusDetails: true });
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        loadActivity({ focusDetails: Boolean(ipInput.value.trim()) });
    });
    // The two filters use different stored data and cannot be combined, so choosing one replaces the other.
    userSelect.addEventListener("change", () => { if (userSelect.value) ipInput.value = ""; });
    ipInput.addEventListener("input", () => { if (ipInput.value.trim()) userSelect.value = ""; });
    byId("request-activity-clear").addEventListener("click", () => {
        ipInput.value = "";
        userSelect.value = "";
        clientsFilter.value = "";
        loadActivity();
    });
    clientsFilter.addEventListener("input", renderClients);
    results.addEventListener("click", event => {
        const button = event.target.closest("[data-inspect-address]");
        if (button) inspect(button.dataset.inspectAddress);
    });
    banForm?.addEventListener("submit", event => {
        const duration = banForm.elements.durationHours.selectedOptions[0].textContent;
        if (!window.confirm(t("Ban {0} for {1}? Every PocketLedger site will refuse this address.", banForm.elements.ip.value, duration))) event.preventDefault();
    });
    loadActivity();
})();
