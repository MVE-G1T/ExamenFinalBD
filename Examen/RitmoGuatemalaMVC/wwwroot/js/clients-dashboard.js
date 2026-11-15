(() => {
    "use strict";

    const svgNs = "http://www.w3.org/2000/svg";
    const numberFormat = new Intl.NumberFormat("es-GT", { maximumFractionDigits: 0 });
    const moneyFormat = new Intl.NumberFormat("es-GT", { maximumFractionDigits: 0 });
    const colors = { yellow: "#FFC93C", green: "#1F9D55", pale: "#EAF0FF", muted: "#9FB3D9", grid: "#263B70" };

    function element(name, attributes = {}, text = null) {
        const node = document.createElementNS(svgNs, name);
        for (const [key, value] of Object.entries(attributes)) {
            node.setAttribute(key, String(value));
        }
        if (text !== null) node.textContent = text;
        return node;
    }

    function readSeries(host) {
        try {
            const value = JSON.parse(host.dataset.series || "[]");
            return Array.isArray(value) ? value : [];
        } catch {
            return [];
        }
    }

    function setupSvg(host, width, height, label) {
        const svg = element("svg", { viewBox: `0 0 ${width} ${height}`, role: "img", "aria-label": label, preserveAspectRatio: "xMidYMid meet" });
        host.replaceChildren(svg);
        return svg;
    }

    function addEmpty(svg, width, height, message) {
        svg.append(element("text", { x: width / 2, y: height / 2, "text-anchor": "middle", class: "svg-empty" }, message));
    }

    function addTitle(node, text) {
        node.append(element("title", {}, text));
    }

    function drawMonthly(host, points) {
        const width = 800;
        const height = 300;
        const margin = { top: 18, right: 16, bottom: 36, left: 55 };
        const plotWidth = width - margin.left - margin.right;
        const plotHeight = height - margin.top - margin.bottom;
        const svg = setupSvg(host, width, height, "Ingresos mensuales apilados: suscripciones y publicidad");
        const valid = points.filter(point => point.SubscriptionRevenue !== null && point.SubscriptionRevenue !== undefined);
        if (!valid.length) {
            addEmpty(svg, width, height, "Sin datos para los filtros seleccionados");
            return;
        }

        const maxValue = Math.max(...valid.map(point => Number(point.SubscriptionRevenue) + Number(point.AdvertisingRevenue)), 1);
        const step = Math.pow(10, Math.floor(Math.log10(maxValue)));
        const axisMax = Math.ceil(maxValue / step) * step;
        for (let index = 0; index <= 4; index += 1) {
            const value = axisMax * index / 4;
            const y = margin.top + plotHeight - plotHeight * index / 4;
            svg.append(element("line", { x1: margin.left, x2: width - margin.right, y1: y, y2: y, class: "svg-grid" }));
            svg.append(element("text", { x: margin.left - 8, y: y + 4, "text-anchor": "end", class: "svg-axis-label" }, `${numberFormat.format(value / 1000)}k`));
        }

        const slot = plotWidth / points.length;
        const barWidth = Math.min(38, slot * 0.62);
        points.forEach((point, index) => {
            const x = margin.left + index * slot + (slot - barWidth) / 2;
            const hasValue = point.SubscriptionRevenue !== null && point.SubscriptionRevenue !== undefined;
            if (hasValue) {
                const subscriptions = Number(point.SubscriptionRevenue);
                const advertising = Number(point.AdvertisingRevenue);
                const subscriptionHeight = subscriptions / axisMax * plotHeight;
                const advertisingHeight = advertising / axisMax * plotHeight;
                const baseY = margin.top + plotHeight;
                const lower = element("rect", { x, y: baseY - subscriptionHeight, width: barWidth, height: subscriptionHeight, rx: 4, fill: colors.yellow, class: "svg-bar" });
                addTitle(lower, `${point.Month}: Q ${moneyFormat.format(subscriptions)} en suscripciones`);
                lower.setAttribute("tabindex", "0");
                svg.append(lower);
                const upper = element("rect", { x, y: baseY - subscriptionHeight - advertisingHeight, width: barWidth, height: advertisingHeight, rx: 4, fill: colors.green, class: "svg-bar" });
                addTitle(upper, `${point.Month}: Q ${moneyFormat.format(advertising)} en publicidad`);
                upper.setAttribute("tabindex", "0");
                svg.append(upper);
            } else {
                svg.append(element("text", { x: x + barWidth / 2, y: margin.top + plotHeight - 8, "text-anchor": "middle", class: "svg-no-data" }, "—"));
            }
            svg.append(element("text", { x: x + barWidth / 2, y: height - 10, "text-anchor": "middle", class: "svg-axis-label" }, point.Month));
        });
    }

    function drawDonut(host, points) {
        const width = 360;
        const height = 212;
        const svg = setupSvg(host, width, height, host.getAttribute("aria-label") || "Distribución");
        const valueOf = point => Number(point.Amount ?? point.Users ?? 0);
        const total = points.reduce((sum, point) => sum + valueOf(point), 0);
        if (!total) {
            addEmpty(svg, width, height, "Sin datos para los filtros seleccionados");
            return;
        }

        const centerX = 180;
        const centerY = 102;
        const radius = 67;
        const circumference = 2 * Math.PI * radius;
        svg.append(element("circle", { cx: centerX, cy: centerY, r: radius, fill: "none", stroke: "#1A2A55", "stroke-width": 28 }));
        let offset = 0;
        points.forEach(point => {
            const value = valueOf(point);
            const segment = value / total * circumference;
            const color = point.Color || colors.green;
            const slice = element("circle", {
                cx: centerX, cy: centerY, r: radius, fill: "none", stroke: color, "stroke-width": 28,
                "stroke-dasharray": `${segment} ${circumference - segment}`,
                "stroke-dashoffset": -offset,
                transform: `rotate(-90 ${centerX} ${centerY})`,
                class: "donut-segment",
                tabindex: "0"
            });
            addTitle(slice, `${point.Label}: ${numberFormat.format(value)}`);
            svg.append(slice);
            offset += segment;
        });

        const centerLabel = host.dataset.center || numberFormat.format(total);
        const mainLabel = centerLabel.length > 15 ? centerLabel.slice(0, 14) + "…" : centerLabel;
        svg.append(element("text", { x: centerX, y: centerY + 1, "text-anchor": "middle", class: "donut-center-value" }, mainLabel));
        svg.append(element("text", { x: centerX, y: centerY + 23, "text-anchor": "middle", class: "donut-center-caption" }, centerLabel.includes("Q ") ? "ingresos" : "usuarios"));
    }

    function drawHorizontal(host, points, metric, formatter, labelFor, label) {
        const width = 760;
        const rowHeight = 42;
        const height = Math.max(150, points.length * rowHeight + 24);
        const labelWidth = metric === "Amount" ? 190 : 220;
        const barStart = labelWidth + 12;
        const valueWidth = 76;
        const barArea = width - barStart - valueWidth;
        const svg = setupSvg(host, width, height, label);
        if (!points.length) {
            addEmpty(svg, width, height, "Sin datos para los filtros seleccionados");
            return;
        }

        const maxValue = Math.max(...points.map(point => Number(point[metric]) || 0), 1);
        points.forEach((point, index) => {
            const y = 12 + index * rowHeight;
            const value = Number(point[metric]) || 0;
            const barWidth = Math.max(2, value / maxValue * barArea);
            const fullLabel = labelFor(point);
            const visibleLabel = fullLabel.length > 26 ? `${fullLabel.slice(0, 24)}…` : fullLabel;
            svg.append(element("text", { x: 0, y: y + 17, class: "svg-row-label" }, visibleLabel));
            const bar = element("rect", { x: barStart, y, width: barArea, height: 20, rx: 7, fill: "#182B58", class: "svg-bar-track" });
            svg.append(bar);
            const fill = element("rect", { x: barStart, y, width: barWidth, height: 20, rx: 7, fill: point.Color || colors.green, class: "svg-bar" });
            fill.setAttribute("tabindex", "0");
            addTitle(fill, `${fullLabel}: ${formatter(value)}`);
            svg.append(fill);
            svg.append(element("text", { x: barStart + barArea + 9, y: y + 15, class: "svg-row-value" }, formatter(value)));
        });
    }

    document.querySelectorAll('[data-chart="monthly"]').forEach(host => drawMonthly(host, readSeries(host)));
    document.querySelectorAll('[data-chart="donut"]').forEach(host => drawDonut(host, readSeries(host)));
    document.querySelectorAll('[data-chart="horizontal"]').forEach(host => drawHorizontal(
        host,
        readSeries(host),
        "Amount",
        value => `Q ${moneyFormat.format(value)}`,
        point => point.Label,
        "Ingresos por departamento"
    ));
    document.querySelectorAll('[data-chart="songs"]').forEach(host => drawHorizontal(
        host,
        readSeries(host),
        "Plays",
        numberFormat.format,
        point => `${point.Title} · ${point.Artist}`,
        "Canciones más escuchadas por reproducciones registradas"
    ));
})();
