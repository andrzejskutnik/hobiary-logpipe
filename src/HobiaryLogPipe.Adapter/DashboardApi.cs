using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HobiaryLogPipe.Adapter;

internal static class DashboardApi
{
    internal static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/dashboard", () =>
            Results.Content(BuildDashboardHtml(), "text/html; charset=utf-8"));

        return routes;
    }

    internal static string BuildDashboardHtml() =>
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Hobiary LogPipe Dashboard</title>
            <style>
                :root {
                    color-scheme: light;
                    --bg: #f5efe3;
                    --bg-soft: linear-gradient(180deg, #f7f2e8 0%, #efe4d2 100%);
                    --panel: rgba(255, 251, 245, 0.86);
                    --panel-strong: #fffaf3;
                    --line: rgba(78, 58, 37, 0.15);
                    --text: #2f2417;
                    --muted: #76624c;
                    --accent: #ba5a31;
                    --accent-soft: #f1c2a0;
                    --accent-2: #247a71;
                    --error: #ac2f2f;
                    --warn: #a96a1c;
                    --shadow: 0 18px 45px rgba(77, 58, 36, 0.12);
                    --radius-lg: 24px;
                    --radius-md: 16px;
                    --radius-sm: 12px;
                }

                * {
                    box-sizing: border-box;
                }

                body {
                    margin: 0;
                    min-height: 100vh;
                    font-family: "Segoe UI Variable Text", "Aptos", "Trebuchet MS", sans-serif;
                    color: var(--text);
                    background:
                        radial-gradient(circle at top left, rgba(186, 90, 49, 0.17), transparent 28%),
                        radial-gradient(circle at right 18%, rgba(36, 122, 113, 0.14), transparent 24%),
                        var(--bg-soft);
                }

                .shell {
                    max-width: 1480px;
                    margin: 0 auto;
                    padding: 28px;
                }

                .hero {
                    display: grid;
                    grid-template-columns: 1.8fr 1fr;
                    gap: 18px;
                    margin-bottom: 18px;
                }

                .hero-card,
                .panel {
                    background: var(--panel);
                    backdrop-filter: blur(14px);
                    border: 1px solid var(--line);
                    border-radius: var(--radius-lg);
                    box-shadow: var(--shadow);
                }

                .hero-card {
                    padding: 28px;
                    position: relative;
                    overflow: hidden;
                }

                .hero-card::after {
                    content: "";
                    position: absolute;
                    inset: auto -60px -70px auto;
                    width: 220px;
                    height: 220px;
                    border-radius: 50%;
                    background: radial-gradient(circle, rgba(186, 90, 49, 0.22), transparent 72%);
                }

                .eyebrow {
                    display: inline-flex;
                    align-items: center;
                    gap: 8px;
                    padding: 8px 12px;
                    border-radius: 999px;
                    background: rgba(186, 90, 49, 0.1);
                    color: var(--accent);
                    font-size: 12px;
                    letter-spacing: 0.12em;
                    text-transform: uppercase;
                    font-weight: 700;
                }

                h1 {
                    margin: 18px 0 10px;
                    font-family: Georgia, "Times New Roman", serif;
                    font-size: clamp(2rem, 3.4vw, 3.5rem);
                    line-height: 0.95;
                    letter-spacing: -0.03em;
                }

                .lead {
                    max-width: 64ch;
                    margin: 0;
                    color: var(--muted);
                    line-height: 1.6;
                }

                .hero-stats {
                    display: grid;
                    grid-template-columns: repeat(3, minmax(0, 1fr));
                    gap: 12px;
                    margin-top: 22px;
                }

                .mini-stat {
                    padding: 16px;
                    border-radius: var(--radius-md);
                    background: rgba(255, 255, 255, 0.56);
                    border: 1px solid rgba(78, 58, 37, 0.08);
                }

                .mini-stat .label {
                    color: var(--muted);
                    font-size: 12px;
                    text-transform: uppercase;
                    letter-spacing: 0.08em;
                }

                .mini-stat .value {
                    margin-top: 8px;
                    font-size: 2rem;
                    font-weight: 700;
                }

                .status-panel {
                    padding: 24px;
                    display: flex;
                    flex-direction: column;
                    gap: 14px;
                }

                .status-pill {
                    display: inline-flex;
                    align-items: center;
                    gap: 8px;
                    width: fit-content;
                    padding: 8px 12px;
                    border-radius: 999px;
                    background: rgba(36, 122, 113, 0.1);
                    color: var(--accent-2);
                    font-weight: 700;
                }

                .grid {
                    display: grid;
                    grid-template-columns: 320px minmax(0, 1fr);
                    gap: 18px;
                }

                .panel {
                    padding: 18px;
                }

                .panel h2,
                .panel h3 {
                    margin: 0;
                    font-size: 1rem;
                    letter-spacing: 0.04em;
                    text-transform: uppercase;
                }

                .panel-header {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    gap: 16px;
                    margin-bottom: 14px;
                }

                .panel-subtitle {
                    margin: 6px 0 0;
                    color: var(--muted);
                    font-size: 0.95rem;
                }

                .plugin-list {
                    display: flex;
                    flex-direction: column;
                    gap: 10px;
                    max-height: 760px;
                    overflow: auto;
                    padding-right: 4px;
                }

                .plugin-card {
                    width: 100%;
                    text-align: left;
                    background: var(--panel-strong);
                    border: 1px solid transparent;
                    border-radius: var(--radius-md);
                    padding: 14px;
                    cursor: pointer;
                    transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease;
                }

                .plugin-card:hover,
                .plugin-card:focus-visible,
                .plugin-card.active {
                    transform: translateY(-1px);
                    border-color: rgba(186, 90, 49, 0.4);
                    box-shadow: 0 12px 28px rgba(186, 90, 49, 0.14);
                    outline: none;
                }

                .plugin-top {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    gap: 12px;
                }

                .plugin-name {
                    font-weight: 700;
                    font-size: 1rem;
                }

                .badge {
                    display: inline-flex;
                    align-items: center;
                    border-radius: 999px;
                    padding: 5px 10px;
                    font-size: 12px;
                    font-weight: 700;
                    background: rgba(36, 122, 113, 0.1);
                    color: var(--accent-2);
                }

                .badge.offline,
                .badge.deregistered {
                    background: rgba(172, 47, 47, 0.1);
                    color: var(--error);
                }

                .plugin-meta {
                    margin-top: 10px;
                    color: var(--muted);
                    font-size: 0.92rem;
                    display: grid;
                    gap: 4px;
                }

                .workspace {
                    display: grid;
                    gap: 18px;
                }

                .detail-grid {
                    display: grid;
                    grid-template-columns: repeat(4, minmax(0, 1fr));
                    gap: 12px;
                    margin-top: 16px;
                }

                .metric {
                    border-radius: var(--radius-md);
                    background: rgba(255, 255, 255, 0.55);
                    border: 1px solid rgba(78, 58, 37, 0.08);
                    padding: 14px;
                }

                .metric .label {
                    font-size: 12px;
                    text-transform: uppercase;
                    letter-spacing: 0.08em;
                    color: var(--muted);
                }

                .metric .value {
                    margin-top: 8px;
                    font-size: 1.3rem;
                    font-weight: 700;
                }

                .levels {
                    display: grid;
                    grid-template-columns: repeat(6, minmax(0, 1fr));
                    gap: 10px;
                }

                .level-box {
                    border-radius: var(--radius-sm);
                    padding: 14px;
                    background: rgba(255, 255, 255, 0.58);
                    border: 1px solid rgba(78, 58, 37, 0.08);
                }

                .level-box.warn { border-color: rgba(169, 106, 28, 0.25); }
                .level-box.error { border-color: rgba(172, 47, 47, 0.25); }
                .level-box.fatal { border-color: rgba(108, 16, 16, 0.28); }

                .filters {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 10px;
                    margin-bottom: 14px;
                }

                label span {
                    display: block;
                    font-size: 12px;
                    text-transform: uppercase;
                    letter-spacing: 0.08em;
                    color: var(--muted);
                    margin-bottom: 6px;
                }

                select,
                input,
                button {
                    font: inherit;
                }

                select,
                input {
                    min-width: 130px;
                    border-radius: 12px;
                    border: 1px solid rgba(78, 58, 37, 0.15);
                    background: rgba(255, 255, 255, 0.82);
                    padding: 10px 12px;
                    color: var(--text);
                }

                button {
                    border: none;
                    border-radius: 12px;
                    padding: 11px 16px;
                    background: var(--accent);
                    color: white;
                    font-weight: 700;
                    cursor: pointer;
                    box-shadow: 0 10px 24px rgba(186, 90, 49, 0.22);
                }

                button:disabled {
                    opacity: 0.45;
                    cursor: not-allowed;
                    box-shadow: none;
                }

                button.secondary {
                    background: rgba(47, 36, 23, 0.08);
                    color: var(--text);
                    box-shadow: none;
                }

                .log-grid {
                    display: grid;
                    grid-template-columns: 1.25fr 1fr;
                    gap: 18px;
                }

                .stream,
                .history {
                    display: grid;
                    gap: 12px;
                }

                .feed,
                .history-list {
                    display: grid;
                    gap: 10px;
                    max-height: 520px;
                    overflow: auto;
                    padding-right: 4px;
                }

                .log-item {
                    border-radius: var(--radius-md);
                    border: 1px solid rgba(78, 58, 37, 0.1);
                    background: rgba(255, 255, 255, 0.72);
                    padding: 14px;
                    animation: rise 220ms ease;
                }

                .log-meta {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 8px 12px;
                    margin-bottom: 10px;
                    font-size: 12px;
                    text-transform: uppercase;
                    letter-spacing: 0.06em;
                    color: var(--muted);
                }

                .chip {
                    display: inline-flex;
                    align-items: center;
                    padding: 4px 8px;
                    border-radius: 999px;
                    font-weight: 700;
                    background: rgba(47, 36, 23, 0.08);
                }

                .chip.error,
                .chip.fatal { background: rgba(172, 47, 47, 0.12); color: var(--error); }
                .chip.warn { background: rgba(169, 106, 28, 0.14); color: var(--warn); }
                .chip.info { background: rgba(36, 122, 113, 0.11); color: var(--accent-2); }

                pre {
                    margin: 0;
                    white-space: pre-wrap;
                    word-break: break-word;
                    font-family: Consolas, "Cascadia Mono", monospace;
                    font-size: 13px;
                    line-height: 1.55;
                }

                .muted {
                    color: var(--muted);
                }

                .empty {
                    padding: 18px;
                    border-radius: var(--radius-md);
                    border: 1px dashed rgba(78, 58, 37, 0.25);
                    color: var(--muted);
                    background: rgba(255, 255, 255, 0.45);
                }

                @keyframes rise {
                    from {
                        opacity: 0;
                        transform: translateY(6px);
                    }
                    to {
                        opacity: 1;
                        transform: translateY(0);
                    }
                }

                @media (max-width: 1100px) {
                    .hero,
                    .grid,
                    .log-grid {
                        grid-template-columns: 1fr;
                    }

                    .detail-grid,
                    .levels,
                    .hero-stats {
                        grid-template-columns: repeat(2, minmax(0, 1fr));
                    }
                }

                @media (max-width: 720px) {
                    .shell {
                        padding: 16px;
                    }

                    .detail-grid,
                    .levels,
                    .hero-stats {
                        grid-template-columns: 1fr;
                    }

                    .filters {
                        flex-direction: column;
                    }

                    select,
                    input,
                    button {
                        width: 100%;
                    }
                }
            </style>
        </head>
        <body>
            <div class="shell">
                <section class="hero">
                    <article class="hero-card">
                        <div class="eyebrow">Embedded dashboard</div>
                        <h1>Hobiary LogPipe<br>operational console</h1>
                        <p class="lead">
                            Read-only view over plugins, log volume and live SSE traffic.
                            This page uses the Adapter API directly, so it stays deployable as a single binary.
                        </p>
                        <div class="hero-stats">
                            <div class="mini-stat">
                                <div class="label">Plugins</div>
                                <div class="value" id="heroPluginCount">0</div>
                            </div>
                            <div class="mini-stat">
                                <div class="label">Selected plugin</div>
                                <div class="value" id="heroSelected">-</div>
                            </div>
                            <div class="mini-stat">
                                <div class="label">Live events</div>
                                <div class="value" id="heroLiveCount">0</div>
                            </div>
                        </div>
                    </article>

                    <aside class="hero-card status-panel">
                        <div class="status-pill" id="connectionState">Loading API</div>
                        <div>
                            <h2>Live stream</h2>
                            <p class="panel-subtitle">SSE source switches automatically when a plugin is selected.</p>
                        </div>
                        <div class="metric">
                            <div class="label">Refresh cadence</div>
                            <div class="value">15s snapshot</div>
                        </div>
                        <div class="metric">
                            <div class="label">SSE reconnects</div>
                            <div class="value" id="streamReconnects">0</div>
                        </div>
                        <div class="metric">
                            <div class="label">API endpoints</div>
                            <div class="value" style="font-size:1rem;line-height:1.5;">/api/plugins, /api/logs, /api/logs/stream</div>
                        </div>
                        <button class="secondary" id="refreshAllButton" type="button">Refresh now</button>
                    </aside>
                </section>

                <section class="grid">
                    <aside class="panel">
                        <div class="panel-header">
                            <div>
                                <h2>Plugins</h2>
                                <p class="panel-subtitle">Registry snapshot from the adapter.</p>
                            </div>
                        </div>
                        <div class="plugin-list" id="pluginList">
                            <div class="empty">Loading plugin registry...</div>
                        </div>
                    </aside>

                    <div class="workspace">
                        <section class="panel">
                            <div class="panel-header">
                                <div>
                                    <h2 id="detailTitle">Plugin detail</h2>
                                    <p class="panel-subtitle" id="detailSubtitle">Choose a plugin to load stats and history.</p>
                                </div>
                            </div>
                            <div class="detail-grid" id="detailGrid"></div>
                        </section>

                        <section class="panel">
                            <div class="panel-header">
                                <div>
                                    <h2>Level distribution</h2>
                                    <p class="panel-subtitle">Rolling last hour per selected plugin.</p>
                                </div>
                            </div>
                            <div class="levels" id="levelGrid"></div>
                        </section>

                        <section class="panel">
                            <div class="panel-header">
                                <div>
                                    <h2>History query</h2>
                                    <p class="panel-subtitle">Fetch recent logs with the existing `/api/logs` endpoint.</p>
                                </div>
                            </div>
                            <div class="filters">
                                <label>
                                    <span>From</span>
                                    <input id="fromFilter" type="datetime-local">
                                </label>
                                <label>
                                    <span>To</span>
                                    <input id="toFilter" type="datetime-local">
                                </label>
                                <label>
                                    <span>Level</span>
                                    <select id="levelFilter">
                                        <option value="">All</option>
                                        <option>Trace</option>
                                        <option>Debug</option>
                                        <option>Info</option>
                                        <option>Warn</option>
                                        <option>Error</option>
                                        <option>Fatal</option>
                                    </select>
                                </label>
                                <label>
                                    <span>Limit</span>
                                    <input id="limitFilter" type="number" min="1" max="1000" value="100">
                                </label>
                                <button id="loadLogsButton" type="button">Load logs</button>
                                <button class="secondary" id="exportLogsButton" type="button">Export NDJSON</button>
                                <button class="secondary" id="clearLiveButton" type="button">Clear live feed</button>
                            </div>
                            <p class="panel-subtitle" id="queryStatus">Last hour by default. Change the range to narrow down stats, history and export.</p>
                            <div class="panel-header" style="margin-bottom:12px;">
                                <p class="panel-subtitle" id="historyPageStatus">Page 1</p>
                                <div style="display:flex;gap:10px;flex-wrap:wrap;">
                                    <button class="secondary" id="prevPageButton" type="button" disabled>Previous page</button>
                                    <button class="secondary" id="nextPageButton" type="button" disabled>Next page</button>
                                </div>
                            </div>

                            <div class="log-grid">
                                <div class="history">
                                    <h3>Historical logs</h3>
                                    <div class="history-list" id="historyList"></div>
                                </div>
                                <div class="stream">
                                    <h3>Live feed</h3>
                                    <div class="feed" id="liveFeed"></div>
                                </div>
                            </div>
                        </section>
                    </div>
                </section>
            </div>

            <script>
                const state = {
                    plugins: [],
                    selectedPluginId: null,
                    liveCount: 0,
                    eventSource: null,
                    reconnectCount: 0,
                    historyCursor: null,
                    nextHistoryCursor: null,
                    cursorStack: [],
                };

                const pageContext = {
                    apiKey: new URLSearchParams(window.location.search).get("apiKey") ?? "",
                };

                const els = {
                    connectionState: document.getElementById("connectionState"),
                    streamReconnects: document.getElementById("streamReconnects"),
                    pluginList: document.getElementById("pluginList"),
                    detailTitle: document.getElementById("detailTitle"),
                    detailSubtitle: document.getElementById("detailSubtitle"),
                    detailGrid: document.getElementById("detailGrid"),
                    levelGrid: document.getElementById("levelGrid"),
                    historyList: document.getElementById("historyList"),
                    liveFeed: document.getElementById("liveFeed"),
                    heroPluginCount: document.getElementById("heroPluginCount"),
                    heroSelected: document.getElementById("heroSelected"),
                    heroLiveCount: document.getElementById("heroLiveCount"),
                    fromFilter: document.getElementById("fromFilter"),
                    toFilter: document.getElementById("toFilter"),
                    levelFilter: document.getElementById("levelFilter"),
                    limitFilter: document.getElementById("limitFilter"),
                    queryStatus: document.getElementById("queryStatus"),
                    historyPageStatus: document.getElementById("historyPageStatus"),
                    loadLogsButton: document.getElementById("loadLogsButton"),
                    exportLogsButton: document.getElementById("exportLogsButton"),
                    clearLiveButton: document.getElementById("clearLiveButton"),
                    prevPageButton: document.getElementById("prevPageButton"),
                    nextPageButton: document.getElementById("nextPageButton"),
                    refreshAllButton: document.getElementById("refreshAllButton"),
                };

                const levelOrder = ["trace", "debug", "info", "warn", "error", "fatal"];

                const emptyBox = (text) => `<div class="empty">${escapeHtml(text)}</div>`;

                function escapeHtml(value) {
                    return String(value ?? "")
                        .replaceAll("&", "&amp;")
                        .replaceAll("<", "&lt;")
                        .replaceAll(">", "&gt;")
                        .replaceAll('"', "&quot;")
                        .replaceAll("'", "&#39;");
                }

                function formatDate(value) {
                    if (!value) return "-";
                    const date = new Date(value);
                    return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
                }

                function formatUs(value) {
                    if (typeof value !== "number") return "-";
                    return new Date(Math.floor(value / 1000)).toLocaleString();
                }

                function toDateTimeLocalValue(date) {
                    const pad = (value) => String(value).padStart(2, "0");
                    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
                }

                function initializeDefaultRange() {
                    const now = new Date();
                    const from = new Date(now.getTime() - 60 * 60 * 1000);
                    els.fromFilter.value = toDateTimeLocalValue(from);
                    els.toFilter.value = toDateTimeLocalValue(now);
                }

                function readRangeFilters() {
                    const params = new URLSearchParams();
                    const fromValue = els.fromFilter.value;
                    const toValue = els.toFilter.value;

                    if (fromValue) {
                        const from = new Date(fromValue);
                        if (Number.isNaN(from.getTime())) throw new Error("Invalid 'from' date.");
                        params.set("from", from.toISOString());
                    }

                    if (toValue) {
                        const to = new Date(toValue);
                        if (Number.isNaN(to.getTime())) throw new Error("Invalid 'to' date.");
                        params.set("to", to.toISOString());
                    }

                    if (fromValue && toValue && new Date(fromValue) > new Date(toValue)) {
                        throw new Error("'From' must be earlier than 'to'.");
                    }

                    return params;
                }

                function setQueryStatus(text, mode = "normal") {
                    els.queryStatus.textContent = text;
                    els.queryStatus.style.color = mode === "error"
                        ? "#ac2f2f"
                        : mode === "warn"
                            ? "#a96a1c"
                            : "var(--muted)";
                }

                function withApiKey(url) {
                    const absolute = new URL(url, window.location.origin);
                    if (pageContext.apiKey && !absolute.searchParams.has("apiKey")) {
                        absolute.searchParams.set("apiKey", pageContext.apiKey);
                    }
                    return `${absolute.pathname}${absolute.search}`;
                }

                function setConnection(text, mode = "ok") {
                    els.connectionState.textContent = text;
                    els.connectionState.style.background = mode === "error"
                        ? "rgba(172, 47, 47, 0.1)"
                        : mode === "warn"
                            ? "rgba(169, 106, 28, 0.14)"
                            : "rgba(36, 122, 113, 0.1)";
                    els.connectionState.style.color = mode === "error"
                        ? "#ac2f2f"
                        : mode === "warn"
                            ? "#a96a1c"
                            : "#247a71";
                    els.streamReconnects.textContent = String(state.reconnectCount);
                }

                async function fetchJson(url) {
                    const response = await fetch(withApiKey(url), { headers: { "Accept": "application/json" } });
                    if (!response.ok) {
                        const text = await response.text();
                        throw new Error(`${response.status} ${response.statusText}: ${text}`);
                    }
                    return await response.json();
                }

                function renderPlugins() {
                    els.heroPluginCount.textContent = String(state.plugins.length);
                    els.heroSelected.textContent = state.selectedPluginId ?? "-";

                    if (state.plugins.length === 0) {
                        els.pluginList.innerHTML = emptyBox("No plugins registered yet.");
                        return;
                    }

                    els.pluginList.innerHTML = state.plugins.map((plugin) => {
                        const isActive = plugin.pluginId === state.selectedPluginId;
                        const statusClass = plugin.isDeregistered ? "badge deregistered" : "badge";
                        const statusText = plugin.isDeregistered ? "deregistered" : "registered";
                        return `
                            <button class="plugin-card ${isActive ? "active" : ""}" type="button" data-plugin-id="${escapeHtml(plugin.pluginId)}">
                                <div class="plugin-top">
                                    <div class="plugin-name">${escapeHtml(plugin.pluginId)}</div>
                                    <span class="${statusClass}">${statusText}</span>
                                </div>
                                <div class="plugin-meta">
                                    <div>format: ${escapeHtml(plugin.messageFormat)}</div>
                                    <div>fields: ${escapeHtml(plugin.levelField)} / ${escapeHtml(plugin.messageField)}</div>
                                    <div>registered: ${escapeHtml(formatDate(plugin.registeredAt))}</div>
                                </div>
                            </button>`;
                    }).join("");

                    for (const button of els.pluginList.querySelectorAll("[data-plugin-id]")) {
                        button.addEventListener("click", () => selectPlugin(button.dataset.pluginId));
                    }
                }

                function renderDetail(detail) {
                    els.detailTitle.textContent = detail ? `Plugin detail: ${detail.pluginId}` : "Plugin detail";
                    els.detailSubtitle.textContent = detail
                        ? `message format ${detail.messageFormat}, fields ${detail.levelField}/${detail.messageField}`
                        : "Choose a plugin to load stats and history.";

                    if (!detail) {
                        els.detailGrid.innerHTML = emptyBox("No plugin selected.");
                        return;
                    }

                    const items = [
                        ["Records", detail.recordCount ?? 0],
                        ["Last heartbeat", formatDate(detail.lastHeartbeatAt)],
                        ["Online", detail.isOnline == null ? "n/a" : detail.isOnline ? "yes" : "no"],
                        ["Source/service", `${detail.sourceField} / ${detail.serviceField}`],
                    ];

                    els.detailGrid.innerHTML = items.map(([label, value]) => `
                        <div class="metric">
                            <div class="label">${escapeHtml(label)}</div>
                            <div class="value">${escapeHtml(value)}</div>
                        </div>`).join("");
                }

                function renderLevels(stats) {
                    if (!stats) {
                        els.levelGrid.innerHTML = emptyBox("Stats unavailable.");
                        return;
                    }

                    els.levelGrid.innerHTML = levelOrder.map((level) => {
                        const value = stats.byLevel?.[level] ?? 0;
                        return `
                            <div class="level-box ${level}">
                                <div class="label">${escapeHtml(level)}</div>
                                <div class="value">${escapeHtml(value)}</div>
                            </div>`;
                    }).join("");
                }

                function renderLogItem(entry, pluginIdOverride) {
                    const eventType = entry.eventType ? String(entry.eventType) : null;
                    const level = String(entry.level ?? "info").toLowerCase();
                    const pluginId = pluginIdOverride ?? entry.pluginId ?? state.selectedPluginId ?? "-";
                    const payload = entry.payload ? entry.payload : "(no payload)";
                    const traceBits = [entry.traceId, entry.correlationId].filter(Boolean).join(" | ");
                    return `
                        <article class="log-item">
                            <div class="log-meta">
                                <span class="chip ${escapeHtml(level)}">${escapeHtml(level)}</span>
                                ${eventType ? `<span class="chip info">${escapeHtml(eventType)}</span>` : ""}
                                <span>${escapeHtml(pluginId)}</span>
                                <span>${escapeHtml(formatUs(entry.timestampUs))}</span>
                                ${entry.source ? `<span>src: ${escapeHtml(entry.source)}</span>` : ""}
                                ${entry.service ? `<span>svc: ${escapeHtml(entry.service)}</span>` : ""}
                            </div>
                            <pre>${escapeHtml(payload)}</pre>
                            ${traceBits ? `<div class="panel-subtitle" style="margin-top:10px;">${escapeHtml(traceBits)}</div>` : ""}
                        </article>`;
                }

                function renderHistory(entries) {
                    if (!entries || entries.length === 0) {
                        els.historyList.innerHTML = emptyBox("No historical logs for the selected filter.");
                        return;
                    }
                    els.historyList.innerHTML = entries.map((entry) => renderLogItem(entry)).join("");
                }

                function updateHistoryPager(page, count) {
                    state.nextHistoryCursor = page?.nextCursor ?? null;
                    els.prevPageButton.disabled = state.cursorStack.length === 0;
                    els.nextPageButton.disabled = state.nextHistoryCursor == null;
                    const pageNumber = state.cursorStack.length + 1;
                    els.historyPageStatus.textContent = `Page ${pageNumber} · ${count} items`;
                }

                function prependLiveEntry(entry) {
                    const markup = renderLogItem(entry, entry.pluginId);
                    els.liveFeed.insertAdjacentHTML("afterbegin", markup);
                    while (els.liveFeed.children.length > 40) {
                        els.liveFeed.lastElementChild.remove();
                    }
                }

                async function loadPlugins() {
                    const plugins = await fetchJson("/api/plugins");
                    state.plugins = Array.isArray(plugins) ? plugins : [];

                    if (!state.selectedPluginId || !state.plugins.some((x) => x.pluginId === state.selectedPluginId)) {
                        state.selectedPluginId = state.plugins[0]?.pluginId ?? null;
                    }

                    renderPlugins();
                }

                async function loadDetailAndStats() {
                    if (!state.selectedPluginId) {
                        renderDetail(null);
                        renderLevels(null);
                        renderHistory([]);
                        connectLiveStream();
                        return;
                    }

                    const pluginId = encodeURIComponent(state.selectedPluginId);
                    const range = readRangeFilters();
                    const [detail, stats] = await Promise.all([
                        fetchJson(`/api/plugins/${pluginId}`),
                        fetchJson(`/api/plugins/${pluginId}/stats?${range.toString()}`),
                    ]);

                    renderDetail(detail);
                    renderLevels(stats);
                    setQueryStatus(`Loaded stats for ${state.selectedPluginId} using the active time range.`);
                }

                async function loadLogs(requestedCursor = null, pushHistory = false) {
                    if (!state.selectedPluginId) {
                        renderHistory([]);
                        updateHistoryPager(null, 0);
                        return;
                    }

                    if (pushHistory && state.historyCursor != null) {
                        state.cursorStack.push(state.historyCursor);
                    }

                    const params = readRangeFilters();
                    params.set("pluginId", state.selectedPluginId);
                    params.set("paged", "true");
                    params.set(
                        "limit",
                        String(Math.min(1000, Math.max(1, Number(els.limitFilter.value || 100)))));
                    if (requestedCursor != null) {
                        params.set("cursor", String(requestedCursor));
                    }
                    const level = els.levelFilter.value;
                    if (level) params.set("level", level);

                    const page = await fetchJson(`/api/logs?${params.toString()}`);
                    const entries = Array.isArray(page) ? page : (page.items ?? []);
                    state.historyCursor = page.cursor ?? requestedCursor;
                    renderHistory(entries);
                    updateHistoryPager(page, entries.length);
                    setQueryStatus(`Loaded ${entries.length} log entries.`);
                }

                function exportLogs() {
                    if (!state.selectedPluginId) {
                        setQueryStatus("Select a plugin before exporting NDJSON.", "warn");
                        return;
                    }

                    const params = readRangeFilters();
                    params.set("limit", String(Math.min(1000, Math.max(1, Number(els.limitFilter.value || 100)))));
                    if (state.historyCursor != null) {
                        params.set("cursor", String(state.historyCursor));
                    }
                    const level = els.levelFilter.value;
                    if (level) params.set("level", level);

                    const pluginId = encodeURIComponent(state.selectedPluginId);
                    setQueryStatus(`Starting NDJSON export for ${state.selectedPluginId} (current page).`);
                    window.location.assign(withApiKey(`/api/plugins/${pluginId}/logs/export?${params.toString()}`));
                }

                function clearLiveFeed() {
                    els.liveFeed.innerHTML = emptyBox("Live feed cleared. Waiting for new events...");
                }

                function connectLiveStream() {
                    if (state.eventSource) {
                        state.eventSource.close();
                        state.eventSource = null;
                    }

                    const url = state.selectedPluginId
                        ? `/api/logs/stream/${encodeURIComponent(state.selectedPluginId)}`
                        : "/api/logs/stream";

                    els.liveFeed.innerHTML = emptyBox("Waiting for live events...");
                    setConnection(`Connecting live stream: ${state.selectedPluginId ?? "all plugins"}`, "warn");

                    const es = new EventSource(withApiKey(url));
                    state.eventSource = es;
                    es.onopen = () => {
                        state.reconnectCount = 0;
                        setConnection(`Live stream connected: ${state.selectedPluginId ?? "all plugins"}`);
                    };
                    es.onerror = () => {
                        state.reconnectCount += 1;
                        setConnection(`Live stream reconnecting (${state.reconnectCount})`, "warn");
                    };
                    es.onmessage = (event) => {
                        try {
                            const payload = JSON.parse(event.data);
                            if (els.liveFeed.firstElementChild?.classList.contains("empty")) {
                                els.liveFeed.innerHTML = "";
                            }
                            prependLiveEntry(payload);
                            state.liveCount += 1;
                            els.heroLiveCount.textContent = String(state.liveCount);
                        } catch {
                            setConnection("Received malformed SSE payload", "warn");
                        }
                    };
                }

                async function selectPlugin(pluginId) {
                    state.selectedPluginId = pluginId;
                    state.historyCursor = null;
                    state.nextHistoryCursor = null;
                    state.cursorStack = [];
                    renderPlugins();
                    await refreshWorkspace();
                }

                async function refreshWorkspace() {
                    try {
                        await loadDetailAndStats();
                        await loadLogs(state.historyCursor);
                        connectLiveStream();
                    } catch (error) {
                        setConnection(error.message || "API error", "error");
                        setQueryStatus(error.message || "API error", "error");
                    }
                }

                async function refreshAll() {
                    try {
                        setConnection("Refreshing");
                        await loadPlugins();
                        await refreshWorkspace();
                    } catch (error) {
                        setConnection(error.message || "API error", "error");
                        setQueryStatus(error.message || "API error", "error");
                    }
                }

                els.loadLogsButton.addEventListener("click", () => {
                    state.historyCursor = null;
                    state.nextHistoryCursor = null;
                    state.cursorStack = [];
                    void loadLogs(null);
                });
                els.exportLogsButton.addEventListener("click", () => {
                    try {
                        exportLogs();
                    } catch (error) {
                        setQueryStatus(error.message || "Invalid export parameters.", "error");
                    }
                });
                els.clearLiveButton.addEventListener("click", clearLiveFeed);
                els.prevPageButton.addEventListener("click", () => {
                    const previousCursor = state.cursorStack.pop();
                    if (previousCursor !== undefined) {
                        void loadLogs(previousCursor);
                    }
                });
                els.nextPageButton.addEventListener("click", () => {
                    if (state.nextHistoryCursor != null) {
                        void loadLogs(state.nextHistoryCursor, true);
                    }
                });
                els.refreshAllButton.addEventListener("click", () => { void refreshAll(); });

                window.addEventListener("beforeunload", () => {
                    if (state.eventSource) {
                        state.eventSource.close();
                    }
                });

                initializeDefaultRange();
                setInterval(() => { void refreshAll(); }, 15000);
                void refreshAll();
            </script>
        </body>
        </html>
        """;
}