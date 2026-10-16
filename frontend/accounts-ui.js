// ============================================================================
// accounts-ui.js
// Zoho Social Style Multi-Workspace & Account Discovery Integration
// - Lets agencies switch between Brand Workspaces (e.g. ABC Interiors, Brand X)
// - Supports connecting OAuth accounts per workspace
// - Discovers Facebook Pages, Instagram Accounts, LinkedIn Profiles/Pages
// - Lets users select and bind specific discovered assets to the current workspace
// ============================================================================

// ============================================================================
// Styles for the brand setup flow. They live here (not in styles.css) so this one
// file is enough - an old or cached styles.css can no longer break the layout.
// ============================================================================
(function injectBrandFlowStyles() {
    if (document.getElementById("wzStyles")) return;
    const style = document.createElement("style");
    style.id = "wzStyles";
    style.textContent = `
/* ==========================================================================
   Zoho-style brand setup flow  (used by accounts-ui.js)
   ========================================================================== */
:root {
    --wz-blue: #2f62b5;
    --wz-blue-dark: #264f93;
    --wz-line: #e3e6ea;
    --wz-muted: #6b7280;
}

.wz-modal .modal-content {
    border: 0;
    border-radius: 6px;
    overflow: hidden;
    box-shadow: 0 18px 50px rgba(0, 0, 0, .25);
}

.wz-head {
    background: #f4f4f5;
    padding: 20px 28px 0;
    border-bottom: 1px solid var(--wz-line);
}
.wz-head:not(:has(.wz-tabs)) { padding-bottom: 18px; }
.wz-title { font-size: 1.35rem; font-weight: 500; margin: 0; color: #111; }
.wz-sub { font-size: .85rem; color: var(--wz-muted); margin-top: 2px; }

/* network tabs */
.wz-tabs { display: flex; gap: 6px; margin-top: 14px; }
.wz-tab {
    position: relative;
    border: 0;
    background: transparent;
    padding: 8px 14px 10px;
    border-bottom: 3px solid transparent;
    cursor: pointer;
    font-size: 1.25rem;
    line-height: 1;
}
.wz-tab:hover:not(.disabled) { background: rgba(0, 0, 0, .04); }
.wz-tab.active { border-bottom-color: #f59e0b; }
.wz-tab.disabled { opacity: .35; cursor: not-allowed; }
.wz-tab-icon { display: inline-flex; font-size: 1.35rem; }
.wz-tab-check {
    position: absolute; top: 2px; right: 6px;
    width: 14px; height: 14px; border-radius: 50%;
    background: #22c55e; color: #fff; font-size: .6rem;
    display: grid; place-items: center;
}

/* body */
.wz-body { padding: 34px 40px 28px; min-height: 250px; }
.wz-body-left { min-height: 0; padding: 26px 36px 30px; }
.wz-connect p { color: #222; margin-bottom: 18px; }
.wz-note { color: var(--wz-muted) !important; font-size: .88rem; margin-top: 26px !important; margin-bottom: 0 !important; }

.wz-foot {
    padding: 14px 28px;
    border-top: 1px solid var(--wz-line);
    display: flex; justify-content: flex-end;
}

/* pill buttons */
.wz-pill {
    border: 0;
    border-radius: 999px;
    padding: 9px 22px;
    background: var(--wz-blue);
    color: #fff;
    font-weight: 600;
    font-size: .92rem;
    cursor: pointer;
    transition: filter .15s, transform .05s;
}
.wz-pill:hover { filter: brightness(.92); }
.wz-pill:active { transform: translateY(1px); }
.wz-pill-lg { padding: 11px 26px; font-size: .98rem; }

/* success panel */
.wz-success { text-align: center; padding-top: 8px; }
.wz-avatar-big {
    position: relative; width: 78px; height: 78px; margin: 6px auto 10px;
    border-radius: 50%; background: #f3f4f6; border: 1px solid var(--wz-line);
    display: grid; place-items: center; font-size: 2rem;
}
.wz-avatar-big img { width: 100%; height: 100%; border-radius: 50%; object-fit: cover; }
.wz-avatar-check {
    position: absolute; right: -10px; top: -4px;
    width: 30px; height: 30px; border-radius: 50%;
    background: #22c55e; color: #fff; font-size: 1.1rem;
    display: grid; place-items: center; border: 2px solid #fff;
}
.wz-avatar-name { color: var(--wz-blue-dark); font-weight: 700; }

/* "Add a Page to this Brand" */
.aa-lead { font-weight: 700; margin-bottom: 16px; }
.aa-owner { display: flex; gap: 14px; align-items: flex-start; margin-bottom: 14px; }
.aa-owner-avatar {
    width: 44px; height: 44px; border-radius: 50%; border: 1px solid var(--wz-line);
    display: grid; place-items: center; font-size: 1.4rem; flex: 0 0 44px;
}
.aa-owner-name { color: var(--wz-blue-dark); font-weight: 700; }
.aa-owner-help { color: #333; font-size: .93rem; }
.aa-link { border: 0; background: none; padding: 0; color: var(--wz-blue-dark); text-decoration: underline; cursor: pointer; }

.aa-list { margin-left: 22px; border-left: 4px solid #d9dce1; padding-left: 20px; }
.aa-row { display: flex; align-items: center; gap: 14px; padding: 16px 0; border-bottom: 1px solid var(--wz-line); }
.aa-row:last-child { border-bottom: 0; }
.aa-avatar {
    width: 44px; height: 44px; border-radius: 50%; flex: 0 0 44px;
    border: 1px solid var(--wz-line); background: #fafafa;
    display: grid; place-items: center; font-size: 1.3rem; overflow: hidden;
}
.aa-avatar img { width: 100%; height: 100%; object-fit: cover; }
.aa-info { flex: 1; min-width: 0; }
.aa-name { color: var(--wz-blue-dark); font-weight: 700; }
.aa-meta { color: var(--wz-muted); font-size: .88rem; }
.aa-add {
    border: 1.5px solid var(--wz-blue); color: var(--wz-blue-dark); background: #fff;
    border-radius: 999px; padding: 7px 22px; font-weight: 600; cursor: pointer;
    transition: background .15s;
}
.aa-add:hover:not(:disabled) { background: #eef3fb; }
.aa-add.done { border-color: #22c55e; color: #15803d; cursor: default; }
.aa-add:disabled { opacity: .85; }

/* "Add more channels" strip under the account list */
.wz-add-strip {
    display: flex; align-items: center; justify-content: flex-end; gap: 12px;
    padding: 12px 16px; font-size: .9rem; color: #333; border-top: 1px solid var(--wz-line);
}
.wz-dot {
    width: 30px; height: 30px; border-radius: 50%; border: 1px solid var(--wz-line); background: #fff;
    display: grid; place-items: center; font-size: 1rem; cursor: pointer; padding: 0;
}
.wz-dot:hover:not(.disabled) { background: #f3f4f6; }
.wz-dot.disabled { opacity: .35; cursor: not-allowed; }

/* Publishing Progress card */
.wz-progress {
    position: fixed; right: 24px; bottom: 24px; z-index: 2000;
    width: 340px; max-width: calc(100vw - 32px);
    background: #fff; border-radius: 10px;
    box-shadow: 0 10px 34px rgba(0, 0, 0, .22);
    padding: 16px 18px;
}
.wz-progress-head { display: flex; justify-content: space-between; font-weight: 700; margin-bottom: 10px; }
.wz-progress-x { border: 0; background: none; font-size: 1.3rem; line-height: 1; color: #888; cursor: pointer; padding: 0 2px; }
.wz-progress-row { display: flex; align-items: center; gap: 10px; padding: 5px 0; font-size: .92rem; }
.wz-progress-state { width: 20px; display: inline-flex; justify-content: center; }

`;
    document.head.appendChild(style);
})();

let currentClientId = null;
let currentClientList = [];
let currentUser = null;

// Initialize workspace selector and user session on startup
// accounts-ui.js loads at the bottom of <body> AFTER bootstrap.bundle.min.js,
// so the DOM and Bootstrap are both ready when this script is parsed.
// We use a single DOMContentLoaded guard just in case, but the readyState
// check below is the real driver (fires immediately since DOM is already ready
// by the time this script tag at the bottom of <body> is evaluated).
document.addEventListener("DOMContentLoaded", async function () {
    await _initApp();
});

// If DOM is already interactive/complete (normal case for bottom-of-body script)
if (document.readyState === "complete" || document.readyState === "interactive") {
    // Small timeout to ensure Bootstrap JS has fully initialized
    setTimeout(_initApp, 0);
}

let _appInitialized = false;
async function _initApp() {
    if (_appInitialized) return;
    _appInitialized = true;
    initAuthSession();
    await loadClientWorkspaces();
    console.log("[Wiseliy] App initialized. currentUser:", currentUser, "clientId:", currentClientId);
}

function initAuthSession() {
    try {
        const stored = localStorage.getItem("wiseliy_user");
        if (stored) {
            currentUser = JSON.parse(stored);
            renderAuthHeader();
        }
        const savedClientId = localStorage.getItem("wiseliy_client_id");
        if (savedClientId) {
            currentClientId = parseInt(savedClientId, 10);
        }
    } catch (e) { console.warn("initAuthSession error:", e); }
}

function renderAuthHeader() {
    const container = document.getElementById("authHeaderContainer");
    if (!container) return;

    if (currentUser) {
        container.innerHTML = `
            <div class="dropdown">
                <button class="btn btn-sm btn-outline-light dropdown-toggle d-flex align-items-center gap-2" type="button" data-bs-toggle="dropdown">
                    <i class="bi bi-person-circle text-info"></i>
                    <span>${escapeHtml(currentUser.fullName)}</span>
                    <span class="badge bg-secondary">${escapeHtml(currentUser.role || 'Owner')}</span>
                </button>
                <ul class="dropdown-menu dropdown-menu-end dropdown-menu-dark">
                    <li><span class="dropdown-item-text small" style="color:#ccc;font-size:12px;padding:4px 16px;display:block;">${escapeHtml(currentUser.email)}</span></li>
                    <li><hr class="dropdown-divider"></li>
                    <li><a class="dropdown-item text-danger" href="javascript:void(0)" onclick="logoutUser()"><i class="bi bi-box-arrow-right"></i> Log out</a></li>
                </ul>
            </div>
        `;
    } else {
        container.innerHTML = `
            <a href="/login" class="btn btn-sm btn-outline-info">
                <i class="bi bi-box-arrow-in-right"></i> Log in
            </a>
            <a href="/signup" class="btn btn-sm btn-primary">
                Sign up for free
            </a>
        `;
    }
}

function openSignupModal() {
    window.location.href = '/signup';
}

function openLoginModal() {
    window.location.href = '/login';
}

async function submitSignup() {
    const fullName = (document.getElementById("signupFullName")?.value || "").trim();
    const email = (document.getElementById("signupEmail")?.value || "").trim();
    const password = (document.getElementById("signupPassword")?.value || "");
    const company = (document.getElementById("signupCompany")?.value || "").trim();
    const firstBrand = (document.getElementById("signupFirstBrand")?.value || "").trim();

    if (!fullName || !email || !password || !company) {
        alert("Please fill in your name, email, password, and company name.");
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/auth/signup`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                fullName,
                email,
                password,
                companyName: company,
                firstBrandName: firstBrand
            })
        });

        const data = await res.json();
        if (!res.ok) throw new Error(data.error || "Signup failed.");

        currentUser = data.user;
        localStorage.setItem("wiseliy_user", JSON.stringify(data.user));
        if (data.token) localStorage.setItem("wiseliy_token", data.token);
        renderAuthHeader();

        // Close modal
        const el = document.getElementById("signupModal");
        if (el) bootstrap.Modal.getInstance(el)?.hide();

        if (data.activeBrand) {
            currentClientId = data.activeBrand.id;
        }

        alert(`Welcome to Wiseliy Social, ${data.user.fullName}!\nYour organization "${data.organization.name}" is ready.`);
        await loadClientWorkspaces();
    } catch (err) {
        alert("Signup Error: " + err.message);
    }
}

async function submitLogin() {
    const email = (document.getElementById("loginEmail")?.value || "").trim();
    const password = (document.getElementById("loginPassword")?.value || "");

    if (!email || !password) {
        alert("Please enter both email and password.");
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/auth/login`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ email, password })
        });

        const data = await res.json();
        if (!res.ok) throw new Error(data.error || "Login failed.");

        currentUser = data.user;
        localStorage.setItem("wiseliy_user", JSON.stringify(data.user));
        if (data.token) localStorage.setItem("wiseliy_token", data.token);
        renderAuthHeader();

        // Close modal
        const el = document.getElementById("loginModal");
        if (el) bootstrap.Modal.getInstance(el)?.hide();

        if (data.brands && data.brands.length > 0) {
            currentClientId = data.brands[0].id;
        }

        alert(`Logged in successfully! Welcome back, ${data.user.fullName}.`);
        await loadClientWorkspaces();
    } catch (err) {
        alert("Login Error: " + err.message);
    }
}

function logoutUser() {
    currentUser = null;
    localStorage.removeItem("wiseliy_user");
    localStorage.removeItem("wiseliy_token");
    localStorage.removeItem("wiseliy_client_id");
    window.location.href = "login.html";
}

async function loadClientWorkspaces() {
    const select = document.getElementById("clientWorkspaceSelect");
    if (!select) return;

    try {
        const res = await fetch(`${API_BASE}/clients`);
        if (!res.ok) throw new Error("HTTP " + res.status);
        const clients = await res.json();
        currentClientList = clients;

        if (clients.length === 0) {
            select.innerHTML = `<option value="">No Workspaces (Create One)</option>`;
            return;
        }

        select.innerHTML = clients.map(c => `
            <option value="${c.id}" ${currentClientId == c.id ? "selected" : ""}>
                ${escapeHtml(c.name)} (${c.connectedAccountsCount} connected)
            </option>
        `).join("");

        if (!currentClientId && clients.length > 0) {
            currentClientId = clients[0].id;
        }

        select.value = currentClientId;
        await loadAccounts();
    } catch (err) {
        console.error("Could not load workspaces:", err);
    }
}

async function onWorkspaceChanged() {
    const select = document.getElementById("clientWorkspaceSelect");
    if (!select) return;
    currentClientId = select.value ? parseInt(select.value, 10) : null;
    try { if (currentClientId) localStorage.setItem("wiseliy_client_id", String(currentClientId)); } catch (e) { }
    await loadAccounts();
}

function openNewClientModal() {
    console.log("[Wiseliy] openNewClientModal called");
    const modalEl = document.getElementById("newClientModal");
    if (!modalEl) { console.error("[Wiseliy] newClientModal NOT found"); return; }
    const modal = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
    modal.show();
}

async function submitCreateWorkspace() {
    const nameInput = document.getElementById("newClientName");
    const descInput = document.getElementById("newClientDesc");
    const name = (nameInput?.value || "").trim();
    const desc = (descInput?.value || "").trim();

    if (!name) {
        alert("Please enter a Brand/Client Workspace name.");
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/clients`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ name: name, description: desc })
        });

        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.error || "Failed to create workspace.");
        }

        const newClient = await res.json();
        currentClientId = newClient.id;

        // Reset inputs and close modal
        if (nameInput) nameInput.value = "";
        if (descInput) descInput.value = "";
        await wzHideAndWait("newClientModal");

        await loadClientWorkspaces();

        // Zoho-style: a new Brand starts with "Get started by setting up a Brand"
        try { sessionStorage.setItem(`wiseliy_setup_shown_${currentClientId}`, "1"); } catch (e) { }
        await openBrandSetup("facebook");
    } catch (err) {
        alert("Error: " + err.message);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Brand setup flow (Zoho Social style)
//   1. "Get started by setting up a Brand"  -> network tabs + Connect button
//   2. Official login popup (OAuth)         -> handled by the backend
//   3. "Add a Page to this Brand"           -> pick the Page / account to attach
//   4. "Awesome! Brand is ready"            -> success panel + "Take me to my Brand"
// ────────────────────────────────────────────────────────────────────────────

const BRAND_NETWORKS = [
    { id: "facebook", label: "Facebook", icon: "bi-facebook", color: "#1877f2", enabled: true },
    { id: "instagram", label: "Instagram", icon: "bi-instagram", color: "#e1306c", enabled: true },
    { id: "linkedin", label: "LinkedIn", icon: "bi-linkedin", color: "#0a66c2", enabled: true },
    { id: "pinterest", label: "Pinterest", icon: "bi-pinterest", color: "#e60023", enabled: true },
    { id: "twitter", label: "X", icon: "bi-twitter-x", color: "#111111", enabled: false },
    { id: "youtube", label: "YouTube", icon: "bi-youtube", color: "#ff0000", enabled: false }
];

const NETWORK_TEXT = {
    facebook: {
        noun: "Facebook Page",
        connect: "Connect the Facebook account that manages the Business Page you want to add to this Brand.",
        pick: "Add a Page associated with this Facebook account:"
    },
    instagram: {
        noun: "Instagram account",
        connect: "Connect the Facebook account that manages the Page linked to your Instagram Business or Creator account.",
        pick: "Add an Instagram account linked to a Facebook Page of this account:"
    },
    linkedin: {
        noun: "LinkedIn profile",
        connect: "Connect your LinkedIn account to publish from your profile. Company Page publishing needs extra approval from LinkedIn.",
        pick: "Add a profile associated with this LinkedIn account:"
    },
    pinterest: {
        noun: "Pinterest Board / Account",
        connect: "Connect your Pinterest account to publish Pins to your boards.",
        pick: "Add a Board or Account associated with this Pinterest account:"
    }
};

let brandAccountsState = {};                         // platform -> { isConnected, name, avatar, status }
let brandSetupState = { tab: "facebook", connectedNow: {} };
let discoverState = { connectionId: null, clientId: null, platform: null, assets: [] };

function currentBrandName() {
    const c = currentClientList.find(x => x.id == currentClientId);
    return c ? c.name : "your Brand";
}

function wzModal(id) {
    const el = document.getElementById(id);
    return el ? bootstrap.Modal.getOrCreateInstance(el) : null;
}

// Hide a modal and wait until it is really closed (Bootstrap cannot stack modals).
function wzHideAndWait(id) {
    return new Promise(resolve => {
        const el = document.getElementById(id);
        if (!el || !el.classList.contains("show")) return resolve();
        el.addEventListener("hidden.bs.modal", () => resolve(), { once: true });
        bootstrap.Modal.getOrCreateInstance(el).hide();
    });
}

function ensureBrandModals() {
    if (document.getElementById("brandSetupModal")) return;

    document.body.insertAdjacentHTML("beforeend", `
        <!-- BRAND SETUP (Get started) -->
        <div class="modal fade" id="brandSetupModal" tabindex="-1" aria-hidden="true">
            <div class="modal-dialog modal-dialog-centered modal-lg wz-modal">
                <div class="modal-content">
                    <div class="wz-head">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <h5 class="wz-title">Get started by setting up a Brand</h5>
                                <div class="wz-sub" id="wzBrandSub"></div>
                            </div>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="wz-tabs" id="wzTabs"></div>
                    </div>
                    <div class="wz-body" id="wzBody"></div>
                    <div class="wz-foot">
                        <button type="button" class="wz-pill" id="wzDoneBtn">Close</button>
                    </div>
                </div>
            </div>
        </div>

        <!-- ADD A PAGE / ACCOUNT TO THIS BRAND -->
        <div class="modal fade" id="addAssetModal" tabindex="-1" aria-hidden="true" data-bs-backdrop="static">
            <div class="modal-dialog modal-dialog-centered modal-lg wz-modal">
                <div class="modal-content">
                    <div class="wz-head">
                        <div class="d-flex justify-content-between align-items-center">
                            <h5 class="wz-title mb-0" id="aaTitle">Add an account to this Brand</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                    </div>
                    <div class="wz-body wz-body-left">
                        <div class="aa-lead" id="aaLead"></div>
                        <div id="aaOwner"></div>
                        <div id="aaList"></div>
                    </div>
                </div>
            </div>
        </div>
    `);

    // Remember that the user closed the setup window, so it does not pop up again this session.
    document.getElementById("brandSetupModal").addEventListener("hidden.bs.modal", () => {
        brandSetupState.connectedNow = {};
    });
}

function networkById(id) { return BRAND_NETWORKS.find(n => n.id === id); }

function renderBrandSetup() {
    const subEl = document.getElementById("wzBrandSub");
    const tabsEl = document.getElementById("wzTabs");
    const bodyEl = document.getElementById("wzBody");
    const doneBtn = document.getElementById("wzDoneBtn");
    if (!subEl || !tabsEl || !bodyEl) return;

    subEl.textContent = `Brand: ${currentBrandName()}`;

    // Tabs
    tabsEl.innerHTML = BRAND_NETWORKS.map(n => {
        const connected = (brandAccountsState[n.id] && brandAccountsState[n.id].isConnected) || brandSetupState.connectedNow[n.id];
        const active = brandSetupState.tab === n.id;
        return `
            <button type="button"
                    class="wz-tab ${active ? "active" : ""} ${n.enabled ? "" : "disabled"}"
                    data-net="${n.id}"
                    ${n.enabled ? "" : "disabled"}
                    title="${n.enabled ? n.label : n.label + " – coming soon"}">
                <span class="wz-tab-icon" style="color:${n.color}"><i class="bi ${n.icon}"></i></span>
                ${connected ? `<span class="wz-tab-check"><i class="bi bi-check-lg"></i></span>` : ""}
            </button>`;
    }).join("");

    const net = networkById(brandSetupState.tab) || BRAND_NETWORKS[0];
    const text = NETWORK_TEXT[net.id];
    const justNow = brandSetupState.connectedNow[net.id];
    const existing = brandAccountsState[net.id];

    if (justNow) {
        // Success panel
        bodyEl.innerHTML = `
            <div class="wz-success">
                <p class="mb-2"><b>Awesome!</b> You added <b>${escapeHtml(justNow.name)}</b> to the Brand <b>${escapeHtml(currentBrandName())}</b>.</p>
                <p class="text-muted mb-4">You can connect more social networks for this Brand by clicking the network icons above.</p>
                <div class="wz-avatar-big">
                    ${justNow.avatarUrl
                ? `<img src="${escapeHtml(justNow.avatarUrl)}" alt="">`
                : `<i class="bi ${net.icon}" style="color:${net.color}"></i>`}
                    <span class="wz-avatar-check"><i class="bi bi-check-lg"></i></span>
                </div>
                <div class="wz-avatar-name">${escapeHtml(justNow.name)}</div>
            </div>`;
    } else if (existing && existing.isConnected) {
        bodyEl.innerHTML = `
            <div class="wz-connect">
                <p class="mb-2"><i class="bi bi-check-circle-fill text-success"></i>
                   <b>${escapeHtml(existing.name || net.label)}</b> is connected to this Brand.</p>
                <p class="text-muted">To use a different ${escapeHtml(text.noun)}, connect again and choose another one.</p>
                <button type="button" class="wz-pill wz-pill-lg" data-connect="${net.id}" style="background:${net.color}">
                    Connect ${escapeHtml(net.label)} again
                </button>
                <p class="wz-note">A Brand is a group of social channels managed together in one dashboard. Each Brand can have one channel of each network.</p>
            </div>`;
    } else {
        bodyEl.innerHTML = `
            <div class="wz-connect">
                <p>${escapeHtml(text.connect)}</p>
                <button type="button" class="wz-pill wz-pill-lg" data-connect="${net.id}" style="background:${net.color}">
                    Connect ${escapeHtml(net.label)}
                </button>
                <p class="wz-note">A Brand is a group of social channels managed together in one dashboard. Each Brand can have one channel of each network.</p>
            </div>`;
    }

    if (doneBtn) {
        doneBtn.textContent = Object.keys(brandSetupState.connectedNow).length ? "Take me to my Brand" : "Close";
    }
}

async function openBrandSetup(tab) {
    ensureBrandModals();
    await wzHideAndWait("addAssetModal");
    brandSetupState.tab = (networkById(tab) && networkById(tab).enabled) ? tab : "facebook";
    renderBrandSetup();
    wzModal("brandSetupModal").show();
}

function refreshBrandSetupTabs() {
    const el = document.getElementById("brandSetupModal");
    if (el && el.classList.contains("show")) renderBrandSetup();
}

function maybeAutoOpenBrandSetup(connectedCount) {
    if (!currentClientId || connectedCount > 0) return;
    const key = `wiseliy_setup_shown_${currentClientId}`;
    try {
        if (sessionStorage.getItem(key)) return;
        sessionStorage.setItem(key, "1");
    } catch (e) { /* storage blocked: skip auto-open */ return; }
    if (document.querySelector(".modal.show")) return;
    openBrandSetup("facebook");
}

// ────────────────────────────────────────────────────────────────────────────
// Load Accounts for Active Client Workspace
// ────────────────────────────────────────────────────────────────────────────
function resolveAccountInfo(platform, clientAcc, baseAcc) {
    const src = clientAcc || baseAcc || null;
    return {
        isConnected: src ? !!src.isConnected : false,
        accountName: src ? (src.accountName || `${platform} Account`) : `${platform} Account`,
        accId: src ? src.id : null,
        avatar: (clientAcc && clientAcc.avatarUrl) || null,
        status: (clientAcc && clientAcc.status) || (baseAcc && baseAcc.status) || null
    };
}

async function loadAccounts() {
    const container = document.getElementById("platformsContainer");
    if (!container) return;

    try {
        let clientAccounts = [];

        if (currentClientId) {
            const clientRes = await fetch(`${API_BASE}/clients/${currentClientId}`);
            if (clientRes.ok) {
                const clientData = await clientRes.json();
                clientAccounts = clientData.accounts || [];
            }
        }

        const res = await fetch(`${API_BASE}/accounts`);
        if (!res.ok) throw new Error("API returned " + res.status);
        const baseAccounts = await res.json();

        const platforms = ["facebook", "instagram", "linkedin", "pinterest"];
        brandAccountsState = {};
        let connectedCount = 0;

        const rowsHtml = platforms.map(p => {
            const clientAcc = clientAccounts.find(a => a.platform.toLowerCase() === p);
            const baseAcc = baseAccounts.find(a => a.platform.toLowerCase() === p);
            const info = resolveAccountInfo(p, clientAcc, baseAcc);

            brandAccountsState[p] = { isConnected: info.isConnected, name: info.accountName, avatar: info.avatar, status: info.status };
            if (info.isConnected) connectedCount++;

            return renderZohoAccountRow(p, clientAcc, baseAcc);
        }).join("");

        container.innerHTML = rowsHtml + renderAddChannelsStrip();

        refreshBrandSetupTabs();
        maybeAutoOpenBrandSetup(connectedCount);

    } catch (err) {
        container.innerHTML = `
            <div class="alert alert-danger mb-0 m-3">
                <i class="bi bi-exclamation-circle"></i>
                <strong>Cannot reach backend API.</strong>
                <br>Make sure the API is running at <code>${API_BASE}</code>
            </div>`;
        console.error(err);
    }
}

function renderAddChannelsStrip() {
    // Only offer networks that are NOT already connected to this Brand.
    // (Connected ones are already listed in the rows above.)
    const remaining = BRAND_NETWORKS.filter(n => !(brandAccountsState[n.id] && brandAccountsState[n.id].isConnected));
    if (remaining.length === 0) return "";

    const canConnectMore = remaining.some(n => n.enabled);

    const dots = remaining.map(n => `
        <button type="button" class="wz-dot ${n.enabled ? "" : "disabled"}"
                data-open-setup="${n.id}" ${n.enabled ? "" : "disabled"}
                title="${n.enabled ? "Connect " + n.label : n.label + " – coming soon"}"
                style="color:${n.color}">
            <i class="bi ${n.icon}"></i>
        </button>`).join("");

    return `
        <div class="wz-add-strip">
            <span>${canConnectMore ? "Add more channels" : "More channels coming soon"}</span>
            <div class="d-flex gap-2">${dots}</div>
        </div>`;
}

function renderZohoAccountRow(platform, clientAcc, baseAcc) {
    const icon = PLATFORM_ICONS[platform] || "bi-globe";
    const color = PLATFORM_COLORS[platform] || "#888";
    const info = resolveAccountInfo(platform, clientAcc, baseAcc);

    const p = escapeHtml(platform);
    const id = escapeHtml(info.accId ?? "");
    const needsReconnect = info.status === "NeedsReconnection" || info.status === "ExpiringSoon";

    const connectBtn = `
        <button type="button" class="btn btn-sm btn-outline-primary js-acc-connect" data-platform="${p}">
            <i class="bi bi-link-45deg"></i> Connect
        </button>`;

    const reconnectBtn = `
        <button type="button" class="btn btn-sm btn-warning js-acc-connect" data-platform="${p}">
            <i class="bi bi-arrow-repeat"></i> Reconnect
        </button>`;

    const disconnectBtn = `
        <button type="button" class="btn btn-sm btn-outline-danger js-acc-disconnect"
                data-id="${id}" data-platform="${p}">
            <i class="bi bi-x-circle"></i> Disconnect
        </button>`;

    let badge;
    if (!info.isConnected) {
        badge = `<span class="badge bg-danger"><i class="bi bi-x-circle"></i> Not Connected</span>`;
    } else if (info.status === "NeedsReconnection") {
        badge = `<span class="badge bg-danger"><i class="bi bi-exclamation-triangle"></i> Needs Reconnection</span>`;
    } else if (info.status === "ExpiringSoon") {
        badge = `<span class="badge bg-warning text-dark"><i class="bi bi-clock"></i> Expiring soon</span>`;
    } else {
        badge = `<span class="badge bg-success"><i class="bi bi-check-circle"></i> Connected</span>`;
    }

    // Connected accounts show Disconnect only. Reconnect appears only when it is really needed.
    const buttons = !info.isConnected ? connectBtn : (needsReconnect ? reconnectBtn + disconnectBtn : disconnectBtn);
    const canPublish = info.isConnected && info.status !== "NeedsReconnection";

    const avatarHtml = info.avatar
        ? `<img src="${escapeHtml(info.avatar)}" class="rounded-circle" style="width:32px;height:32px;object-fit:cover;" alt="">`
        : `<i class="bi ${icon} fs-4" style="color:${color}"></i>`;

    return `
        <div class="platform-row d-flex align-items-center justify-content-between p-3 border-bottom">
            <div class="d-flex align-items-center gap-3">
                <input class="form-check-input platform-checkbox mt-0"
                       type="checkbox"
                       value="${p}"
                       id="chk_${p}"
                       ${canPublish ? "" : "disabled"}>

                <label class="form-check-label d-flex align-items-center gap-2 mb-0 fw-semibold" for="chk_${p}">
                    ${avatarHtml}
                    <div>
                        <span class="text-capitalize d-block">${p}</span>
                        ${info.isConnected ? `<small class="text-muted fw-normal">${escapeHtml(info.accountName)}</small>` : ""}
                    </div>
                </label>
            </div>

            <div class="d-flex align-items-center gap-2">
                ${badge}
                ${buttons}
            </div>
        </div>`;
}

// ────────────────────────────────────────────────────────────────────────────
// Connect Platform with Client Context (opens the platform's official login)
// ────────────────────────────────────────────────────────────────────────────
function connectPlatform(platform, opts) {
    const params = [];
    if (currentClientId) params.push(`clientId=${encodeURIComponent(currentClientId)}`);
    if (opts && opts.switchAccount) params.push("switch=1");

    const oauthUrl = `${API_BASE}/accounts/connect/${encodeURIComponent(platform)}` +
        (params.length ? "?" + params.join("&") : "");

    const popup = window.open(
        oauthUrl,
        `Connect ${platform}`,
        "width=650,height=720,scrollbars=yes,resizable=yes"
    );

    const timer = setInterval(() => {
        if (popup && popup.closed) {
            clearInterval(timer);
            loadAccounts();
            loadClientWorkspaces();
        }
    }, 1000);
}

// ────────────────────────────────────────────────────────────────────────────
// "Add a Page to this Brand" - choose which Page / account to attach
// ────────────────────────────────────────────────────────────────────────────
async function openDiscoveryModal(connectionId, targetClientId) {
    ensureBrandModals();
    await wzHideAndWait("brandSetupModal");
    await wzHideAndWait("discoverAccountsModal");   // old window from index.html, if it is open

    const clientId = targetClientId || currentClientId;
    discoverState = { connectionId, clientId, platform: null, assets: [] };

    document.getElementById("aaTitle").textContent = "Add an account to this Brand";
    document.getElementById("aaLead").textContent = "";
    document.getElementById("aaOwner").innerHTML = "";
    document.getElementById("aaList").innerHTML = `
        <div class="text-center py-4">
            <div class="spinner-border text-primary"></div>
            <p class="text-muted mt-2 mb-0">Looking for the Pages and accounts you can manage...</p>
        </div>`;
    wzModal("addAssetModal").show();

    try {
        const res = await fetch(`${API_BASE}/accounts/discover/${connectionId}`);
        if (!res.ok) throw new Error("HTTP " + res.status);
        const data = await res.json();
        const assets = data.discoveredAssets || [];

        if (assets.length === 0) {
            document.getElementById("aaList").innerHTML = `
                <div class="alert alert-warning mb-0">
                    <i class="bi bi-exclamation-triangle"></i>
                    No Pages, profiles or channels were found for this login.
                    <button type="button" class="btn btn-link p-0 align-baseline" data-switch-account="${escapeHtml(data.platform || "facebook")}">Try a different account</button>
                </div>`;
            return;
        }

        const platform = assets[0].platform;
        const net = networkById(platform) || BRAND_NETWORKS[0];
        const text = NETWORK_TEXT[platform] || NETWORK_TEXT.facebook;
        discoverState.platform = platform;
        discoverState.assets = assets;

        document.getElementById("aaTitle").textContent = `Add a ${text.noun} to this Brand`;
        document.getElementById("aaLead").textContent = text.pick;

        const ownerName = data.accountOwnerName || data.providerUserName || data.userName || data.connectionName || "";
        document.getElementById("aaOwner").innerHTML = `
            <div class="aa-owner">
                <div class="aa-owner-avatar"><i class="bi ${net.icon}" style="color:${net.color}"></i></div>
                <div>
                    ${ownerName ? `<div class="aa-owner-name">${escapeHtml(ownerName)}</div>` : `<div class="aa-owner-name">${escapeHtml(net.label)} account</div>`}
                    <div class="aa-owner-help">Not seeing the ${escapeHtml(text.noun)} you are looking for? You may be logged in with an account that does not have access to it.
                        <button type="button" class="aa-link" data-switch-account="${escapeHtml(platform)}">Switch account</button>
                    </div>
                </div>
            </div>`;

        document.getElementById("aaList").innerHTML = `
            <div class="aa-list">
                ${assets.map((asset, i) => {
            const aIcon = PLATFORM_ICONS[asset.platform] || "bi-globe";
            const aColor = PLATFORM_COLORS[asset.platform] || "#888";
            const linkedHere = asset.connectedClientId == clientId;
            const meta = [
                (asset.likes != null ? `${asset.likes} Likes` : null),
                (asset.category || null)
            ].filter(Boolean).join(" · ");

            return `
                        <div class="aa-row">
                            <div class="aa-avatar">
                                ${asset.avatarUrl
                    ? `<img src="${escapeHtml(asset.avatarUrl)}" alt="">`
                    : `<i class="bi ${aIcon}" style="color:${aColor}"></i>`}
                            </div>
                            <div class="aa-info">
                                <div class="aa-name">${escapeHtml(asset.name)}</div>
                                <div class="aa-meta">${escapeHtml(meta || asset.platform)}</div>
                                ${asset.connectedClientName && !linkedHere
                    ? `<div class="aa-meta">Already added to: ${escapeHtml(asset.connectedClientName)}</div>` : ""}
                            </div>
                            <button type="button" class="aa-add ${linkedHere ? "done" : ""}" data-asset-add="${i}" ${linkedHere ? "disabled" : ""}>
                                ${linkedHere ? `<i class="bi bi-check-lg"></i> Added` : `<i class="bi bi-plus-lg"></i> Add`}
                            </button>
                        </div>`;
        }).join("")}
            </div>`;
    } catch (err) {
        document.getElementById("aaList").innerHTML = `
            <div class="alert alert-danger mb-0">
                <i class="bi bi-x-circle"></i> Could not load your accounts: ${escapeHtml(err.message)}
            </div>`;
    }
}

async function addDiscoveredAsset(index, btn) {
    const asset = discoverState.assets[index];
    if (!asset) return;

    btn.disabled = true;
    btn.innerHTML = `<span class="spinner-border spinner-border-sm"></span> Adding...`;

    try {
        const res = await fetch(`${API_BASE}/accounts/bind`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                clientId: discoverState.clientId,
                connectionId: discoverState.connectionId,
                platform: asset.platform,
                externalAccountId: asset.externalId,
                accountName: asset.name,
                avatarUrl: asset.avatarUrl || null
            })
        });

        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.error || "Could not add this account.");
        }

        brandSetupState.connectedNow[asset.platform] = { name: asset.name, avatarUrl: asset.avatarUrl || null };

        await wzHideAndWait("addAssetModal");
        await loadClientWorkspaces();            // also reloads the account rows
        await openBrandSetup(asset.platform);    // shows the success panel

    } catch (err) {
        alert("Could not add this account: " + err.message);
        btn.disabled = false;
        btn.innerHTML = `<i class="bi bi-arrow-repeat"></i> Retry`;
    }
}

// Kept so older code that calls it still works.
async function bindAssetToWorkspace(clientId, connectionId, platform, externalId, name, avatarUrl, btn) {
    discoverState = { connectionId, clientId, platform, assets: [{ platform, externalId, name, avatarUrl }] };
    await addDiscoveredAsset(0, btn || document.createElement("button"));
}

// ────────────────────────────────────────────────────────────────────────────
// Click handling
// ────────────────────────────────────────────────────────────────────────────
document.addEventListener("click", async function (event) {

    // Network tab inside the setup window
    const tab = event.target.closest(".wz-tab[data-net]");
    if (tab && !tab.disabled) {
        brandSetupState.tab = tab.dataset.net;
        renderBrandSetup();
        return;
    }

    // "Connect <Network>" button inside the setup window
    const connectNet = event.target.closest("[data-connect]");
    if (connectNet) {
        connectPlatform(connectNet.dataset.connect);
        return;
    }

    // Network icons in "Add more channels"
    const openSetup = event.target.closest("[data-open-setup]");
    if (openSetup && !openSetup.disabled) {
        openBrandSetup(openSetup.dataset.openSetup);
        return;
    }

    // "Add" next to a Page / account
    const addBtn = event.target.closest("[data-asset-add]");
    if (addBtn && !addBtn.disabled) {
        addDiscoveredAsset(parseInt(addBtn.dataset.assetAdd, 10), addBtn);
        return;
    }

    // "Switch account"
    const switchBtn = event.target.closest("[data-switch-account]");
    if (switchBtn) {
        const platform = switchBtn.dataset.switchAccount;
        await wzHideAndWait("addAssetModal");
        connectPlatform(platform, { switchAccount: true });
        return;
    }

    // Footer button of the setup window
    if (event.target.closest("#wzDoneBtn")) {
        wzModal("brandSetupModal").hide();
        return;
    }

    // Row buttons: Connect / Reconnect
    const connectBtn = event.target.closest(".js-acc-connect");
    if (connectBtn) {
        connectPlatform(connectBtn.dataset.platform);
        return;
    }

    // Row button: Disconnect
    const disconnectBtn = event.target.closest(".js-acc-disconnect");
    if (!disconnectBtn) return;

    const id = disconnectBtn.dataset.id;
    const platform = disconnectBtn.dataset.platform;

    if (!id || id === "undefined") {
        alert("Cannot disconnect: the account ID is missing from the accounts list.");
        return;
    }

    const ok = confirm(`Disconnect ${platform}?\n\nAny scheduled posts for ${platform} will fail until you connect it again.`);
    if (!ok) return;

    disconnectBtn.disabled = true;

    try {
        const res = await fetch(`${API_BASE}/accounts/${id}/disconnect`, { method: "POST" });
        if (!res.ok) throw new Error(`Disconnect failed (HTTP ${res.status}).`);
        await loadAccounts();
        await loadClientWorkspaces();
    } catch (err) {
        alert(err.message || "Disconnect failed.");
        console.error(err);
        disconnectBtn.disabled = false;
    }
});

// Message from the OAuth popup
window.addEventListener("message", function (event) {
    if (event.origin !== window.location.origin) return;

    if (event.data && event.data.type === "wiseliy-oauth") {
        if (event.data.success && event.data.connectionId) {
            openDiscoveryModal(event.data.connectionId, event.data.clientId || currentClientId);
        } else {
            loadAccounts();
            loadClientWorkspaces();
        }
    }
});

// ────────────────────────────────────────────────────────────────────────────
// Publishing Progress card (bottom-right), shown while a post is being sent
// ────────────────────────────────────────────────────────────────────────────
let ppState = null;
let ppHideTimer = null;

function ppEnsure() {
    if (document.getElementById("wzProgress")) return;
    document.body.insertAdjacentHTML("beforeend", `
        <div id="wzProgress" class="wz-progress d-none" role="status" aria-live="polite">
            <div class="wz-progress-head">
                <span>Publishing Progress</span>
                <span class="d-flex align-items-center gap-2">
                    <span id="wzProgressCount">0/0</span>
                    <button type="button" class="wz-progress-x" id="wzProgressClose" aria-label="Close">&times;</button>
                </span>
            </div>
            <div id="wzProgressRows"></div>
        </div>`);
    document.getElementById("wzProgressClose").addEventListener("click", ppHide);
}

function ppHide() {
    const el = document.getElementById("wzProgress");
    if (el) el.classList.add("d-none");
    ppState = null;
    clearTimeout(ppHideTimer);
}

function ppStart(platforms) {
    ppEnsure();
    clearTimeout(ppHideTimer);
    ppState = {};
    platforms.forEach(p => { ppState[p] = { status: "InProgress" }; });
    ppRender();
}

function ppApply(results) {
    if (!ppState) ppStart((results || []).map(r => r.platform));
    (results || []).forEach(r => { ppState[r.platform] = { status: r.status, error: r.error }; });
    ppRender();
}

function ppFinish() {
    if (!ppState) return;
    Object.keys(ppState).forEach(p => {
        if (ppState[p].status === "InProgress") ppState[p] = { status: "Failed", error: "The post did not finish." };
    });
    ppRender();
}

function ppRender() {
    const el = document.getElementById("wzProgress");
    if (!el || !ppState) return;
    el.classList.remove("d-none");

    const platforms = Object.keys(ppState);
    const done = platforms.filter(p => ppState[p].status !== "InProgress").length;
    document.getElementById("wzProgressCount").textContent = `${done}/${platforms.length}`;

    document.getElementById("wzProgressRows").innerHTML = platforms.map(p => {
        const s = ppState[p];
        const icon = PLATFORM_ICONS[p] || "bi-globe";
        const color = PLATFORM_COLORS[p] || "#888";
        const name = p.charAt(0).toUpperCase() + p.slice(1);

        let state, text;
        if (s.status === "InProgress") {
            state = `<span class="spinner-border spinner-border-sm text-secondary"></span>`;
            text = "Post is in progress.";
        } else if (s.status === "Published") {
            state = `<i class="bi bi-check-circle-fill text-success"></i>`;
            text = "Post published.";
        } else if (s.status === "Scheduled") {
            state = `<i class="bi bi-clock-fill text-primary"></i>`;
            text = "Post scheduled.";
        } else {
            state = `<i class="bi bi-x-circle-fill text-danger"></i>`;
            text = "Failed: " + String(s.error || "unknown error").slice(0, 140);
        }

        return `
            <div class="wz-progress-row">
                <span class="wz-progress-state">${state}</span>
                <i class="bi ${icon}" style="color:${color}"></i>
                <span>${escapeHtml(name)}: ${escapeHtml(text)}</span>
            </div>`;
    }).join("");

    // Close by itself a few seconds after everything finished without errors.
    clearTimeout(ppHideTimer);
    const allDone = done === platforms.length;
    const anyFailed = platforms.some(p => ppState[p].status === "Failed");
    if (allDone && !anyFailed) ppHideTimer = setTimeout(ppHide, 8000);
}

// Wrap the existing publish functions (their own logic is not changed).
(function installPublishProgress() {
    if (typeof window.publishPost === "function") {
        const origPublish = window.publishPost;

        window.publishPost = async function () {
            const selected = Array.from(document.querySelectorAll(".platform-checkbox:checked")).map(cb => cb.value);
            const btn = document.getElementById("publishBtn");
            const wasDisabled = btn ? btn.disabled : false;

            const running = origPublish.apply(this, arguments);

            // The original disables the button synchronously once validation passed.
            const started = !!btn && !wasDisabled && btn.disabled && selected.length > 0;
            if (started) ppStart(selected);

            try { return await running; }
            finally { if (started) ppFinish(); }
        };
    }

    if (typeof window.renderResults === "function") {
        const origRender = window.renderResults;

        window.renderResults = function (postId, overallStatus, results) {
            const out = origRender.apply(this, arguments);
            try { ppApply(results); } catch (e) { console.warn("progress card:", e); }
            return out;
        };
    }
})();