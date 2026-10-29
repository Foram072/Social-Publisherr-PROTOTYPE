// ============================================================
// GOOGLE ADS INTEGRATION
// ============================================================

// Safe JSON fetch: never crashes on an empty body and surfaces the server's error message.
async function fetchJson(url, options) {
    const res = await fetch(url, options);
    const text = await res.text();

    let data = null;
    try {
        data = text ? JSON.parse(text) : null;
    } catch {
        /* response was not JSON */
    }

    if (!res.ok) {
        throw new Error((data && data.error) || `Request failed (${res.status})`);
    }
    return data;
}

async function loadGoogleAdsSection() {
    if (!currentClientId) return;

    const actionBtn = document.getElementById("googleAdsActionBtn");
    const contentEl = document.getElementById("googleAdsContent");
    if (!actionBtn || !contentEl) return;

    try {
        // 1. Check connection status
        const status = await fetchJson(`${API_ORIGIN}/api/googleads/status/${currentClientId}`);

        if (!status || !status.isConnected || status.status === "NotConnected") {
            // Show "Connect Google Ads" button
            actionBtn.innerHTML = `
                <button class="btn btn-sm btn-outline-danger" onclick="connectGoogleAds()">
                    <i class="bi bi-google me-1"></i> Connect Google Ads
                </button>
            `;
            contentEl.innerHTML = `
                <div class="alert alert-light border text-center py-4">
                    <i class="bi bi-google text-danger fs-1 d-block mb-2"></i>
                    <h6 class="fw-bold text-dark">No Google Ads Account Connected</h6>
                    <p class="text-muted small mb-3">
                        Connect your Google Ads account to see campaign performance, spend, clicks, CTR, CPC and more.
                    </p>
                    <button class="btn btn-danger" onclick="connectGoogleAds()">
                        <i class="bi bi-google me-1"></i> Connect Google Ads Account
                    </button>
                </div>
            `;
            return;
        }

        if (status.status === "NeedsReconnection") {
            actionBtn.innerHTML = `
                <button class="btn btn-sm btn-warning" onclick="connectGoogleAds()">
                    <i class="bi bi-arrow-repeat me-1"></i> Reconnect
                </button>
            `;
            contentEl.innerHTML = `
                <div class="alert alert-warning py-3">
                    <i class="bi bi-exclamation-triangle-fill me-2"></i>
                    Your Google Ads token has expired. Please reconnect to continue viewing data.
                </div>
            `;
            return;
        }

        // 2. Connected! Show disconnect button and load overview
        actionBtn.innerHTML = `
            <div class="d-flex gap-2 align-items-center">
                <span class="badge bg-success-subtle text-success border border-success-subtle">
                    <i class="bi bi-check-circle-fill me-1"></i> Connected: ${escapeHtml(status.accountName || status.customerId || "")}
                </span>
                <button class="btn btn-sm btn-outline-secondary" onclick="refreshGoogleAds()" title="Refresh">
                    <i class="bi bi-arrow-clockwise"></i>
                </button>
                <button class="btn btn-sm btn-outline-danger" onclick="disconnectGoogleAds()" title="Disconnect">
                    <i class="bi bi-x-circle"></i>
                </button>
            </div>
        `;

        // 3. Fetch overview data
        contentEl.innerHTML = `
            <div class="text-center py-3">
                <div class="spinner-border spinner-border-sm text-danger"></div>
                <span class="text-muted ms-2 small">Fetching Google Ads metrics...</span>
            </div>
        `;

        const overview = await fetchJson(`${API_ORIGIN}/api/googleads/overview/${currentClientId}`);

        // Show the account name in the badge once we have it
        if (overview && overview.accountName) {
            const badge = actionBtn.querySelector(".badge");
            if (badge) {
                badge.innerHTML = `<i class="bi bi-check-circle-fill me-1"></i> Connected: ${escapeHtml(overview.accountName)}`;
            }
        }

        renderGoogleAdsOverview(overview || {});

    } catch (err) {
        contentEl.innerHTML = `
            <div class="alert alert-danger py-2">
                <i class="bi bi-exclamation-triangle-fill me-1"></i>
                Failed to load Google Ads data: ${escapeHtml(err.message)}
            </div>
        `;
    }
}

function renderGoogleAdsOverview(data) {
    const contentEl = document.getElementById("googleAdsContent");
    if (!contentEl) return;

    const currency = data.currencyCode || "USD";
    const cs = currency === "USD" ? "$" : currency === "EUR" ? "€" : currency === "INR" ? "₹" : currency + " ";

    // KPI Cards
    let kpiHtml = `
        <div class="row g-3 mb-4">
            <div class="col-md-2">
                <div class="card border-0 shadow-sm p-3 bg-white h-100">
                    <div class="text-muted small fw-semibold">TOTAL SPEND</div>
                    <div class="fs-5 fw-bold text-danger mt-1">${cs}${Number(data.totalSpend || 0).toFixed(2)}</div>
                </div>
            </div>
            <div class="col-md-2">
                <div class="card border-0 shadow-sm p-3 bg-white h-100">
                    <div class="text-muted small fw-semibold">IMPRESSIONS</div>
                    <div class="fs-5 fw-bold text-primary mt-1">${Number(data.totalImpressions || 0).toLocaleString()}</div>
                </div>
            </div>
            <div class="col-md-2">
                <div class="card border-0 shadow-sm p-3 bg-white h-100">
                    <div class="text-muted small fw-semibold">CLICKS</div>
                    <div class="fs-5 fw-bold text-info mt-1">${Number(data.totalClicks || 0).toLocaleString()}</div>
                </div>
            </div>
            <div class="col-md-2">
                <div class="card border-0 shadow-sm p-3 bg-white h-100">
                    <div class="text-muted small fw-semibold">CTR</div>
                    <div class="fs-5 fw-bold text-success mt-1">${Number(data.ctr || 0).toFixed(2)}%</div>
                </div>
            </div>
            <div class="col-md-2">
                <div class="card border-0 shadow-sm p-3 bg-white h-100">
                    <div class="text-muted small fw-semibold">AVG CPC</div>
                    <div class="fs-5 fw-bold text-warning-emphasis mt-1">${cs}${Number(data.avgCPC || 0).toFixed(2)}</div>
                </div>
            </div>
            <div class="col-md-2">
                <div class="card border-0 shadow-sm p-3 bg-white h-100">
                    <div class="text-muted small fw-semibold">CONVERSIONS</div>
                    <div class="fs-5 fw-bold text-dark mt-1">${Number(data.totalConversions || 0).toLocaleString()}</div>
                </div>
            </div>
        </div>
    `;

    // Campaign table
    let campaignsHtml = "";
    if (data.campaigns && data.campaigns.length > 0) {
        let rows = "";
        data.campaigns.forEach(c => {
            const statusClass = c.status === "ENABLED" ? "bg-success" : c.status === "PAUSED" ? "bg-warning" : "bg-secondary";
            rows += `
                <tr>
                    <td class="fw-semibold">${escapeHtml(c.campaignName)}</td>
                    <td><span class="badge ${statusClass} text-uppercase" style="font-size:10px;">${escapeHtml(c.status)}</span></td>
                    <td class="small text-muted">${escapeHtml(c.campaignType || "-")}</td>
                    <td>${cs}${Number(c.budget || 0).toFixed(2)}</td>
                    <td class="fw-bold text-danger">${cs}${Number(c.spend || 0).toFixed(2)}</td>
                    <td>${Number(c.impressions || 0).toLocaleString()}</td>
                    <td>${Number(c.clicks || 0).toLocaleString()}</td>
                    <td>${Number(c.ctr || 0).toFixed(2)}%</td>
                    <td>${cs}${Number(c.avgCPC || 0).toFixed(2)}</td>
                    <td>${Number(c.conversions || 0)}</td>
                </tr>
            `;
        });

        campaignsHtml = `
            <div class="mb-3">
                <h6 class="fw-bold text-dark mb-2">
                    <i class="bi bi-megaphone-fill text-danger me-1"></i> Campaigns
                </h6>
                <div class="table-responsive">
                    <table class="table table-sm table-hover align-middle mb-0 bg-white shadow-sm rounded">
                        <thead class="table-dark">
                            <tr>
                                <th>Campaign</th>
                                <th>Status</th>
                                <th>Type</th>
                                <th>Budget</th>
                                <th>Spend</th>
                                <th>Impressions</th>
                                <th>Clicks</th>
                                <th>CTR</th>
                                <th>Avg CPC</th>
                                <th>Conv.</th>
                            </tr>
                        </thead>
                        <tbody>${rows}</tbody>
                    </table>
                </div>
            </div>
        `;
    } else {
        campaignsHtml = `
            <div class="alert alert-light border text-center py-3 small text-muted">
                <i class="bi bi-megaphone fs-4 d-block mb-1 text-secondary"></i>
                No campaigns found in this Google Ads account yet.
            </div>
        `;
    }

    // Date range label
    let dateLabel = data.dateRangeLabel
        ? `<div class="text-muted small mb-3"><i class="bi bi-calendar3 me-1"></i> ${escapeHtml(data.dateRangeLabel)}</div>`
        : "";

    contentEl.innerHTML = dateLabel + kpiHtml + campaignsHtml;
}

// Single message listener (registered once) so repeated clicks don't stack handlers.
let googleAdsMessageListenerAdded = false;

function connectGoogleAds() {
    if (!currentClientId) {
        alert("Please select a brand/client workspace first.");
        return;
    }

    if (!googleAdsMessageListenerAdded) {
        googleAdsMessageListenerAdded = true;
        window.addEventListener("message", function (event) {
            if (!event.data) return;

            if (event.data.type === "googleads-connected") {
                // Refresh the Google Ads section
                setTimeout(() => loadGoogleAdsSection(), 500);
            } else if (event.data.type === "googleads-failed") {
                loadGoogleAdsSection();
            }
        });
    }

    const url = `${API_ORIGIN}/api/googleads/connect?clientId=${currentClientId}`;
    window.open(url, "googleads_connect", "width=600,height=700,scrollbars=yes");
}

async function disconnectGoogleAds() {
    if (!currentClientId) return;
    if (!confirm("Are you sure you want to disconnect your Google Ads account?")) return;

    try {
        await fetchJson(`${API_ORIGIN}/api/googleads/disconnect/${currentClientId}`, { method: "DELETE" });
        loadGoogleAdsSection();
    } catch (err) {
        alert("Failed to disconnect Google Ads: " + err.message);
    }
}

function refreshGoogleAds() {
    loadGoogleAdsSection();
}