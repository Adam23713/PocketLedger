(() => {
    const trendDataElement = document.getElementById("monthly-trend-data");

    if (!trendDataElement || typeof Chart === "undefined") {
        return;
    }

    const trend = JSON.parse(trendDataElement.textContent);
    const colors = ["#2563eb", "#dc2626", "#16a34a", "#d97706", "#7c3aed", "#0891b2", "#db2777", "#4d7c0f", "#9333ea", "#ea580c"];
    const moneyFormatter = new Intl.NumberFormat("hu-HU", { maximumFractionDigits: 0 });
    const percentFormatter = new Intl.NumberFormat("hu-HU", { maximumFractionDigits: 1 });
    const charts = [];

    const formatMoney = value => moneyFormatter.format(value);
    const chartTextColor = () => getComputedStyle(document.documentElement).getPropertyValue("--text-primary").trim() || "#212529";
    const chartGridColor = () => getComputedStyle(document.documentElement).getPropertyValue("--surface-border").trim() || "rgba(0, 0, 0, .1)";

    const categoryCenterText = {
        id: "categoryCenterText",
        afterDraw(chart, _, options) {
            if (!options?.amount) {
                return;
            }

            const { ctx, chartArea } = chart;
            const centerX = (chartArea.left + chartArea.right) / 2;
            const centerY = (chartArea.top + chartArea.bottom) / 2;
            const maxWidth = Math.min(chartArea.right - chartArea.left, chartArea.bottom - chartArea.top) * .48;
            const amount = formatMoney(options.amount);
            let fontSize = 18;

            ctx.save();
            ctx.fillStyle = chartTextColor();
            ctx.textAlign = "center";
            ctx.textBaseline = "middle";
            ctx.font = "600 11px system-ui, sans-serif";
            ctx.fillText("TOTAL", centerX, centerY - 12);

            do {
                ctx.font = `700 ${fontSize}px system-ui, sans-serif`;
                fontSize--;
            } while (fontSize > 10 && ctx.measureText(amount).width > maxWidth);

            ctx.fillText(amount, centerX, centerY + 9);
            ctx.restore();
        }
    };

    const setupCategoryChart = kind => {
        const categoryDataElement = document.getElementById(`${kind}-main-category-data`);
        const categories = categoryDataElement ? JSON.parse(categoryDataElement.textContent) : [];
        if (categories.length === 0 || !document.getElementById(`${kind}-category-chart`)) {
            return;
        }

        const selector = document.getElementById(`${kind}-category-selector`);
        const breakdownHeading = document.getElementById(`${kind}-breakdown-heading`);
        const breakdownTotal = document.getElementById(`${kind}-breakdown-total`);
        const breakdownBody = document.getElementById(`${kind}-breakdown-body`);
        let selectedCategoryIndex = null;

        const selectCategory = index => {
            const category = categories[index];
            selectedCategoryIndex = index;
            breakdownHeading.textContent = category.name;
            breakdownTotal.textContent = `${formatMoney(category.amount)} total`;
            breakdownBody.replaceChildren(...category.subcategories.map(subcategory => {
                const row = document.createElement("tr");
                const name = document.createElement("td");
                const amount = document.createElement("td");
                const share = document.createElement("td");
                name.textContent = subcategory.name;
                amount.className = "text-end";
                amount.textContent = formatMoney(subcategory.amount);
                share.className = "text-end";
                share.textContent = `${percentFormatter.format(category.amount === 0 ? 0 : subcategory.amount / category.amount * 100)}%`;
                row.append(name, amount, share);
                return row;
            }));

            selector.querySelectorAll("button").forEach((button, buttonIndex) => button.setAttribute("aria-pressed", String(buttonIndex === index)));
            categoryChart.options.plugins.categoryCenterText.amount = category.amount;
            categoryChart.update();
        };

        const resetSelection = () => {
            const row = document.createElement("tr");
            const message = document.createElement("td");
            selectedCategoryIndex = null;
            breakdownHeading.textContent = "All main categories";
            breakdownTotal.textContent = `${formatMoney(total)} total`;
            message.className = "text-muted";
            message.colSpan = 3;
            message.textContent = "Select a category to see the breakdown.";
            row.append(message);
            breakdownBody.replaceChildren(row);
            selector.querySelectorAll("button").forEach(button => button.setAttribute("aria-pressed", "false"));
            categoryChart.options.plugins.categoryCenterText.amount = total;
            categoryChart.update();
        };

        const total = categories.reduce((total, category) => total + category.amount, 0);

        categories.forEach((category, index) => {
            const button = document.createElement("button");
            const swatch = document.createElement("span");
            swatch.className = "statistics-category-swatch";
            swatch.style.backgroundColor = colors[index % colors.length];
            button.type = "button";
            button.setAttribute("aria-pressed", "false");
            button.append(swatch, document.createTextNode(`${category.name} ${percentFormatter.format(total === 0 ? 0 : category.amount / total * 100)}%`));
            button.addEventListener("click", () => selectCategory(index));
            selector.append(button);
        });

        const categoryChart = new Chart(document.getElementById(`${kind}-category-chart`), {
            type: "doughnut",
            plugins: [categoryCenterText],
            data: {
                labels: categories.map(category => category.name),
                datasets: [{
                    data: categories.map(category => category.amount),
                    backgroundColor: context => {
                        const color = colors[context.dataIndex % colors.length];
                        return selectedCategoryIndex === null || selectedCategoryIndex === context.dataIndex ? color : `${color}55`;
                    },
                    borderWidth: context => selectedCategoryIndex === context.dataIndex ? 4 : 2,
                    hoverOffset: 16,
                    offset: context => selectedCategoryIndex === context.dataIndex ? 24 : 0
                }]
            },
            options: {
                maintainAspectRatio: false,
                cutout: "58%",
                layout: { padding: 28 },
                onClick: (_, elements) => elements.length > 0 ? selectCategory(elements[0].index) : resetSelection(),
                plugins: {
                    legend: { display: false },
                    categoryCenterText: { amount: categories[0].amount },
                    tooltip: { enabled: false }
                }
            }
        });
        charts.push(categoryChart);
        resetSelection();

        document.addEventListener("click", event => {
            if (selectedCategoryIndex !== null && !categoryChart.canvas.contains(event.target) && !selector.contains(event.target)) {
                resetSelection();
            }
        });
    };

    setupCategoryChart("expense");
    setupCategoryChart("income");

    const trendChart = new Chart(document.getElementById("monthly-trend-chart"), {
        type: "bar",
        data: {
            labels: trend.map(item => `${item.year}-${String(item.month).padStart(2, "0")}`),
            datasets: [
                { label: "Income", data: trend.map(item => item.income), backgroundColor: "#16a34a" },
                { label: "Expenses", data: trend.map(item => item.expenses), backgroundColor: "#dc2626" }
            ]
        },
        options: {
            maintainAspectRatio: false,
            scales: {
                x: { grid: { display: false }, ticks: { color: chartTextColor() } },
                y: { beginAtZero: true, grid: { color: chartGridColor() }, ticks: { color: chartTextColor(), callback: value => moneyFormatter.format(value) } }
            },
            plugins: {
                legend: { labels: { color: chartTextColor() } },
                tooltip: { callbacks: { label: context => `${context.dataset.label}: ${formatMoney(context.raw)}` } }
            }
        }
    });
    charts.push(trendChart);

    new MutationObserver(() => {
        charts.forEach(chart => {
            if (chart.options.scales?.x) chart.options.scales.x.ticks.color = chartTextColor();
            if (chart.options.scales?.y) {
                chart.options.scales.y.ticks.color = chartTextColor();
                chart.options.scales.y.grid.color = chartGridColor();
            }
            if (chart.options.plugins.legend?.labels) chart.options.plugins.legend.labels.color = chartTextColor();
            chart.update();
        });
    }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme", "data-bs-theme"] });
})();
