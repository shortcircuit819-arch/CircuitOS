const viewTitles = {
  overview: "System Overview",
  branding: "Game Profile",
  messages: "Message Templates",
  twitch: "Twitch Settings",
  setup: "Streamer.bot Setup",
  economy: "Scrap Economy",
  viewers: "Viewer Inventory",
  roles: "Discord Role Awards",
  ratelab: "Rate Lab",
  patchnotes: "Patch Notes",
  backups: "Backup & Recovery",
  collections: "Main Collections",
  events: "Event Collections",
  boost: "Featured Boost",
  overlay: "Overlay Editor",
  appearance: "Appearance",
  profiles: "Profiles",
  settings: "Settings"
};

const platformName = "CircuitOS";

const defaultSystemProfile = {
  schemaVersion: 1,
  gameName: "Circuit Components",
  adminName: "CircuitOS Control Core",
  brandKicker: "CIRCUITOS",
  itemSingular: "component",
  itemPlural: "components",
  collectionSingular: "collection",
  collectionPlural: "collections",
  currencyName: "Scrap",
  redemptionName: "Circuit Component",
  redeemCooldownSeconds: 120,
  redeemDupProtectionTurns: 0,
  redemptionCost: 100,
  commands: {
    inventory: "components",
    missing: "missing",
    duplicates: "dupes",
    leaderboard: "leaderboard",
    balance: "scrap",
    collection: "collection",
    salvage: "salvage"
  },
  messages: {
    redeemSuccess: "⚡ Scan complete: @{viewer} found {item} [{collection}]. Progress: {owned}/{total}.{duplicateText}",
    rarePull: "{rareLabel}: @{viewer} pulled {item}! Current odds: about 1 in {odds}.",
    triplePull: "TRIPLE MATCH: @{viewer} pulled {item} three times in a row! Sequence odds: about 1 in {odds}.",
    collectionComplete: "✅ COLLECTION COMPLETE: @{viewer} completed {collection}!",
    noInventory: "@{viewer} you don't have any {itemPlural} yet. Redeem {redemption} to start your {collectionSingular}.",
    balance: "@{viewer} {currency} balance: {balance}.",
    noDuplicates: "@{viewer} you don't have any duplicate {itemPlural} yet.",
    collectionUsage: "@{viewer} usage: !{collectionCommand} <{collectionSingular}>",
    collectionSummary: "@{viewer} {collection}: {owned}/{total} | {status}{availability}",
    salvageUsage: "@{viewer} usage: !{salvageCommand} <{collectionSingular}> or !{salvageCommand} all",
    nothingToSalvage: "@{viewer} you have no extra copies to salvage in {selection}.",
    salvageSuccess: "@{viewer} salvaged {count} extra {itemWord} for {earned} {currency}. Balance: {balance}.",
    variantPull: ""
  },
  colors: {
    background: "#000d19",
    panel: "#061a2b",
    panelAlt: "#092239",
    line: "#193a55",
    accent: "#ff1a24",
    text: "#eef5fb",
    muted: "#8295a8"
  }
};

const messageDefinitions = {
  redeemSuccess: { label: "Redemption success", description: "Sent after every successful pull.", placeholders: ["viewer", "item", "collection", "owned", "total", "duplicateText"] },
  rarePull: { label: "Rare pull", description: "Sent when a collection has a rare label.", placeholders: ["rareLabel", "viewer", "item", "odds"] },
  triplePull: { label: "Triple match", description: "Sent after the same item is pulled three times consecutively.", placeholders: ["viewer", "item", "odds"] },
  collectionComplete: { label: "Collection completed", description: "Sent once when a viewer first completes a collection.", placeholders: ["viewer", "collection"] },
  variantPull: { label: "Variant pull (optional)", description: "Sent when one or more variants fire on a pull. Leave blank to disable. Here {item} is the BASE name (no variant prefix), so {variantLabels} {item} reads as 'SHINY Capacitor' — using both will not double the label.", placeholders: ["variantLabels", "viewer", "item", "collection"], optional: true },
  noInventory: { label: "No inventory", description: "Sent when a viewer has not collected anything yet.", placeholders: ["viewer", "itemPlural", "redemption", "collectionSingular"] },
  balance: { label: "Currency balance", description: "Response for the configured balance command.", placeholders: ["viewer", "currency", "balance"] },
  noDuplicates: { label: "No duplicates", description: "Sent when the duplicate command finds no extra copies.", placeholders: ["viewer", "itemPlural"] },
  collectionUsage: { label: "Collection command usage", description: "Usage guidance when no collection is supplied.", placeholders: ["viewer", "collectionCommand", "collectionSingular"] },
  collectionSummary: { label: "Collection summary", description: "The first collection-detail response line.", placeholders: ["viewer", "collection", "owned", "total", "status", "availability"] },
  salvageUsage: { label: "Salvage command usage", description: "Usage guidance when no salvage target is supplied.", placeholders: ["viewer", "salvageCommand", "collectionSingular"] },
  nothingToSalvage: { label: "Nothing to salvage", description: "Sent when the selected collection has no extra copies.", placeholders: ["viewer", "selection"] },
  salvageSuccess: { label: "Salvage success", description: "Sent after duplicates are converted into currency.", placeholders: ["viewer", "count", "itemWord", "earned", "currency", "balance"] }
};

const placeholderDescriptions = {
  viewer: "The viewer's current display name.",
  item: "The item that was pulled.",
  collection: "The collection display name.",
  owned: "Unique items the viewer owns in this collection.",
  total: "Total items available in this collection.",
  duplicateText: "Extra duplicate and featured-boost details when applicable.",
  rareLabel: "The configured rare-pull label for the collection.",
  odds: "Approximate one-in-N odds for this result.",
  itemPlural: "The configured plural item term, such as components or cards.",
  redemption: "The configured channel-point redemption name.",
  collectionSingular: "The configured singular collection term.",
  currency: "The configured currency name.",
  balance: "The viewer's current currency balance.",
  collectionCommand: "The configured collection-detail chat command.",
  status: "Completion status for the requested collection.",
  availability: "Event availability details when relevant.",
  variantLabels: "Space-separated list of variant labels that fired (e.g., SHINY LARGE).",
  salvageCommand: "The configured duplicate-salvage chat command.",
  selection: "The collection or all-collections salvage target.",
  count: "Number of duplicate items consumed.",
  itemWord: "The singular or plural item word for the result.",
  earned: "Currency earned by the salvage operation."
};

let collections = [];
let boost = { enabled: false, displayName: "Featured Boost", collectionMultipliers: {} };
let analytics = { summary: {}, collections: [], viewers: [] };
let roleAwards = { roleNames: {}, awards: [], summary: { pending: 0, assigned: 0, total: 0 } };
let selectedViewerId = "";
let lastSimulation = null;
let baselineModel = null;
let backupCenter = { liveFiles: [], backups: [], backupPath: "" };
let selectedBackupFile = "";
let selectedBackupPreview = null;
let systemProfile = clone(defaultSystemProfile);
let profileConfigured = false;
let profileDirty = false;
let setupBundle = null;
let dataPath = "";
let overlayFilePath = "";
let runtimeInfo = { runtime: "unknown", version: "unknown" };
let dirty = false;
let operationalRefreshInFlight = false;
let firstRunStarterConfiguration = null;
let wizardStep = 1;
let wizardPreset = "blank";
let wizardInitialized = false;
let collectionImportPreview = null;
let eventImportPreview = null;
let overlayConfig = null;
let overlayDirty = false;
let activeOverlayPreviewState = "normal";
let profilesData = { activeProfileId: "", profiles: [] };
let pendingSwitchId = "";
const expandedCollectionKeys = new Set();

const notice = document.getElementById("notice");
const saveButton = document.getElementById("saveButton");

function clone(value) {
  return JSON.parse(JSON.stringify(value));
}

function showNotice(message, type = "info") {
  notice.hidden = false;
  notice.className = `notice ${type}`;
  notice.textContent = message;
}

function clearNotice() {
  notice.hidden = true;
  notice.textContent = "";
}

function markDirty() {
  dirty = true;
  saveButton.textContent = "Save *";
}

function markClean() {
  dirty = false;
  saveButton.textContent = "Save";
}

function normalizeProfile(value) {
  const incoming = value || {};
  return {
    ...clone(defaultSystemProfile),
    ...clone(incoming),
    schemaVersion: 1,
    colors: { ...clone(defaultSystemProfile.colors), ...clone(incoming.colors || {}) },
    commands: { ...clone(defaultSystemProfile.commands), ...clone(incoming.commands || {}) },
    messages: { ...clone(defaultSystemProfile.messages), ...clone(incoming.messages || {}) }
  };
}

function hexToRgba(hex, alpha) {
  const match = /^#([0-9a-f]{6})$/i.exec(hex || "");
  if (!match) return `rgba(255, 26, 36, ${alpha})`;
  const value = Number.parseInt(match[1], 16);
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`;
}

let lastSession = { mode: "local", twitch: null, dataPath: "" };
let twitchRewardCatalog = { loaded: false, loading: false, error: "", items: [] };

function renderSessionMode(mode, twitch, location) {
  lastSession = { mode, twitch, dataPath: location || "" };
  const element = document.getElementById("sessionMode");
  if (!element) return;
  const name = twitch ? twitch.displayName || twitch.login : null;
  const cloud = mode === "cloud";
  if (cloud && name) element.textContent = `☁ @${name}`;
  else if (cloud) element.textContent = "☁ Cloud";
  else if (name) element.textContent = `@${name}`;
  else element.textContent = "Local data";
  element.classList.toggle("cloud", cloud);
  element.title = "Click for session details";
  element.onclick = toggleSessionPanel;
  const panel = document.getElementById("sessionPanel");
  if (panel && !panel.hidden) renderSessionPanel();
  renderTwitchSettings();
}

function toggleSessionPanel() {
  const panel = document.getElementById("sessionPanel");
  if (!panel) return;
  if (panel.hidden) { renderSessionPanel(); panel.hidden = false; }
  else panel.hidden = true;
}

function renderSessionPanel() {
  const panel = document.getElementById("sessionPanel");
  if (!panel) return;
  const esc = value => String(value).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
  const { mode, twitch, dataPath } = lastSession;
  const cloud = mode === "cloud";
  const expired = twitch?.expiresAt ? new Date(twitch.expiresAt).getTime() <= Date.now() : false;
  panel.innerHTML = `
    <div class="session-panel-title">Twitch</div>
    <div class="session-summary ${twitch ? "connected" : ""}">
      <strong>${twitch ? `@${esc(twitch.login || twitch.displayName)}` : "Not signed in"}</strong>
      <span>${twitch ? (expired ? "Refresh your login when you are ready to go live." : "Connected on this PC.") : "Connect Twitch when you want native rewards."}</span>
    </div>
    <div class="session-actions">
      ${twitch
        ? `<button type="button" class="button danger small" id="twitchLogoutButton">Log out</button>`
        : `<button type="button" class="button twitch-button primary small" id="twitchLoginButton">Log in with Twitch</button>`}
    </div>
    <details class="session-advanced">
      <summary>Advanced details</summary>
      <div class="session-row"><span>Mode</span><strong>${cloud ? "Cloud" : "Local"}</strong></div>
      ${dataPath ? `<div class="session-row"><span>Data folder</span><strong>${esc(dataPath)}</strong></div>` : ""}
    </details>
  `;
  const logoutButton = document.getElementById("twitchLogoutButton");
  if (logoutButton) logoutButton.addEventListener("click", logoutTwitch);
  const loginButton = document.getElementById("twitchLoginButton");
  if (loginButton) loginButton.addEventListener("click", loginTwitch);
}

async function loginTwitch() {
  const button = document.getElementById("twitchLoginButton");
  const setButton = text => { if (button) { button.disabled = true; button.textContent = text; } };
  setButton("Connecting…");
  try {
    const startResp = await fetch("/api/twitch/login/start", { method: "POST" });
    const start = await startResp.json().catch(() => ({}));
    if (!startResp.ok || !start.ok) throw new Error((start.errors || ["Could not start Twitch login."]).join(" "));

    // Self-host (own app + secret): fall back to the legacy blocking browser flow.
    if (!start.inline) return await loginTwitchBlocking();

    // Zero-config device flow: the host opened the pre-filled activate page; show the code and poll.
    setButton(`Code: ${start.userCode}`);
    showNotice(`Authorize in the browser tab that just opened. If asked, the code is ${start.userCode}.`, "success");
    await onTwitchLoginComplete(await pollTwitchLogin(start));
  } catch (error) {
    showNotice(error.message, "error");
    renderSessionPanel();
  }
}

// Polls /api/twitch/login/poll until the device login completes; returns the success payload or throws.
async function pollTwitchLogin(start) {
  const intervalMs = Math.max(2, Number(start.interval) || 5) * 1000;
  const deadline = Date.now() + (Number(start.expiresIn) || 1800) * 1000;
  while (Date.now() < deadline) {
    await new Promise(resolve => setTimeout(resolve, intervalMs));
    const resp = await fetch("/api/twitch/login/poll", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ loginId: start.loginId })
    });
    const poll = await resp.json().catch(() => ({}));
    if (poll.status === "done") return poll;
    if (poll.status === "expired") throw new Error("The login code expired — please try again.");
    if (poll.status === "error" || poll.ok === false) throw new Error((poll.errors || ["Twitch login failed."]).join(" "));
    // status === "pending" → keep waiting
  }
  throw new Error("Login timed out — please try again.");
}

async function onTwitchLoginComplete(result) {
  const health = await (await fetch("/api/health", { cache: "no-store" })).json();
  renderSessionMode(health.mode || lastSession.mode, health.twitch || null, lastSession.dataPath);
  twitchRewardCatalog = { loaded: false, loading: false, error: "", items: [] };
  renderSessionPanel();
  renderTwitchSettings();
  showNotice(`Logged in to Twitch as ${result.displayName}.`, "success");
}

// Legacy blocking flow (self-host with a client secret): the host opens the browser and the request
// returns once authorized. Kept as a fallback so login still works if the inline path is unavailable.
async function loginTwitchBlocking() {
  showNotice("Opening Twitch in your browser — approve the login there, then come back.", "success");
  const response = await fetch("/api/twitch/login", { method: "POST" });
  const result = await response.json().catch(() => ({}));
  if (!response.ok || !result.ok) throw new Error(result.error || "Twitch login failed.");
  await onTwitchLoginComplete(result);
}

async function logoutTwitch() {
  if (!window.confirm("Log out of Twitch? This clears your cached tokens on this PC. You'll need to log in again for cloud mode keyed to your account.")) return;
  try {
    const response = await fetch("/api/twitch/logout", { method: "POST" });
    if (!response.ok) throw new Error("Logout failed.");
    renderSessionMode(lastSession.mode, null, lastSession.dataPath);
    twitchRewardCatalog = { loaded: false, loading: false, error: "", items: [] };
    renderSessionPanel();
    renderTwitchSettings();
    showNotice("Logged out of Twitch — cached tokens cleared from this PC.", "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function applySystemProfile() {
  const root = document.documentElement;
  const colors = systemProfile.colors;
  root.style.setProperty("--bg", colors.background);
  root.style.setProperty("--panel", colors.panel);
  root.style.setProperty("--panel-2", colors.panelAlt);
  root.style.setProperty("--line", colors.line);
  root.style.setProperty("--red", colors.accent);
  root.style.setProperty("--accent", colors.accent);
  root.style.setProperty("--red-soft", hexToRgba(colors.accent, 0.13));
  root.style.setProperty("--red-border", hexToRgba(colors.accent, 0.28));
  root.style.setProperty("--sidebar-bg", hexToRgba(colors.background, 0.93));
  root.style.setProperty("--sidebar-card", hexToRgba(colors.panel, 0.52));
  root.style.setProperty("--sidebar-card-hover", hexToRgba(colors.panelAlt, 0.72));
  root.style.setProperty("--chrome-bg", hexToRgba(colors.background, 0.96));
  root.style.setProperty("--text", colors.text);
  root.style.setProperty("--muted", colors.muted);
  document.title = `${platformName} | ${systemProfile.gameName}`;
  document.getElementById("activeProfileGame").textContent = systemProfile.gameName;
  document.getElementById("activeProfileAdmin").textContent = systemProfile.adminName;
  document.querySelector('[data-view="economy"]').textContent = `${systemProfile.currencyName} Economy`;
  document.querySelector('[data-view="collections"]').textContent = `Main ${titleCase(systemProfile.collectionPlural)}`;
  document.querySelector('[data-view="events"]').textContent = `Event ${titleCase(systemProfile.collectionPlural)}`;
  const collectionsGroupSummary = document.querySelector("#collectionsNav > summary");
  if (collectionsGroupSummary) collectionsGroupSummary.textContent = titleCase(systemProfile.collectionPlural);
  const cooldownInput = document.getElementById("profileCooldown");
  if (cooldownInput) cooldownInput.value = systemProfile.redeemCooldownSeconds ?? 120;
  const dupProtInput = document.getElementById("profileDupProtection");
  if (dupProtInput) dupProtInput.value = systemProfile.redeemDupProtectionTurns ?? 0;
  const redemptionCostInput = document.getElementById("profileRedemptionCost");
  if (redemptionCostInput) redemptionCostInput.value = systemProfile.redemptionCost ?? 100;
  document.getElementById("overviewEconomyTitle").textContent = `${systemProfile.currencyName} & Duplicates`;
  document.getElementById("collectionEconomyTitle").textContent = `Unclaimed ${systemProfile.currencyName} by ${titleCase(systemProfile.collectionSingular)}`;
  document.getElementById("currencyLeaderTitle").textContent = `Top ${systemProfile.currencyName} Balances`;
  document.getElementById("collectionHealthTitle").textContent = `${titleCase(systemProfile.collectionSingular)} Health`;
  document.getElementById("editCollectionsButton").textContent = `Edit ${titleCase(systemProfile.collectionPlural)}`;
  document.getElementById("collectionsHelp").textContent = `Edit permanent ${systemProfile.collectionSingular} rates, ${systemProfile.currencyName} values, labels, and ${systemProfile.itemPlural}.`;
  document.getElementById("eventsHelp").textContent = `Event ${systemProfile.collectionPlural} add their items to pulls only while enabled and within their scheduled window. Times are in UTC — set them a little wide if you're unsure of your offset.`;
  document.getElementById("addCollectionButton").textContent = `Add ${titleCase(systemProfile.collectionSingular)}`;
  document.getElementById("importCollectionButton").textContent = `Import ${titleCase(systemProfile.itemPlural)}`;
  document.getElementById("importEventButton").textContent = `Import ${titleCase(systemProfile.itemPlural)}`;
  document.querySelector('#collectionImportValueField > span').textContent = `${systemProfile.currencyName} value`;
  document.getElementById("eventImportValueLabel").textContent = `${systemProfile.currencyName} value`;
  const patchTitle = document.getElementById("patchTitle");
  if (!patchTitle.dataset.userEdited) patchTitle.value = `${systemProfile.gameName} Update`;
  const activeView = document.querySelector(".view.active")?.id?.replace(/View$/, "") || "overview";
  document.getElementById("viewTitle").textContent = getViewTitle(activeView);
}

function titleCase(value) {
  return String(value || "").replace(/\b\w/g, character => character.toUpperCase());
}

function getViewTitle(view) {
  if (view === "economy") return `${systemProfile.currencyName} Economy`;
  if (view === "collections") return `Main ${titleCase(systemProfile.collectionPlural)}`;
  if (view === "events") return `Event ${titleCase(systemProfile.collectionPlural)}`;
  return viewTitles[view] || systemProfile.adminName;
}

function renderProfile() {
  const fields = {
    profileGameName: "gameName",
    profileAdminName: "adminName",
    profileRedemptionName: "redemptionName",
    profileItemSingular: "itemSingular",
    profileItemPlural: "itemPlural",
    profileCollectionSingular: "collectionSingular",
    profileCollectionPlural: "collectionPlural",
    profileCurrencyName: "currencyName"
  };
  for (const [id, key] of Object.entries(fields)) document.getElementById(id).value = systemProfile[key];
  const colorNames = {
    background: "Background",
    panel: "Panel",
    panelAlt: "Raised panel",
    line: "Borders",
    accent: "Accent",
    text: "Text",
    muted: "Muted text"
  };
  const grid = document.getElementById("profileColors");
  grid.replaceChildren();
  for (const [key, labelText] of Object.entries(colorNames)) {
    const label = element("label", "color-field");
    const input = document.createElement("input");
    input.type = "color";
    input.value = systemProfile.colors[key];
    input.dataset.profileColor = key;
    input.addEventListener("input", () => {
      systemProfile.colors[key] = input.value;
      profileDirty = true;
      setupBundle = null;
      applySystemProfile();
      renderProfilePreview();
      updateProfileStatus();
      renderStreamerBotSetup();
    });
    label.append(input, element("span", "", labelText));
    grid.append(label);
  }
  const commandNames = {
    inventory: "Inventory",
    missing: "Missing",
    duplicates: "Duplicates",
    leaderboard: "Leaderboard",
    balance: "Balance",
    collection: "Collection detail",
    salvage: "Salvage"
  };
  const commandGrid = document.getElementById("profileCommands");
  commandGrid.replaceChildren();
  for (const [key, labelText] of Object.entries(commandNames)) {
    const row = element("label", "command-row");
    row.append(element("span", "command-row-label", labelText));
    const wrapper = element("div", "command-input");
    wrapper.append(element("strong", "", "!"));
    const input = document.createElement("input");
    input.type = "text";
    input.maxLength = 31;
    input.value = systemProfile.commands[key];
    input.dataset.profileCommand = key;
    input.addEventListener("input", () => {
      input.value = input.value.toLowerCase().replace(/[^a-z0-9_-]/g, "");
      systemProfile.commands[key] = input.value;
      profileDirty = true;
      setupBundle = null;
      updateProfileStatus();
      renderStreamerBotSetup();
    });
    wrapper.append(input);
    row.append(wrapper);
    commandGrid.append(row);
  }
  renderProfilePreview();
  updateProfileStatus();
}

function renderProfilePreview() {
  document.getElementById("previewAdminName").textContent = systemProfile.adminName;
  document.getElementById("previewGameName").textContent = systemProfile.gameName;
  document.getElementById("previewRedemptionName").textContent = systemProfile.redemptionName;
  document.getElementById("previewTerminology").textContent = `${titleCase(systemProfile.collectionPlural)} contain ${systemProfile.itemPlural}. Duplicates convert into ${systemProfile.currencyName}.`;
  document.getElementById("previewMessage").textContent = `@Viewer found a new ${systemProfile.itemSingular}! ${systemProfile.currencyName} balance: 25.`;
}

function updateProfileStatus() {
  const status = document.getElementById("profileStatus");
  status.textContent = profileDirty ? "UNSAVED" : profileConfigured ? "CONFIGURED" : "USING DEFAULTS";
  status.className = `metric-chip ${profileDirty ? "warning" : profileConfigured ? "valid" : ""}`.trim();
  document.getElementById("saveProfileButton").textContent = profileDirty ? "Save Profile *" : "Save Profile";
  const appearanceStatus = document.getElementById("appearanceStatus");
  if (appearanceStatus) { appearanceStatus.textContent = status.textContent; appearanceStatus.className = status.className; }
  const saveAppearance = document.getElementById("saveAppearanceButton");
  if (saveAppearance) saveAppearance.textContent = profileDirty ? "Save Appearance *" : "Save Appearance";
}

function updateProfileFromInputs() {
  const fields = {
    profileGameName: "gameName",
    profileAdminName: "adminName",
    profileRedemptionName: "redemptionName",
    profileItemSingular: "itemSingular",
    profileItemPlural: "itemPlural",
    profileCollectionSingular: "collectionSingular",
    profileCollectionPlural: "collectionPlural",
    profileCurrencyName: "currencyName"
  };
  for (const [id, key] of Object.entries(fields)) systemProfile[key] = document.getElementById(id).value;
  systemProfile.redeemCooldownSeconds = Math.max(0, Math.min(3600, Number(document.getElementById("profileCooldown").value) || 0));
  systemProfile.redeemDupProtectionTurns = Math.max(0, Math.min(20, Number(document.getElementById("profileDupProtection").value) || 0));
  systemProfile.redemptionCost = Math.max(1, Math.min(1000000, Number(document.getElementById("profileRedemptionCost").value) || 100));
  profileDirty = true;
  setupBundle = null;
  applySystemProfile();
  renderProfilePreview();
  updateProfileStatus();
  renderStreamerBotSetup();
}

function validateProfileClient() {
  const errors = [];
  for (const key of ["gameName", "adminName", "redemptionName", "itemSingular", "itemPlural", "collectionSingular", "collectionPlural", "currencyName"]) {
    if (!String(systemProfile[key] || "").trim()) errors.push(`${key} cannot be empty.`);
  }
  for (const [key, value] of Object.entries(systemProfile.colors || {})) {
    if (!/^#[0-9a-f]{6}$/i.test(value)) errors.push(`${key} needs a valid hex color.`);
  }
  const commands = Object.values(systemProfile.commands || {});
  for (const command of commands) {
    if (!/^[a-z0-9][a-z0-9_-]{0,30}$/.test(command)) errors.push("Every chat command needs 1 to 31 valid characters.");
  }
  if (new Set(commands).size !== commands.length) errors.push("Chat commands must be unique.");
  for (const [key, value] of Object.entries(systemProfile.messages || {})) {
    errors.push(...validateMessageTemplate(key, value));
  }
  return errors;
}

async function saveSystemProfile() {
  const errors = validateProfileClient();
  if (errors.length) {
    showNotice(errors.join(" "), "error");
    return;
  }
  try {
    const response = await fetch("/api/profile", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(systemProfile)
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Profile save failed."]).join(" "));
    profileConfigured = true;
    profileDirty = false;
    renderAll();
    updateProfileStatus();
    await Promise.all([refreshBackupIndex(false), generateStreamerBotSetup()]);
    if (dirty) {
      const backupCount = await _saveCatalogData();
      showNotice(`Profile and catalog saved. ${backupCount} backup files created.`, "success");
    } else {
      showNotice("Game profile saved.", "success");
    }
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function resetSystemProfile() {
  if (!window.confirm("Reset the game profile to the built-in Circuit Components defaults? Nothing is saved until you choose Save.")) return;
  systemProfile = clone(defaultSystemProfile);
  profileDirty = true;
  setupBundle = null;
  applySystemProfile();
  renderProfile();
}

function validateMessageTemplate(key, template) {
  const definition = messageDefinitions[key];
  const errors = [];
  const value = String(template || "");
  if (!value.trim()) {
    // Optional messages (e.g. the variant-pull line) are allowed to be blank.
    if (!definition.optional) errors.push("Message cannot be empty.");
    return [...new Set(errors)];
  }
  if (value.length > 450) errors.push("Message cannot exceed 450 characters.");
  for (const match of value.matchAll(/\{([a-zA-Z][a-zA-Z0-9]*)\}/g)) {
    if (!definition.placeholders.includes(match[1])) errors.push(`Unsupported placeholder ${match[0]}.`);
  }
  const withoutTokens = value.replace(/\{[a-zA-Z][a-zA-Z0-9]*\}/g, "");
  if (withoutTokens.includes("{") || withoutTokens.includes("}")) errors.push("Message contains an invalid placeholder brace.");
  return [...new Set(errors)];
}

function sampleMessage(template) {
  const samples = {
    viewer: "Viewer",
    item: titleCase(systemProfile.itemSingular),
    collection: `Basic ${titleCase(systemProfile.collectionSingular)}`,
    owned: "4",
    total: "5",
    duplicateText: " Duplicate copy x2.",
    rareLabel: "RARE PULL",
    variantLabels: "SHINY",
    odds: "250",
    itemPlural: systemProfile.itemPlural,
    redemption: systemProfile.redemptionName,
    collectionSingular: systemProfile.collectionSingular,
    currency: systemProfile.currencyName,
    balance: "25",
    collectionCommand: systemProfile.commands.collection,
    status: "IN PROGRESS",
    availability: " | ACTIVE",
    salvageCommand: systemProfile.commands.salvage,
    selection: `Basic ${titleCase(systemProfile.collectionSingular)}`,
    count: "3",
    itemWord: systemProfile.itemPlural,
    earned: "6"
  };
  return String(template || "").replace(/\{([a-zA-Z][a-zA-Z0-9]*)\}/g, (token, key) => samples[key] ?? token);
}

function renderMessages() {
  const list = document.getElementById("messageList");
  list.replaceChildren();
  for (const [key, definition] of Object.entries(messageDefinitions)) {
    const card = element("article", "message-card");
    const header = element("div", "message-card-header");
    const copy = element("div");
    copy.append(element("h3", "", definition.label), element("p", "", definition.description));
    const headerActions = element("div", "message-card-actions");
    const help = element("button", "button secondary message-help-button", "?");
    help.type = "button";
    help.title = `Help for ${definition.label}`;
    help.setAttribute("aria-label", `Show help for ${definition.label}`);
    help.setAttribute("aria-expanded", "false");
    const reset = element("button", "button secondary small", "Reset");
    reset.addEventListener("click", () => resetMessageTemplate(key));
    headerActions.append(help, reset);
    header.append(copy, headerActions);
    const helpText = element("div", "message-help-text", "Write normal chat text, then insert the supported placeholders below wherever live values should appear. Placeholder braces must remain unchanged.");
    helpText.hidden = true;
    help.addEventListener("click", () => {
      helpText.hidden = !helpText.hidden;
      help.setAttribute("aria-expanded", String(!helpText.hidden));
    });
    const textarea = document.createElement("textarea");
    textarea.value = systemProfile.messages[key];
    textarea.maxLength = 450;
    textarea.dataset.messageKey = key;
    textarea.setAttribute("aria-label", `${definition.label} template`);
    const editorMeta = element("div", "message-editor-meta");
    editorMeta.append(element("span", "", "Click a placeholder to insert it at the cursor."));
    const characterCount = element("span", "message-character-count");
    const updateCharacterCount = () => {
      characterCount.textContent = `${textarea.value.length} / 450`;
      characterCount.classList.toggle("warning", textarea.value.length >= 400);
    };
    updateCharacterCount();
    editorMeta.append(characterCount);
    const tokens = element("div", "message-placeholders");
    for (const placeholder of definition.placeholders) {
      const token = element("button", "message-token", `{${placeholder}}`);
      const description = placeholderDescriptions[placeholder] || "A live value supplied by CircuitOS.";
      token.type = "button";
      token.dataset.description = description;
      token.title = description;
      token.setAttribute("aria-label", `Insert {${placeholder}}: ${description}`);
      token.addEventListener("click", () => {
        const start = textarea.selectionStart ?? textarea.value.length;
        const end = textarea.selectionEnd ?? start;
        textarea.setRangeText(`{${placeholder}}`, start, end, "end");
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
        textarea.focus();
      });
      tokens.append(token);
    }
    const preview = element("div", "message-preview");
    preview.append(element("strong", "", "SAMPLE"), document.createTextNode(sampleMessage(textarea.value)));
    const error = element("div", "message-error");
    error.hidden = true;
    textarea.addEventListener("input", () => {
      systemProfile.messages[key] = textarea.value;
      profileDirty = true;
      setupBundle = null;
      const errors = validateMessageTemplate(key, textarea.value);
      error.hidden = !errors.length;
      error.textContent = errors.join(" ");
      preview.replaceChildren(element("strong", "", "SAMPLE"), document.createTextNode(sampleMessage(textarea.value)));
      updateCharacterCount();
      updateProfileStatus();
      updateMessageStatus();
      renderStreamerBotSetup();
    });
    card.append(header, helpText, textarea, editorMeta, tokens, error, preview);
    list.append(card);
  }
  updateMessageStatus();
}

function updateMessageStatus() {
  const status = document.getElementById("messageStatus");
  const errors = Object.entries(systemProfile.messages).flatMap(([key, value]) => validateMessageTemplate(key, value));
  status.textContent = errors.length ? `${errors.length} ERROR${errors.length === 1 ? "" : "S"}` : profileDirty ? "UNSAVED" : "VALID";
  status.className = `metric-chip ${errors.length ? "invalid" : profileDirty ? "warning" : "valid"}`;
  document.getElementById("saveMessagesButton").disabled = errors.length > 0;
}

function resetMessageTemplate(key) {
  systemProfile.messages[key] = defaultSystemProfile.messages[key];
  profileDirty = true;
  setupBundle = null;
  renderMessages();
  updateProfileStatus();
  renderStreamerBotSetup();
}

function resetAllMessages() {
  if (!window.confirm("Reset all chat messages to the CircuitOS defaults? Changes are not saved until you choose Save Messages.")) return;
  systemProfile.messages = clone(defaultSystemProfile.messages);
  profileDirty = true;
  setupBundle = null;
  renderMessages();
  updateProfileStatus();
  renderStreamerBotSetup();
}

async function generateStreamerBotSetup() {
  const errors = validateProfileClient();
  if (errors.length) throw new Error(errors.join(" "));
  document.getElementById("setupVersion").textContent = "GENERATING";
  const response = await fetch("/api/setup", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ profile: systemProfile })
  });
  const result = await response.json();
  if (!response.ok || !result.ok) throw new Error((result.errors || ["Could not generate Streamer.bot actions."]).join(" "));
  setupBundle = result;
  renderStreamerBotSetup();
  return result;
}


function renderTwitchSettings() {
  const status = document.getElementById("twitchStatusChip");
  const account = document.getElementById("twitchAccountCard");
  const utilities = document.getElementById("twitchUtilityList");
  const rewards = document.getElementById("twitchRewardList");
  if (!status || !account || !utilities || !rewards) return;

  const { mode, twitch } = lastSession;
  const cloud = mode === "cloud";
  const liveProfiles = (profilesData.profiles || []).filter(profile => profile.isLive || profile.active);
  const configuredReward = String(systemProfile.redemptionName || "").trim();
  const tokenExpired = twitch?.expiresAt ? new Date(twitch.expiresAt).getTime() <= Date.now() : false;

  status.textContent = twitch ? "CONNECTED" : "CONNECT TWITCH";
  status.className = `metric-chip ${twitch ? "valid" : "warning"}`;

  account.replaceChildren();
  const accountCopy = element("div", "twitch-account-copy");
  accountCopy.append(
    element("span", "", "TWITCH ACCOUNT"),
    element("strong", "", twitch ? `@${twitch.login || twitch.displayName}` : "Connect Twitch"),
    element("small", "", twitch
      ? (tokenExpired ? "Refresh login before using native rewards." : "Connected and ready for Twitch setup.")
      : "Sign in when you want CircuitOS to manage rewards directly.")
  );
  const accountActions = element("div", "twitch-actions");
  const primary = element("button", `button twitch-button ${twitch ? "secondary" : "primary"}`, twitch ? "Refresh Login" : "Log in with Twitch");
  primary.type = "button";
  primary.addEventListener("click", loginTwitch);
  accountActions.append(primary);
  if (twitch) {
    const logout = element("button", "button danger", "Log out");
    logout.type = "button";
    logout.addEventListener("click", logoutTwitch);
    accountActions.append(logout);
  }
  account.append(accountCopy, accountActions);

  utilities.replaceChildren();
  const utilityCards = [
    { title: "Channel Rewards", detail: liveProfiles.length ? "Manage rewards below." : "Mark a profile Live first." },
    { title: "Native Mode", detail: cloud ? "Cloud bridge is active." : "You're in local mode — redemptions and chat go live right here, no extra setup." },
    { title: "Streamer.bot", detail: "Fallback setup remains available whenever you need pasted actions." }
  ];
  for (const card of utilityCards) {
    const item = element("div", "twitch-utility-card");
    item.append(element("strong", "", card.title), element("span", "", card.detail));
    utilities.append(item);
  }
  const permissionsCard = element("div", `twitch-utility-card twitch-scope-card${tokenExpired ? " warning" : ""}`);
  const permissionsList = element("ul", "twitch-scope-list");
  for (const item of ["Channel-point reward read/manage", "Redemption intake", "Chat command read/replies"]) {
    permissionsList.append(element("li", "", item));
  }
  const permissionAction = element("button", "button small secondary", twitch ? "Refresh Login" : "Log in with Twitch");
  permissionAction.type = "button";
  permissionAction.addEventListener("click", loginTwitch);
  permissionsCard.append(
    element("strong", "", tokenExpired ? "Permissions Need Refresh" : twitch ? "Permissions" : "Permissions CircuitOS uses"),
    element("span", "", tokenExpired
      ? "Your token looks expired. Refresh login before syncing rewards or listening for chat."
      : twitch
        ? "If reward sync, redemptions, or chat replies stop working after an update, refresh your login so Twitch can grant the newest permissions."
        : "When you connect, CircuitOS asks Twitch for just these:"),
    permissionsList,
    permissionAction
  );
  utilities.append(permissionsCard);
  const attachCard = element("div", "twitch-utility-card");
  attachCard.append(
    element("strong", "", "Attach-Only Rewards"),
    element("span", "", "Rewards not created by CircuitOS can be attached for routing, but edit/delete stays in Twitch unless CircuitOS can manage them.")
  );
  utilities.append(attachCard);
  if (twitch) {
    const refreshCard = element("div", "twitch-utility-card");
    const refreshButton = element("button", "button small secondary", twitchRewardCatalog.loading ? "Loading rewards..." : "Refresh Rewards");
    refreshButton.type = "button";
    refreshButton.disabled = twitchRewardCatalog.loading || tokenExpired;
    refreshButton.addEventListener("click", () => loadTwitchRewards(true));
    refreshCard.append(element("strong", "", "Existing Rewards"), element("span", "", twitchRewardCatalog.loaded ? `${twitchRewardCatalog.items.length} Twitch rewards loaded.` : "Load current channel-point rewards for attach/reuse."), refreshButton);
    utilities.append(refreshCard);
    if (!twitchRewardCatalog.loaded && !twitchRewardCatalog.loading && !twitchRewardCatalog.error && !tokenExpired) loadTwitchRewards();
  }

  rewards.replaceChildren();
  if (!liveProfiles.length) {
    const empty = element("div", "twitch-empty-state");
    empty.append(
      element("strong", "", "Choose a live profile first"),
      element("span", "", "Open Profiles and mark the game you want Twitch to run as Live.")
    );
    rewards.append(empty);
    return;
  }
  for (const profile of liveProfiles) {
    const reward = profile.twitchReward || null;
    const row = element("div", "twitch-reward-row");
    const actions = element("div", "twitch-reward-actions");
    const rewardCell = element("div", "twitch-reward-cell");
    rewardCell.append(element("span", "", reward?.title || configuredReward || "Reward not named"));
    const picker = element("select", "twitch-reward-select");
    picker.append(new Option("Create/sync from profile name", ""));
    for (const existing of twitchRewardCatalog.items) {
      const option = new Option(`${existing.title} (${existing.cost || 0} pts)${existing.manageable ? "" : " • attach only"}`, existing.rewardId);
      if (reward?.rewardId && existing.rewardId === reward.rewardId) option.selected = true;
      picker.append(option);
    }
    picker.disabled = !twitch || tokenExpired || twitchRewardCatalog.loading;
    picker.title = "Choose an existing Twitch reward to attach, or leave blank to create/sync from this profile.";
    rewardCell.append(picker);
    if (twitchRewardCatalog.loading) rewardCell.append(element("small", "", "Loading Twitch rewards..."));
    if (twitchRewardCatalog.error) rewardCell.append(element("small", "", twitchRewardCatalog.error));

    const syncLabel = reward?.rewardId ? "Sync" : "Create";
    const syncButton = element("button", "button small secondary", syncLabel);
    syncButton.type = "button";
    syncButton.disabled = !twitch || tokenExpired;
    syncButton.title = twitch ? "Create/update from the profile name, or attach the selected existing Twitch reward." : "Log in to Twitch before syncing rewards.";
    syncButton.addEventListener("click", () => syncTwitchReward(profile.id, syncButton, picker.value));
    actions.append(syncButton);
    const editButton = element("button", "button small secondary", "Edit");
    editButton.type = "button";
    editButton.disabled = !twitch || tokenExpired || !reward?.rewardId || reward.manageable === false;
    editButton.title = reward?.manageable === false ? "This attached reward was not created by CircuitOS, so edit it in Twitch." : reward?.rewardId ? "Edit this CircuitOS-managed reward title and cost." : "Sync or attach a reward before editing it.";
    editButton.addEventListener("click", () => editTwitchReward(profile, reward, editButton));
    actions.append(editButton);

    const deleteButton = element("button", "button small danger", "Delete");
    deleteButton.type = "button";
    deleteButton.disabled = !twitch || tokenExpired || !reward?.rewardId || reward.manageable === false;
    deleteButton.title = reward?.manageable === false ? "This attached reward was not created by CircuitOS, so delete it in Twitch." : reward?.rewardId ? "Delete this CircuitOS-managed Twitch reward." : "Sync a reward before deleting it.";
    deleteButton.addEventListener("click", () => deleteTwitchReward(profile, reward, deleteButton));
    actions.append(deleteButton);
    row.append(
      element("div", "", ""),
      element("strong", "", profile.name || profile.id),
      rewardCell,
      element("small", "", reward?.rewardId ? (reward.manageable === false ? "Attached" : "Synced") : twitch ? "Ready to create" : "Login needed"),
      actions
    );
    rewards.append(row);
  }
}
async function loadTwitchRewards(force = false) {
  if (!lastSession.twitch) {
    twitchRewardCatalog = { loaded: false, loading: false, error: "", items: [] };
    renderTwitchSettings();
    return;
  }
  if (twitchRewardCatalog.loading || (twitchRewardCatalog.loaded && !force)) return;
  twitchRewardCatalog = { ...twitchRewardCatalog, loading: true, error: "" };
  renderTwitchSettings();
  try {
    const response = await fetch("/api/twitch/rewards", { cache: "no-store" });
    const result = await response.json();
    if (!response.ok || result.ok === false) {
      const message = result.errors?.join?.(" ") || result.error || "Could not load Twitch rewards.";
      throw new Error(message);
    }
    twitchRewardCatalog = { loaded: true, loading: false, error: "", items: Array.isArray(result.rewards) ? result.rewards : [] };
  } catch (error) {
    twitchRewardCatalog = { loaded: false, loading: false, error: error.message, items: [] };
    showNotice(error.message, "error");
  }
  renderTwitchSettings();
}

async function syncTwitchReward(profileId, button, rewardId = "") {
  const original = button?.textContent || "Sync";
  if (button) {
    button.disabled = true;
    button.textContent = "Syncing...";
  }
  try {
    const response = await fetch("/api/twitch/reward-sync", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ profileId, rewardId })
    });
    const result = await response.json();
    if (!response.ok || result.ok === false) {
      const message = result.errors?.join?.(" ") || result.error || "Twitch reward sync failed.";
      throw new Error(message);
    }
    if (Array.isArray(result.profiles)) profilesData.profiles = result.profiles;
    renderProfileSwitcher();
    renderTwitchSettings();
    showNotice(`Twitch reward ready: ${result.reward?.title || "channel-point reward"}.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
    renderTwitchSettings();
  } finally {
    if (button) button.textContent = original;
  }
}
async function editTwitchReward(profile, reward, button) {
  if (!reward?.rewardId) return;
  const currentTitle = reward.title || "";
  const nextTitle = prompt("Twitch reward title", currentTitle);
  if (nextTitle === null) return;
  const trimmedTitle = nextTitle.trim();
  if (!trimmedTitle) {
    showNotice("Twitch reward title cannot be empty.", "error");
    return;
  }
  const currentCost = Number(reward.cost || 100);
  const nextCostText = prompt("Twitch reward cost", String(currentCost));
  if (nextCostText === null) return;
  const nextCost = Number.parseInt(nextCostText, 10);
  if (!Number.isFinite(nextCost) || nextCost < 1) {
    showNotice("Twitch reward cost must be at least 1 point.", "error");
    return;
  }

  const original = button?.textContent || "Edit";
  if (button) {
    button.disabled = true;
    button.textContent = "Saving...";
  }
  try {
    const response = await fetch("/api/twitch/reward-update", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ profileId: profile.id, title: trimmedTitle, cost: nextCost })
    });
    const result = await response.json();
    if (!response.ok || result.ok === false) {
      const message = result.errors?.join?.(" ") || result.error || "Twitch reward update failed.";
      throw new Error(message);
    }
    if (Array.isArray(result.profiles)) profilesData.profiles = result.profiles;
    if (profile.id === profilesData.activeProfileId) {
      systemProfile.redemptionName = result.reward?.title || trimmedTitle;
      applySystemProfile();
    }
    if (twitchRewardCatalog.loaded) {
      twitchRewardCatalog.items = twitchRewardCatalog.items.map(item => item.rewardId === result.reward?.rewardId ? { ...item, ...result.reward } : item);
    }
    renderProfileSwitcher();
    renderTwitchSettings();
    showNotice(`Updated Twitch reward: ${result.reward?.title || trimmedTitle}.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
    renderTwitchSettings();
  } finally {
    if (button) button.textContent = original;
  }
}
async function deleteTwitchReward(profile, reward, button) {
  const rewardName = reward?.title || "this Twitch reward";
  if (!confirm(`Delete ${rewardName} from Twitch for ${profile.name || profile.id}? This only removes the channel-point reward and clears the saved reward id.`)) return;
  const original = button?.textContent || "Delete";
  if (button) {
    button.disabled = true;
    button.textContent = "Deleting...";
  }
  try {
    const response = await fetch("/api/twitch/reward-delete", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ profileId: profile.id })
    });
    const result = await response.json();
    if (!response.ok || result.ok === false) {
      const message = result.errors?.join?.(" ") || result.error || "Twitch reward delete failed.";
      throw new Error(message);
    }
    if (Array.isArray(result.profiles)) profilesData.profiles = result.profiles;
    renderProfileSwitcher();
    renderTwitchSettings();
    showNotice(`Deleted Twitch reward: ${result.deletedReward?.title || rewardName}.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
    renderTwitchSettings();
  } finally {
    if (button) button.textContent = original;
  }
}
function renderStreamerBotSetup() {
  const version = document.getElementById("setupVersion");
  const summary = document.getElementById("setupSummary");
  const actionsContainer = document.getElementById("setupActions");
  const checklist = document.getElementById("setupChecklist");
  summary.replaceChildren();
  actionsContainer.replaceChildren();
  checklist.replaceChildren();
  if (!setupBundle) {
    version.textContent = "REGENERATE";
    summary.append(element("div", "empty-state", "Generate the Streamer.bot package after finishing the profile."));
    return;
  }

  version.textContent = `${setupBundle.integrationPlatform || platformName} ${setupBundle.integrationVersion}`;
  const summaryValues = [
    ["GAME PROFILE", systemProfile.gameName],
    ["DATA FOLDER", setupBundle.dataPath],
    ["ACTIONS", String(setupBundle.actions?.length || 0)]
  ];
  for (const [label, value] of summaryValues) {
    const item = element("div", "setup-summary-item");
    item.append(element("span", "", label), element("strong", "", value));
    summary.append(item);
  }

  (setupBundle.actions || []).forEach((action, index) => {
    const card = element("article", "setup-action");
    const header = element("div", "setup-action-header");
    const step = element("div", "setup-step");
    step.append(element("span", "", String(index + 1)));
    const copy = element("div");
    copy.append(element("h2", "", action.name), element("p", "", action.description));
    const toggle = element("button", "button secondary", "Show C#");
    toggle.addEventListener("click", () => {
      card.classList.toggle("open");
      toggle.textContent = card.classList.contains("open") ? "Hide C#" : "Show C#";
    });
    header.append(step, copy, toggle);
    const triggers = element("div", "setup-triggers");
    for (const trigger of action.triggers || []) triggers.append(element("span", "setup-trigger", trigger));
    for (const reference of action.references || []) triggers.append(element("span", "setup-trigger setup-reference", `Reference: ${reference}`));
    const code = element("div", "setup-code");
    const textarea = document.createElement("textarea");
    textarea.readOnly = true;
    textarea.value = action.source;
    textarea.setAttribute("aria-label", `${action.name} generated C#`);
    const codeActions = element("div", "setup-code-actions");
    const copyButton = element("button", "button primary", "Copy C#");
    copyButton.addEventListener("click", () => copyGeneratedCode(action.name, textarea));
    codeActions.append(element("span", "setup-trigger", "Execute C# sub-action"), copyButton);
    code.append(textarea, codeActions);
    card.append(header, triggers, code);
    actionsContainer.append(card);
  });

  for (const item of setupBundle.checklist || []) checklist.append(element("div", "setup-check", item));
}

async function copyGeneratedCode(actionName, textarea) {
  try {
    await navigator.clipboard.writeText(textarea.value);
  } catch {
    textarea.select();
    if (!document.execCommand("copy")) throw new Error("Clipboard access was unavailable.");
    textarea.setSelectionRange(0, 0);
  }
  showNotice(`${actionName} C# copied. Paste it into a Streamer.bot Execute C# sub-action.`, "success");
}

const wizardProfileFields = {
  gameName: "wizardGameName",
  adminName: "wizardAdminName",
  redemptionName: "wizardRedemptionName",
  itemSingular: "wizardItemSingular",
  itemPlural: "wizardItemPlural",
  collectionSingular: "wizardCollectionSingular",
  collectionPlural: "wizardCollectionPlural",
  currencyName: "wizardCurrencyName"
};

function blankWizardProfile() {
  const profile = clone(defaultSystemProfile);
  Object.assign(profile, {
    gameName: "My Collection Game",
    adminName: "My Collection Control",
    brandKicker: "CIRCUITOS",
    redemptionName: "Collect an Item",
    itemSingular: "item",
    itemPlural: "items",
    collectionSingular: "collection",
    collectionPlural: "collections",
    currencyName: "Points"
  });
  profile.commands = {
    inventory: "inventory",
    missing: "missing",
    duplicates: "duplicates",
    leaderboard: "leaderboard",
    balance: "balance",
    collection: "collection",
    salvage: "salvage"
  };
  return profile;
}

function wizardSetError(message = "") {
  const error = document.getElementById("wizardError");
  error.hidden = !message;
  error.textContent = message;
}

function applyWizardProfile(profile) {
  for (const [field, id] of Object.entries(wizardProfileFields)) {
    document.getElementById(id).value = profile[field] || "";
  }
  document.getElementById("wizardAccent").value = profile.colors?.accent || "#ff1a24";
}

function selectWizardPreset(preset) {
  wizardPreset = preset;
  document.querySelectorAll("[data-wizard-preset]").forEach(button => {
    button.classList.toggle("active", button.dataset.wizardPreset === preset);
  });
  applyWizardProfile(preset === "circuit" ? clone(defaultSystemProfile) : blankWizardProfile());
  updateWizardCollectionStep();
  wizardSetError();
}

function updateWizardCollectionStep() {
  const blank = wizardPreset === "blank";
  document.getElementById("wizardBlankCollection").hidden = !blank;
  document.getElementById("wizardStarterSummary").hidden = blank;
  document.getElementById("wizardCollectionHeading").textContent = blank ? "Create the first collection" : "Review the included starter catalog";
  document.getElementById("wizardCollectionHelp").textContent = blank
    ? "Just type your starter items — one per line."
    : "Circuit Components can be renamed, expanded, or replaced after setup.";
  if (!blank) renderWizardStarterSummary();
}

function renderWizardStarterSummary() {
  const container = document.getElementById("wizardStarterSummary");
  container.innerHTML = "";
  const values = Object.values(firstRunStarterConfiguration?.components?.collections || {});
  const partCount = values.reduce((sum, value) => sum + (value.parts?.length || 0), 0);
  for (const [label, value] of [["Collections", values.length], ["Components", partCount], ["Starting profile", "Circuit Components"]]) {
    const card = element("div", "wizard-summary-card");
    card.append(element("span", "", label), element("strong", "", String(value)));
    container.append(card);
  }
}

function setWizardStep(step) {
  wizardStep = Math.max(1, Math.min(4, step));
  document.querySelectorAll("[data-wizard-step]").forEach(section => {
    section.hidden = Number(section.dataset.wizardStep) !== wizardStep;
  });
  document.querySelectorAll("[data-wizard-progress]").forEach(progress => {
    const value = Number(progress.dataset.wizardProgress);
    progress.classList.toggle("active", value === wizardStep);
    progress.classList.toggle("complete", value < wizardStep);
  });
  document.getElementById("wizardBackButton").hidden = wizardStep === 1;
  document.getElementById("wizardNextButton").hidden = wizardStep === 4;
  document.getElementById("wizardCompleteButton").hidden = wizardStep !== 4;
  wizardSetError();
  if (wizardStep === 3) updateWizardCollectionStep();
  if (wizardStep === 4) renderWizardReview();
}

function wizardSlug(value, fallback) {
  return String(value || "").trim().toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "") || fallback;
}

function wizardItemNames() {
  return document.getElementById("wizardItems").value.split(/\r?\n/).map(value => value.trim()).filter(Boolean);
}

function validateWizardStep() {
  if (wizardStep === 2) {
    for (const id of Object.values(wizardProfileFields)) {
      if (!document.getElementById(id).value.trim()) return "Complete every game identity field before continuing.";
    }
  }
  if (wizardStep === 3 && wizardPreset === "blank") {
    if (!document.getElementById("wizardCollectionName").value.trim()) return "Enter a name for the first collection.";
    if (!wizardItemNames().length) return "Enter at least one starter item.";
  }
  return "";
}

function buildWizardProfile() {
  const profile = wizardPreset === "circuit" ? clone(defaultSystemProfile) : blankWizardProfile();
  for (const [field, id] of Object.entries(wizardProfileFields)) profile[field] = document.getElementById(id).value.trim();
  profile.colors.accent = document.getElementById("wizardAccent").value;
  return profile;
}

function buildWizardConfiguration() {
  if (wizardPreset === "circuit") return clone(firstRunStarterConfiguration);
  const displayName = document.getElementById("wizardCollectionName").value.trim();
  const collectionKey = wizardSlug(displayName, "starter_collection");
  const usedIds = new Set();
  const parts = wizardItemNames().map((name, index) => {
    const base = `${collectionKey}_${wizardSlug(name, `item_${index + 1}`)}`;
    let id = base;
    let suffix = 2;
    while (usedIds.has(id)) id = `${base}_${suffix++}`;
    usedIds.add(id);
    return { id, name };
  });
  return {
    components: {
      collections: {
        [collectionKey]: { displayName, type: "permanent", weight: 100, salvageValue: 1, parts }
      }
    },
    boost: { enabled: false, displayName: "Featured Boost", collectionMultipliers: {} }
  };
}

function renderWizardReview() {
  const profile = buildWizardProfile();
  const configuration = buildWizardConfiguration();
  const collectionValues = Object.values(configuration.components.collections);
  const partCount = collectionValues.reduce((sum, value) => sum + value.parts.length, 0);
  const review = document.getElementById("wizardReview");
  review.innerHTML = "";
  const rows = [
    ["Game", profile.gameName],
    ["Starting point", wizardPreset === "blank" ? "Blank collection" : "Circuit Components"],
    [profile.collectionPlural, collectionValues.length],
    [profile.itemPlural, partCount],
    ["Currency", profile.currencyName],
    ["Redemption", profile.redemptionName]
  ];
  for (const [label, value] of rows) {
    const item = element("div", "wizard-review-item");
    item.append(element("span", "", String(label)), element("strong", "", String(value)));
    review.append(item);
  }
}

function initializeFirstRunWizard() {
  if (!wizardInitialized) {
    wizardInitialized = true;
    selectWizardPreset("blank");
    setWizardStep(1);
  }
  const overlay = document.getElementById("firstRunWizard");
  overlay.hidden = false;
  const appShell = document.querySelector(".app-shell");
  appShell.inert = true;
  appShell.setAttribute("aria-hidden", "true");
}

function closeFirstRunWizard() {
  document.getElementById("firstRunWizard").hidden = true;
  const appShell = document.querySelector(".app-shell");
  appShell.inert = false;
  appShell.removeAttribute("aria-hidden");
}

async function completeFirstRun() {
  const button = document.getElementById("wizardCompleteButton");
  button.disabled = true;
  button.textContent = "Creating...";
  wizardSetError();
  try {
    const response = await fetch("/api/first-run", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ profile: buildWizardProfile(), configuration: buildWizardConfiguration() })
    });
    const result = await response.json();
    if (!response.ok) throw new Error((result.errors || ["First-run setup failed."]).join(" "));
    closeFirstRunWizard();
    await loadConfiguration(true);
    switchView("twitch");
    showNotice("Setup complete! Next: connect your Twitch account to go live. (Prefer Streamer.bot? Its setup is on the Streamer.bot page.)", "success");
  } catch (error) {
    wizardSetError(error.message);
  } finally {
    button.disabled = false;
    button.textContent = "Create Game";
  }
}

function normalizeModel(payload) {
  dataPath = payload.dataPath || "";
  collections = Object.entries(payload.components?.collections || {}).map(([key, value]) => ({
    key,
    value: {
      displayName: value.displayName || key,
      type: value.type || "permanent",
      weight: Number(value.weight ?? 0),
      salvageValue: Number(value.salvageValue ?? 1),
      rareLabel: value.rareLabel || "",
      ...clone(value),
      parts: Array.isArray(value.parts) ? clone(value.parts) : [],
      tiers: Array.isArray(value.tiers) ? clone(value.tiers) : [],
      variants: Array.isArray(value.variants) ? clone(value.variants) : []
    }
  }));
  boost = clone(payload.boost || { enabled: false, displayName: "Featured Boost", collectionMultipliers: {} });
  boost.collectionMultipliers ||= {};
}

function serializeModel() {
  const collectionObject = {};
  for (const collection of collections) {
    const value = clone(collection.value);
    if (!value.rareLabel) delete value.rareLabel;
    if (!value.tiers || value.tiers.length === 0) delete value.tiers;
    if (!value.variants || value.variants.length === 0) delete value.variants;
    if (value.type !== "event") {
      delete value.enabled;
      delete value.activeFromUtc;
      delete value.activeUntilUtc;
    }
    collectionObject[collection.key] = value;
  }
  return { components: { collections: collectionObject }, boost: clone(boost) };
}

async function loadConfiguration(force = false) {
  if ((dirty || profileDirty) && !force && !window.confirm("Discard unsaved editor changes and refresh all live data?")) return false;
  clearNotice();
  const [configResponse, analyticsResponse, rolesResponse, backupsResponse, profileResponse, healthResponse, overlayConfigResponse] = await Promise.all([
    fetch("/api/config", { cache: "no-store" }),
    fetch("/api/analytics", { cache: "no-store" }),
    fetch("/api/roles", { cache: "no-store" }),
    fetch("/api/backups", { cache: "no-store" }),
    fetch("/api/profile", { cache: "no-store" }),
    fetch("/api/health", { cache: "no-store" }),
    fetch("/api/overlay-config", { cache: "no-store" })
  ]);
  if (!configResponse.ok) throw new Error("Could not load live configuration.");
  if (!analyticsResponse.ok) throw new Error("Could not load inventory analytics.");
  if (!rolesResponse.ok) throw new Error("Could not load Discord role awards.");
  if (!backupsResponse.ok) throw new Error("Could not load backup history.");
  if (!profileResponse.ok) throw new Error("Could not load the system profile.");
  if (!healthResponse.ok) throw new Error("Could not load the CircuitOS runtime version.");
  const payload = await configResponse.json();
  analytics = await analyticsResponse.json();
  roleAwards = await rolesResponse.json();
  backupCenter = await backupsResponse.json();
  const profilePayload = await profileResponse.json();
  const healthPayload = await healthResponse.json();
  runtimeInfo = { runtime: healthPayload.runtime || "unknown", version: healthPayload.version || "unknown" };
  overlayFilePath = healthPayload.overlayFilePath || "";
  systemProfile = normalizeProfile(profilePayload.profile);
  profileConfigured = Boolean(profilePayload.isConfigured);
  profileDirty = false;
  const overlayPayload = overlayConfigResponse.ok ? await overlayConfigResponse.json() : null;
  overlayConfig = overlayPayload?.config ?? {
    schemaVersion: 1, enabled: true,
    layout: { position: "bottom-center", width: 1500, minHeight: 178, offsetX: 0, offsetY: 54 },
    timing: { displaySeconds: 8, enterMilliseconds: 620, exitMilliseconds: 430 },
    appearance: { backgroundColor: "#00101f", panelColor: "#06233e", accentColor: "#ff1821", textColor: "#f8fbff", mutedColor: "#8094ad", backgroundImage: "", backgroundOpacity: 0.98 },
    typography: { fontFamily: "Segoe UI" },
    content: { showCollection: true, showProgress: true, showCircuitOSBranding: true },
    animation: { style: "slide" },
    labels: { eyebrow: "CIRCUIT SCAN", componentAcquired: "COMPONENT ACQUIRED", collectionProgress: "COLLECTION PROGRESS", newItem: "NEW COMPONENT", collectionComplete: "COLLECTION COMPLETE", duplicate: "DUPLICATE" }
  };
  overlayDirty = false;
  if (selectedBackupFile && !backupCenter.backups.some(item => item.fileName === selectedBackupFile)) {
    selectedBackupFile = "";
    selectedBackupPreview = null;
  }
  if (!selectedViewerId || !analytics.viewers.some(viewer => viewer.id === selectedViewerId)) {
    selectedViewerId = analytics.viewers[0]?.id || "";
  }
  normalizeModel(payload);
  if (!profileConfigured && !firstRunStarterConfiguration) firstRunStarterConfiguration = clone(serializeModel());
  baselineModel = clone(serializeModel());
  document.getElementById("runtimeVersion").textContent = `Version ${healthPayload.version || "unknown"}`;
  renderSessionMode(healthPayload.mode || "local", healthPayload.twitch || null, dataPath);
  markClean();
  applySystemProfile();
  await loadProfiles().catch(() => {});
  renderAll();
  generateStreamerBotSetup().catch(error => showNotice(error.message, "error"));
  if (!profileConfigured) {
    initializeFirstRunWizard();
  }
  if (payload.validationErrors?.length) {
    showNotice(payload.validationErrors.join(" "), "error");
  }
  return !payload.validationErrors?.length;
}

async function refreshOperationalData() {
  if (operationalRefreshInFlight) return;
  operationalRefreshInFlight = true;
  try {
    const [analyticsResponse, rolesResponse, backupsResponse] = await Promise.all([
      fetch("/api/analytics", { cache: "no-store" }),
      fetch("/api/roles", { cache: "no-store" }),
      fetch("/api/backups", { cache: "no-store" })
    ]);
    if (!analyticsResponse.ok || !rolesResponse.ok || !backupsResponse.ok) {
      throw new Error("Could not refresh live operational data.");
    }
    analytics = await analyticsResponse.json();
    roleAwards = await rolesResponse.json();
    backupCenter = await backupsResponse.json();
    if (!selectedViewerId || !analytics.viewers.some(viewer => viewer.id === selectedViewerId)) {
      selectedViewerId = analytics.viewers[0]?.id || "";
    }
    if (selectedBackupFile && !backupCenter.backups.some(item => item.fileName === selectedBackupFile)) {
      selectedBackupFile = "";
      selectedBackupPreview = null;
    }
    renderOverview();
    renderEconomy();
    renderViewerInspector();
    renderRoleAwards();
    renderBackups();
  } finally {
    operationalRefreshInFlight = false;
  }
}

async function refreshLiveData() {
  const button = document.getElementById("reloadButton");
  button.disabled = true;
  button.textContent = "Refreshing...";
  try {
    const loadedCleanly = await loadConfiguration();
    if (loadedCleanly) showNotice("Live data refreshed from disk.", "success");
  } catch (error) {
    showNotice(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "Refresh";
  }
}

function validateModel() {
  const errors = [];
  const keys = new Set();
  const partIds = new Set();
  const keyPattern = /^[a-z0-9][a-z0-9_]*$/;

  if (!collections.length) errors.push("At least one collection is required.");

  for (const collection of collections) {
    const { key, value } = collection;
    if (!keyPattern.test(key)) errors.push(`Collection key '${key}' is invalid.`);
    if (keys.has(key.toLowerCase())) errors.push(`Collection key '${key}' is duplicated.`);
    keys.add(key.toLowerCase());
    if (!String(value.displayName || "").trim()) errors.push(`Collection '${key}' needs a display name.`);
    if (!["permanent", "event"].includes(value.type)) errors.push(`Collection '${key}' has an invalid type.`);
    if (!Number.isFinite(Number(value.weight)) || Number(value.weight) < 0) errors.push(`Collection '${key}' needs a nonnegative weight.`);
    if (!Number.isInteger(Number(value.salvageValue)) || Number(value.salvageValue) <= 0) errors.push(`Collection '${key}' needs a positive whole salvage value.`);
    if (!Array.isArray(value.parts) || !value.parts.length) errors.push(`Collection '${key}' needs at least one component.`);

    for (const part of value.parts || []) {
      const partId = String(part.id || "");
      if (!keyPattern.test(partId)) errors.push(`Component ID '${partId}' is invalid.`);
      if (partIds.has(partId.toLowerCase())) errors.push(`Component ID '${partId}' is duplicated.`);
      partIds.add(partId.toLowerCase());
      if (!String(part.name || "").trim()) errors.push(`Component '${partId}' needs a name.`);
    }

    if (value.type === "event") {
      if (typeof value.enabled !== "boolean") errors.push(`Event '${key}' enabled must be true or false.`);
      const start = new Date(value.activeFromUtc);
      const end = new Date(value.activeUntilUtc);
      if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start) {
        errors.push(`Event '${key}' needs a valid start before its end.`);
      }
    }
  }

  if (typeof boost.enabled !== "boolean") errors.push("Boost enabled must be true or false.");
  if (boost.enabled && !String(boost.displayName || "").trim()) errors.push("An enabled boost needs a display name.");
  const multiplierEntries = Object.entries(boost.collectionMultipliers || {});
  if (boost.enabled && !multiplierEntries.length) errors.push("An enabled boost needs at least one multiplier above 1.");
  for (const [key, multiplier] of multiplierEntries) {
    if (!keys.has(key.toLowerCase())) errors.push(`Boost references unknown collection '${key}'.`);
    if (!Number.isFinite(Number(multiplier)) || Number(multiplier) <= 0) errors.push(`Boost multiplier for '${key}' is invalid.`);
  }

  return [...new Set(errors)];
}

function eventState(value) {
  if (value.type !== "event") return { label: "PERMANENT", className: "" };
  if (!value.enabled) return { label: "DISABLED", className: "disabled" };
  const now = Date.now();
  const start = new Date(value.activeFromUtc).getTime();
  const end = new Date(value.activeUntilUtc).getTime();
  if (!Number.isFinite(start) || !Number.isFinite(end)) return { label: "INVALID", className: "ended" };
  if (now < start) return { label: "UPCOMING", className: "upcoming" };
  if (now >= end) return { label: "ENDED", className: "ended" };
  return { label: "ACTIVE", className: "active" };
}

function activeForRates(value) {
  if (value.type !== "event") return true;
  return eventState(value).label === "ACTIVE";
}

function effectiveRates() {
  const rows = collections.filter(c => activeForRates(c.value)).map(collection => {
    const multiplier = boost.enabled ? Number(boost.collectionMultipliers?.[collection.key] ?? 1) : 1;
    return {
      name: collection.value.displayName,
      key: collection.key,
      weight: Math.max(0, Number(collection.value.weight) || 0) * Math.max(0, multiplier || 0)
    };
  });
  const total = rows.reduce((sum, row) => sum + row.weight, 0);
  return { rows: rows.map(row => ({ ...row, percent: total > 0 ? (row.weight / total) * 100 : 0 })), total };
}

function renderAll() {
  const activeView = document.querySelector(".view.active")?.id?.replace(/View$/, "") || "overview";
  renderProfile();
  renderMessages();
  renderOverview();
  renderEconomy();
  renderRoleAwards();
  renderPatchNotes();
  renderBackups();
  renderTwitchSettings();
  renderStreamerBotSetup();
  renderBoost();
  renderViewOnDemand(activeView);
}

async function renderSettings() {
  const cards = document.getElementById("settingsBackendCards");
  const errorBox = document.getElementById("settingsCloudError");
  document.getElementById("settingsRestartNote").hidden = true;
  try {
    const data = await (await fetch("/api/settings", { cache: "no-store" })).json();
    if (data.cloudError) {
      errorBox.hidden = false;
      errorBox.textContent = `Cloud mode couldn't start, so CircuitOS is running locally: ${data.cloudError}`;
    } else {
      errorBox.hidden = true;
    }
    const running = data.dataBackend;                      // what's actually live now
    const chosen = data.cloudEnabled ? "cloud" : "local";  // the saved choice
    cards.replaceChildren(
      backendCard("local", "Local (this PC)", "Everything stays in your CircuitOS data folder. No account needed — this is the default.", chosen, running),
      backendCard("cloud", "Cloud (Appwrite)", "Sync your data to your own Appwrite project so it follows you across machines.", chosen, running)
    );
    const a = data.appwrite || {};
    document.getElementById("appwriteEndpoint").value = a.endpoint || "";
    document.getElementById("appwriteProject").value = a.projectId || "";
    document.getElementById("appwriteDatabase").value = a.databaseId || "";
    document.getElementById("appwriteCollection").value = a.collectionId || "";
    const keyInput = document.getElementById("appwriteApiKey");
    keyInput.value = "";
    keyInput.placeholder = a.hasApiKey ? "Saved — paste to replace" : "Paste your API key";
    document.getElementById("appwriteTestResult").hidden = true;
    // About
    document.getElementById("aboutVersion").textContent = runtimeInfo.version || "—";
    document.getElementById("aboutBackend").textContent = data.dataBackend === "cloud" ? "Cloud (Appwrite)" : "Local (this PC)";
    document.getElementById("aboutDataFolder").textContent = data.dataRoot || "—";
    document.getElementById("hideSystemCheckSetting").checked = localStorage.getItem("circuitos.hideSystemCheck") === "1";
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function openDataFolder() {
  try {
    const resp = await fetch("/api/settings/open-folder", { method: "POST" });
    const result = await resp.json();
    if (!resp.ok || !result.ok) throw new Error((result.errors || ["Could not open the folder."]).join(" "));
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function backendCard(id, title, desc, chosen, running) {
  const card = element("button", "settings-backend-card");
  card.type = "button";
  if (id === chosen) card.classList.add("selected");
  card.append(element("strong", "", title), element("span", "", desc));
  const tags = element("div", "settings-backend-tags");
  if (id === running) tags.append(element("span", "settings-tag running", "Running now"));
  if (id === chosen && id !== running) tags.append(element("span", "settings-tag pending", "Restart to apply"));
  card.append(tags);
  card.addEventListener("click", () => chooseBackend(id));
  return card;
}

async function chooseBackend(backend) {
  try {
    const resp = await fetch("/api/settings/mode", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ dataBackend: backend })
    });
    const result = await resp.json();
    if (!resp.ok || !result.ok) throw new Error((result.errors || ["Could not change data storage."]).join(" "));
    await renderSettings();
    const note = document.getElementById("settingsRestartNote");
    if (result.restartRequired) {
      note.hidden = false;
      note.className = "notice";
      note.textContent = `Saved. Restart CircuitOS to switch to ${backend === "cloud" ? "cloud" : "local"} storage.`;
    } else {
      showNotice("Data storage setting saved.", "success");
    }
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function appwriteFormBody() {
  return {
    endpoint: document.getElementById("appwriteEndpoint").value.trim(),
    projectId: document.getElementById("appwriteProject").value.trim(),
    apiKey: document.getElementById("appwriteApiKey").value,
    databaseId: document.getElementById("appwriteDatabase").value.trim(),
    collectionId: document.getElementById("appwriteCollection").value.trim()
  };
}

async function saveAppwriteConnection() {
  const resp = await fetch("/api/settings/appwrite", {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify(appwriteFormBody())
  });
  const result = await resp.json();
  if (!resp.ok || !result.ok) throw new Error((result.errors || ["Save failed."]).join(" "));
  return result;
}

async function saveAppwriteConnectionAndNotify() {
  try {
    await saveAppwriteConnection();
    await renderSettings();
    showNotice("Appwrite connection saved.", "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

// Saves the on-screen values first (so we test exactly what's shown), then asks the server to connect.
async function testAppwriteConnection() {
  const box = document.getElementById("appwriteTestResult");
  const button = document.getElementById("testAppwriteButton");
  box.hidden = false;
  box.className = "settings-test-result";
  box.textContent = "Testing connection…";
  button.disabled = true;
  try {
    await saveAppwriteConnection();
    const resp = await fetch("/api/settings/appwrite/test", { method: "POST" });
    const result = await resp.json();
    box.classList.add(result.ok ? "ok" : "error");
    box.textContent = result.message || (result.ok ? "Connected." : "Connection failed.");
    document.getElementById("appwriteApiKey").value = "";
    document.getElementById("appwriteApiKey").placeholder = "Saved — paste to replace";
  } catch (error) {
    box.classList.add("error");
    box.textContent = error.message;
  } finally {
    button.disabled = false;
  }
}

function renderViewOnDemand(view) {
  if (view === "viewers") renderViewerInspector();
  if (view === "collections") renderCollectionList("permanent");
  if (view === "events") renderCollectionList("event");
  if (view === "overlay") { renderOverlayEditor(); scaleOverlayPreview(); }
  if (view === "profiles") { renderProfilesSummary(); renderProfiles(); }
  if (view === "ratelab") renderRateLab();
  if (view === "settings") renderSettings();
}

async function loadProfiles() {
  const response = await fetch("/api/profiles", { cache: "no-store" });
  if (!response.ok) return;
  profilesData = await response.json();
  const activeView = document.querySelector(".view.active")?.id?.replace(/View$/, "") || "overview";
  if (activeView === "profiles") {
    renderProfilesSummary();
    renderProfiles();
  }
  renderProfileSwitcher();
  renderTwitchSettings();
}

function renderProfilesSummary() {
  const panel = document.getElementById("profilesSummary");
  if (!panel) return;
  const profiles = profilesData.profiles || [];
  const editing = profiles.find(profile => profile.isActive) || null;
  const live = profiles.filter(profile => profile.isLive);
  panel.replaceChildren();
  const card = element("div", "profile-summary-card");
  const title = element("div", "profile-summary-title", editing ? `${editing.name} is the profile you're editing` : "No profile is currently selected for editing");
  const detail = element("div", "profile-summary-detail", live.length
    ? `Live now: ${live.map(profile => profile.name).join(", ")}`
    : "No profiles are live right now.");
  const badge = element("div", `profile-summary-badge${live.length ? " is-live" : ""}`, live.length ? `${live.length} live` : "Not live");
  card.append(badge, title, detail);
  panel.append(card);
}

function renderProfiles() {
  const grid = document.getElementById("profileGrid");
  grid.replaceChildren();
  for (const profile of profilesData.profiles || []) {
    const isActive = profile.id === profilesData.activeProfileId;
    const card = element("div", `profile-card${isActive ? " is-active" : ""}${profile.isLive ? " is-live" : ""}`);
    if (isActive) {
      card.append(element("div", "pc-badge", "EDITING"));
    }
    if (profile.isLive) {
      card.append(element("div", "pc-badge pc-badge-live", "LIVE"));
    }
    card.append(element("div", "pc-name", profile.name));
    const statusText = isActive ? (profile.isLive ? "Editing + live" : "Editing only") : (profile.isLive ? "Live now" : "Ready to go live");
    card.append(element("div", "pc-status", statusText));
    card.append(element("div", "pc-meta", `Created ${new Date(profile.createdAt).toLocaleDateString()}`));
    const actions = element("div", "pc-actions");
    const liveBtn = element("button", `button ${profile.isLive ? "secondary" : "primary"} small`, profile.isLive ? "Stop Live" : "Go Live");
    liveBtn.type = "button";
    liveBtn.addEventListener("click", () => toggleProfileLive(profile.id, profile.name, !profile.isLive));
    actions.append(liveBtn);
    if (!isActive) {
      const switchBtn = element("button", "button secondary small", "Switch");
      switchBtn.type = "button";
      switchBtn.addEventListener("click", () => openProfileSwitchConfirm(profile.id, profile.name));
      actions.append(switchBtn);
    }
    const renameBtn = element("button", "button secondary small", "Rename");
    renameBtn.type = "button";
    renameBtn.addEventListener("click", () => promptRenameProfile(profile.id, profile.name));
    actions.append(renameBtn);
    if (!isActive) {
      const deleteBtn = element("button", "button danger small", "Delete");
      deleteBtn.type = "button";
      deleteBtn.addEventListener("click", () => deleteProfile(profile.id, profile.name));
      actions.append(deleteBtn);
    }
    card.append(actions);
    grid.append(card);
  }
}

function renderProfileSwitcher() {
  const list = document.getElementById("profileSwitcherList");
  if (!list) return;
  list.replaceChildren();
  for (const profile of profilesData.profiles || []) {
    const isActive = profile.id === profilesData.activeProfileId;
    const btn = element("button", `profile-switcher-item${isActive ? " is-active" : ""}`, "");
    btn.type = "button";
    btn.setAttribute("role", "option");
    btn.setAttribute("aria-selected", String(isActive));
    const name = element("strong", "", profile.name);
    const date = element("small", "", isActive ? "Active now" : `Created ${new Date(profile.createdAt).toLocaleDateString()}`);
    btn.append(name, date);
    if (!isActive) btn.addEventListener("click", () => { closeProfileSwitcher(); openProfileSwitchConfirm(profile.id, profile.name); });
    list.append(btn);
  }
}

function openProfileSwitcher() {
  const trigger = document.getElementById("profileSwitcherTrigger");
  const dropdown = document.getElementById("profileSwitcherDropdown");
  trigger.setAttribute("aria-expanded", "true");
  dropdown.hidden = false;
}

function closeProfileSwitcher() {
  const trigger = document.getElementById("profileSwitcherTrigger");
  const dropdown = document.getElementById("profileSwitcherDropdown");
  trigger.setAttribute("aria-expanded", "false");
  dropdown.hidden = true;
}

function toggleProfileSwitcher() {
  const dropdown = document.getElementById("profileSwitcherDropdown");
  if (dropdown.hidden) openProfileSwitcher(); else closeProfileSwitcher();
}

function openProfileSwitchConfirm(id, name) {
  pendingSwitchId = id;
  document.getElementById("profileSwitchMessage").textContent = `Switch to "${name}"? Any unsaved changes will be lost and all data will reload.`;
  document.getElementById("profileSwitchModal").hidden = false;
}

function closeProfileSwitchConfirm() {
  pendingSwitchId = "";
  document.getElementById("profileSwitchModal").hidden = true;
}

async function confirmProfileSwitch() {
  const id = pendingSwitchId;
  if (!id) return;
  closeProfileSwitchConfirm();
  try {
    const response = await fetch("/api/profiles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "switch", id })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Switch failed."]).join(" "));
    await loadConfiguration(true);
    showNotice(`Profile switched. All data reloaded.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function toggleProfileLive(id, name, live) {
  try {
    const response = await fetch("/api/profiles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: live ? "activate" : "deactivate", id })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || [live ? "Could not activate profile." : "Could not deactivate profile."]).join(" "));
    await loadProfiles();
    showNotice(live ? `Profile "${name}" is now live.` : `Profile "${name}" is no longer live.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function promptRenameProfile(id, currentName) {
  const name = window.prompt(`Rename "${currentName}" to:`, currentName);
  if (!name || !name.trim() || name.trim() === currentName) return;
  try {
    const response = await fetch("/api/profiles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "rename", id, name: name.trim() })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Rename failed."]).join(" "));
    await loadProfiles();
    showNotice(`Profile renamed to "${result.name}".`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function deleteProfile(id, name) {
  if (!window.confirm(`Delete profile "${name}"? This cannot be undone.`)) return;
  try {
    const response = await fetch("/api/profiles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "delete", id })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Delete failed."]).join(" "));
    await loadProfiles();
    showNotice(`Profile "${name}" deleted.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function createProfile() {
  const name = window.prompt("New profile name:");
  if (!name || !name.trim()) return;
  try {
    const response = await fetch("/api/profiles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "create", name: name.trim() })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Create failed."]).join(" "));
    await loadProfiles();
    showNotice(`Profile "${result.name}" created.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function exportModule() {
  try {
    const response = await fetch("/api/modules/export", { cache: "no-store" });
    const result = await response.json();
    if (!response.ok) throw new Error((result.errors || ["Export failed."]).join(" "));
    const name = result.manifest?.name || "profile";
    const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "profile";
    const blob = new Blob([JSON.stringify(result, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `${slug}.circuitmodule`;
    anchor.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function importModule(file) {
  try {
    const text = await file.text();
    const module = JSON.parse(text);
    const response = await fetch("/api/modules/import", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(module)
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Import failed."]).join(" "));
    await loadProfiles();
    showNotice(`Module imported as profile "${result.name}". Switch to it from the Profiles view.`, "success");
  } catch (error) {
    if (error instanceof SyntaxError) showNotice("Could not parse the module file. Is it a valid .circuitmodule?", "error");
    else showNotice(error.message, "error");
  }
}

function renderOverview() {
  const stats = document.getElementById("stats");
  const summary = analytics.summary || {};
  const totalParts = collections.reduce((sum, c) => sum + (c.value.parts?.length || 0), 0);
  const eventCount = collections.filter(c => c.value.type === "event").length;
  const activeEvents = collections.filter(c => eventState(c.value).label === "ACTIVE").length;
  const rates = effectiveRates();
  const collectionRows = (analytics.collections || []).map(row => ({
    ...row,
    currentUnclaimedScrap: Number(row.duplicateUnits || 0) * currentSalvageValue(row.key, row.salvageValue)
  }));
  const totalUnclaimed = collectionRows.reduce((sum, row) => sum + row.currentUnclaimedScrap, 0);
  const values = [
    ["VIEWERS", summary.viewerCount || 0, "Inventories in the system", "viewers"],
    [systemProfile.collectionPlural.toUpperCase(), collections.length, `${collections.length - eventCount} permanent / ${eventCount} event`, "collections"],
    [systemProfile.itemPlural.toUpperCase(), totalParts, `Unique catalog ${systemProfile.itemPlural}`, "collections"],
    ["ACTIVE EVENTS", activeEvents, eventCount ? `${eventCount} configured event ${systemProfile.collectionPlural}` : "No events configured", "events"],
    [systemProfile.currencyName.toUpperCase(), summary.totalScrap || 0, `${summary.duplicateUnits || 0} duplicate units held`, "economy"],
    ["BOOST", boost.enabled ? "ON" : "OFF", boost.enabled ? boost.displayName : "Base rates in effect", "boost"]
  ];
  stats.replaceChildren();
  for (const [label, value, detail, view] of values) {
    const node = document.getElementById("statTemplate").content.cloneNode(true);
    node.querySelector(".stat-label").textContent = label;
    node.querySelector(".stat-value").textContent = value;
    node.querySelector(".stat-detail").textContent = detail;
    const card = node.querySelector(".stat-card");
    if (card && view) {
      card.classList.add("clickable-card");
      card.dataset.jumpView = view;
      card.addEventListener("click", () => switchView(view));
    }
    stats.appendChild(node);
  }

  document.getElementById("rateStateChip").textContent = boost.enabled ? "BOOST ACTIVE" : "BASE RATES";
  document.getElementById("rateChartTitle").textContent = boost.enabled ? "Boosted Pull Rates" : "Pull Rates";
  const chart = document.getElementById("rateChart");
  chart.replaceChildren();
  // Shared slider scale for the draggable weight bars, with headroom above the largest weight.
  const maxWeight = Math.max(0, ...collections.map(c => Number(c.value.weight) || 0));
  const sliderMax = Math.max(50, Math.ceil((maxWeight * 1.5) / 10) * 10);
  for (const row of rates.rows) {
    const wrapper = element("div", "rate-row");
    wrapper.dataset.collectionKey = row.key;
    wrapper.append(element("div", "rate-name", row.name));
    // The bar IS the slider — drag it to retune the weight; a small box allows exact entry.
    const col = collections.find(c => c.key === row.key);
    if (col) {
      const slider = document.createElement("input");
      slider.type = "range";
      slider.min = "0";
      slider.max = String(sliderMax);
      slider.step = "1";
      slider.className = "rate-slider";
      slider.title = "Drag to set the pull weight";
      slider.value = String(Math.max(0, Number(col.value.weight) || 0));
      const paintSlider = () => {
        const min = Number(slider.min) || 0;
        const max = Number(slider.max) || 100;
        const value = Number(slider.value) || 0;
        const ratio = max > min ? Math.min(1, Math.max(0, (value - min) / (max - min))) : 0;
        slider.style.setProperty("--fill", ratio);
      };
      paintSlider();
      slider.addEventListener("input", () => {
        col.value.weight = Math.max(0, Number(slider.value) || 0);
        paintSlider();
        markDirty();
        refreshOverviewRates();
      });
      wrapper.append(slider);
    } else {
      wrapper.append(element("div", ""));
    }
    wrapper.append(element("div", "rate-number", `${row.percent.toFixed(2)}%`));
    chart.append(wrapper);
  }

  const health = document.getElementById("healthList");
  const errors = validateModel();
  health.replaceChildren();
  if (!errors.length) {
    health.append(element("div", "health-item", "Catalog keys, component IDs, rates, dates, and boost references are valid."));
    health.append(element("div", "health-item", "Viewer inventory is outside this editor and cannot be modified here."));
    health.append(element("div", "health-item", "Live saves create timestamped backups before atomic replacement."));
  } else {
    for (const error of errors.slice(0, 8)) health.append(element("div", "health-item error", error));
  }

  const eventTimeline = document.getElementById("overviewEvents");
  eventTimeline.replaceChildren();
  const events = collections.filter(collection => collection.value.type === "event").sort((a, b) =>
    new Date(a.value.activeFromUtc).getTime() - new Date(b.value.activeFromUtc).getTime());
  for (const event of events) {
    const state = eventState(event.value);
    const row = element("div", "timeline-row");
    const copy = element("div", "timeline-copy");
    copy.append(element("strong", "", event.value.displayName), element("small", "", eventWindowText(event.value)));
    row.append(copy, element("span", `event-status ${state.className}`, state.label));
    row.append(element("span", "timeline-date", eventCountdownText(event.value, state.label)));
    eventTimeline.append(row);
  }
  if (!events.length) eventTimeline.append(element("div", "empty-state", `No event ${systemProfile.collectionPlural} configured.`));

  const economy = document.getElementById("overviewEconomy");
  economy.replaceChildren();
  const economyValues = [
    [`${systemProfile.currencyName.toUpperCase()} IN CIRCULATION`, summary.totalScrap || 0],
    ["UNCLAIMED", totalUnclaimed],
    ["DUPLICATE UNITS", summary.duplicateUnits || 0],
    ["MEDIAN BALANCE", Number(summary.medianScrap || 0).toFixed(1).replace(".0", "")]
  ];
  for (const [label, value] of economyValues) {
    const item = element("div", "pulse-item");
    item.append(element("span", "", label), element("strong", "", String(value)));
    economy.append(item);
  }

  const leaders = document.getElementById("overviewLeaders");
  leaders.replaceChildren();
  const rankedViewers = [...(analytics.viewers || [])].sort((a, b) =>
    Number(b.uniqueComponents) - Number(a.uniqueComponents) ||
    Number(b.completedCollections) - Number(a.completedCollections) ||
    a.displayName.localeCompare(b.displayName)).slice(0, 5);
  rankedViewers.forEach((viewer, index) => {
    const row = element("div", "leader-row");
    row.append(element("span", "leader-rank", `#${index + 1}`));
    row.append(element("strong", "", viewer.displayName));
    row.append(element("span", "collector-score", `${viewer.uniqueComponents} unique / ${viewer.completedCollections} complete`));
    leaders.append(row);
  });
  if (!rankedViewers.length) leaders.append(element("div", "empty-state", "No viewer inventories yet."));

  const healthTable = document.getElementById("overviewCollections");
  healthTable.replaceChildren();
  const header = element("div", "health-table-row header");
  ["COLLECTION", "OWNERS", "DUPES", "SCRAP", "PULL RATE"].forEach(label => header.append(element("span", "", label)));
  healthTable.append(header);
  const rateMap = new Map(rates.rows.map(row => [row.key, row.percent]));
  for (const collection of collections) {
    const metrics = collectionRows.find(row => row.key === collection.key) || {};
    const row = element("div", "health-table-row");
    row.append(element("strong", "", collection.value.displayName));
    row.append(element("span", "", String(metrics.viewerOwners || 0)));
    row.append(element("span", "", String(metrics.duplicateUnits || 0)));
    row.append(element("span", "", String(metrics.currentUnclaimedScrap || 0)));
    const state = eventState(collection.value).label;
    const rateText = rateMap.has(collection.key) ? `${rateMap.get(collection.key).toFixed(2)}%` : state;
    row.append(element("span", "", rateText));
    healthTable.append(row);
  }

  const actionCenter = document.getElementById("actionCenter");
  actionCenter.replaceChildren();
  const actions = errors.slice(0, 3).map(message => ({ title: "Configuration error", message, type: "error" }));
  for (const event of events) {
    const state = eventState(event.value).label;
    if (state === "ENDED" && event.value.enabled) actions.push({ title: `${event.value.displayName} has ended`, message: "Disable it or update its event window.", type: "warning" });
    if (state === "UPCOMING") actions.push({ title: `${event.value.displayName} is upcoming`, message: eventCountdownText(event.value, state), type: "" });
  }
  for (const collection of collections.filter(item => activeForRates(item.value) && Number(item.value.weight) === 0)) {
    actions.push({ title: `${collection.value.displayName} cannot be pulled`, message: "Its active collection weight is zero.", type: "warning" });
  }
  if (boost.enabled) actions.push({ title: "Featured boost is live", message: `${boost.displayName} is modifying effective pull rates.`, type: "" });
  if (Number(roleAwards.summary?.pending || 0) > 0) {
    actions.unshift({
      title: `${roleAwards.summary.pending} Discord role award${roleAwards.summary.pending === 1 ? "" : "s"} pending`,
      message: "Open Discord Roles to review completed collections.",
      type: "warning"
    });
  }
  if (!actions.length) actions.push({ title: "No action required", message: "Configuration, event windows, and active weights look healthy.", type: "good" });
  for (const action of actions.slice(0, 6)) {
    const item = element("div", `action-item ${action.type}`.trim());
    item.append(element("strong", "", action.title), document.createTextNode(action.message));
    actionCenter.append(item);
  }
}

function eventWindowText(value) {
  const start = new Date(value.activeFromUtc);
  const end = new Date(value.activeUntilUtc);
  if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime())) return "Event window needs valid dates";
  const format = { month: "short", day: "numeric", year: "numeric" };
  return `${start.toLocaleDateString(undefined, format)} - ${end.toLocaleDateString(undefined, format)}`;
}

function eventCountdownText(value, state) {
  if (state === "DISABLED") return "Not scheduled to run";
  if (state === "INVALID") return "Check event dates";
  const target = state === "UPCOMING" ? new Date(value.activeFromUtc).getTime() : new Date(value.activeUntilUtc).getTime();
  const days = Math.max(0, Math.ceil(Math.abs(target - Date.now()) / 86400000));
  if (state === "UPCOMING") return `Starts in ${days} day${days === 1 ? "" : "s"}`;
  if (state === "ACTIVE") return `Ends in ${days} day${days === 1 ? "" : "s"}`;
  return `Ended ${days} day${days === 1 ? "" : "s"} ago`;
}

function currentSalvageValue(key, fallback = 0) {
  const collection = collections.find(item => item.key === key);
  return Number(collection?.value.salvageValue ?? fallback) || 0;
}

function renderEconomy() {
  const summary = analytics.summary || {};
  const collectionRows = (analytics.collections || []).map(row => ({
    ...row,
    currentSalvageValue: currentSalvageValue(row.key, row.salvageValue),
    currentUnclaimedScrap: Number(row.duplicateUnits || 0) * currentSalvageValue(row.key, row.salvageValue)
  }));
  const totalUnclaimed = collectionRows.reduce((sum, row) => sum + row.currentUnclaimedScrap, 0);
  const values = [
    ["VIEWERS", summary.viewerCount || 0, "Inventories participating in the system"],
    [`${systemProfile.currencyName.toUpperCase()} CIRCULATION`, summary.totalScrap || 0, `Average ${Number(summary.averageScrap || 0).toFixed(2)} / median ${Number(summary.medianScrap || 0).toFixed(2)}`],
    ["DUPLICATE UNITS", summary.duplicateUnits || 0, "Extra copies currently available"],
    [`UNCLAIMED ${systemProfile.currencyName.toUpperCase()}`, totalUnclaimed, "At current editor salvage values"]
  ];
  const stats = document.getElementById("economyStats");
  stats.replaceChildren();
  for (const [label, value, detail] of values) {
    const node = document.getElementById("statTemplate").content.cloneNode(true);
    node.querySelector(".stat-label").textContent = label;
    node.querySelector(".stat-value").textContent = value;
    node.querySelector(".stat-detail").textContent = detail;
    stats.append(node);
  }

  const table = document.getElementById("collectionEconomy");
  table.replaceChildren();
  const maximum = Math.max(1, ...collectionRows.map(row => row.currentUnclaimedScrap));
  for (const row of collectionRows.sort((a, b) => b.currentUnclaimedScrap - a.currentUnclaimedScrap)) {
    const wrapper = element("div", "economy-row");
    wrapper.append(element("strong", "", row.displayName));
    wrapper.append(element("span", "economy-number", `${row.duplicateUnits} extras`));
    wrapper.append(element("span", "economy-number", `${row.currentUnclaimedScrap} ${systemProfile.currencyName}`));
    const bar = element("div", "economy-bar");
    const fill = document.createElement("span");
    fill.style.width = `${(row.currentUnclaimedScrap / maximum) * 100}%`;
    bar.append(fill);
    wrapper.append(bar);
    table.append(wrapper);
  }
  if (!collectionRows.length) table.append(element("div", "empty-state", `No ${systemProfile.collectionSingular} analytics available.`));

  const leaders = document.getElementById("scrapLeaders");
  leaders.replaceChildren();
  const ranked = [...(analytics.viewers || [])].sort((a, b) => Number(b.scrap) - Number(a.scrap) || a.displayName.localeCompare(b.displayName)).slice(0, 10);
  ranked.forEach((viewer, index) => {
    const row = element("div", "leader-row");
    row.append(element("span", "leader-rank", `#${index + 1}`));
    row.append(element("strong", "", viewer.displayName));
    row.append(element("span", "leader-balance", `${viewer.scrap} ${systemProfile.currencyName}`));
    leaders.append(row);
  });
  if (!ranked.length) leaders.append(element("div", "empty-state", "No viewer balances yet."));
}

function renderViewerInspector() {
  const search = document.getElementById("viewerSearch");
  const query = search.value.trim().toLowerCase();
  const viewerRows = (analytics.viewers || []).filter(viewer =>
    !query || viewer.displayName.toLowerCase().includes(query) || viewer.id.toLowerCase().includes(query)
  );

  const list = document.getElementById("viewerList");
  list.replaceChildren();
  for (const viewer of viewerRows) {
    const button = element("button", `viewer-button ${viewer.id === selectedViewerId ? "active" : ""}`);
    button.append(element("strong", "", viewer.displayName));
    button.addEventListener("click", () => { selectedViewerId = viewer.id; renderViewerInspector(); });
    list.append(button);
  }
  if (!viewerRows.length) list.append(element("div", "empty-state", "No viewers match this search."));

  const selected = (analytics.viewers || []).find(viewer => viewer.id === selectedViewerId);
  renderViewerDetail(selected);
}

function renderViewerDetail(viewer) {
  const detail = document.getElementById("viewerDetail");
  detail.replaceChildren();
  if (!viewer) {
    detail.append(element("div", "empty-state", "Select a viewer to inspect collection progress."));
    return;
  }

  const header = element("div", "viewer-detail-header");
  const identity = element("div");
  identity.append(element("h2", "", viewer.displayName));
  const resetBtn = element("button", "button danger small", "Reset Inventory");
  resetBtn.type = "button";
  resetBtn.addEventListener("click", () => resetViewer(viewer.id, viewer.displayName));
  header.append(identity, resetBtn);
  detail.append(header);

  const viewerUnclaimed = (viewer.collections || []).reduce((sum, collection) =>
    sum + Number(collection.duplicateUnits || 0) * currentSalvageValue(collection.key), 0);
  const metrics = [
    [systemProfile.currencyName.toUpperCase(), viewer.scrap],
    ["UNIQUE PARTS", viewer.uniqueComponents],
    ["DUPLICATES", viewer.duplicateUnits],
    [`AVAILABLE ${systemProfile.currencyName.toUpperCase()}`, viewerUnclaimed]
  ];
  const metricGrid = element("div", "viewer-metrics");
  for (const [label, value] of metrics) {
    const metric = element("div", "viewer-metric");
    metric.append(element("span", "", label), element("strong", "", String(value)));
    metricGrid.append(metric);
  }
  detail.append(metricGrid);

  const collectionList = element("div", "viewer-collections");
  for (const collection of viewer.collections || []) {
    const container = element("details", "viewer-collection");
    const summary = document.createElement("summary");
    const name = element("strong", "viewer-collection-name", collection.displayName);
    if (collection.completionDate) name.append(element("span", "completion-tag", "COMPLETE"));
    summary.append(name);
    summary.append(element("span", "viewer-progress", `${collection.ownedCount}/${collection.totalCount}`));
    summary.append(element("span", "viewer-dupes", `${collection.duplicateUnits} dupes`));
    container.append(summary);

    const parts = element("div", "viewer-parts");
    parts.append(element("div", "empty-state", `Open to inspect ${collection.totalCount} ${systemProfile.itemPlural}.`));
    container.addEventListener("toggle", () => {
      if (!container.open || parts.dataset.loaded === "true") return;
      parts.dataset.loaded = "true";
      parts.replaceChildren();
      for (const part of collection.parts || []) {
        const className = part.quantity > 1 ? "viewer-part duplicate" : part.quantity > 0 ? "viewer-part owned" : "viewer-part";
        const row = element("div", className);
        row.append(element("span", "", part.name), element("strong", "", part.quantity > 0 ? `x${part.quantity}` : "missing"));
        if (part.quantity > 0) {
          const removeBtn = element("button", "viewer-part-remove", "×");
          removeBtn.type = "button";
          removeBtn.title = `Remove ${part.name} from ${viewer.displayName}'s inventory`;
          removeBtn.addEventListener("click", () => removeInventoryItem(viewer.id, viewer.displayName, part.id, part.name));
          row.append(removeBtn);
        }
        parts.append(row);
      }
    });
    container.append(parts);
    collectionList.append(container);
  }
  detail.append(collectionList);
}

function formatRoleDate(value) {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return "Date unavailable";
  return date.toLocaleString(undefined, { month: "short", day: "numeric", year: "numeric", hour: "numeric", minute: "2-digit" });
}

function renderRoleAwards() {
  const summary = roleAwards.summary || {};
  const roleNameCount = Object.values(roleAwards.roleNames || {}).filter(name => String(name).trim()).length;
  const values = [
    ["PENDING", summary.pending || 0, "Discord roles awaiting assignment"],
    ["ASSIGNED", summary.assigned || 0, "Role awards acknowledged"],
    ["COMPLETIONS", summary.total || 0, "Recorded collection completions"],
    ["ROLE MAPPINGS", roleNameCount, "Collections with a Discord role"]
  ];
  const stats = document.getElementById("roleStats");
  stats.replaceChildren();
  for (const [label, value, detail] of values) {
    const node = document.getElementById("statTemplate").content.cloneNode(true);
    node.querySelector(".stat-label").textContent = label;
    node.querySelector(".stat-value").textContent = value;
    node.querySelector(".stat-detail").textContent = detail;
    stats.append(node);
  }

  const badge = document.getElementById("roleNavBadge");
  badge.hidden = !(summary.pending > 0);
  badge.textContent = summary.pending || "";
  document.getElementById("pendingRoleCount").textContent = `${summary.pending || 0} PENDING`;

  renderRoleAwardList("pendingRoleAwards", (roleAwards.awards || []).filter(award => !award.assigned), false);
  renderRoleAwardList("assignedRoleAwards", (roleAwards.awards || []).filter(award => award.assigned), true);

  const settings = document.getElementById("roleNameSettings");
  settings.replaceChildren();
  for (const [key, roleName] of Object.entries(roleAwards.roleNames || {})) {
    const collectionName = collections.find(item => item.key === key)?.value.displayName || key;
    const row = element("label", "role-name-row");
    row.append(element("span", "", collectionName));
    const input = document.createElement("input");
    input.type = "text";
    input.maxLength = 100;
    input.value = roleName;
    input.dataset.collectionKey = key;
    row.append(input);
    settings.append(row);
  }
}

function renderRoleAwardList(targetId, awards, assigned) {
  const list = document.getElementById(targetId);
  list.replaceChildren();
  for (const award of awards) {
    const row = element("div", `role-award-row ${assigned ? "assigned" : ""}`.trim());
    const identity = element("div", "role-identity");
    identity.append(element("strong", "", award.displayName), element("small", "", `Twitch ID ${award.userId}`));
    const earned = element("div", "role-earned");
    earned.append(element("strong", "", award.roleName), element("small", "", award.collectionName));
    const date = element("div", "role-date", assigned ? `Assigned ${formatRoleDate(award.assignedAtUtc)}` : `Completed ${formatRoleDate(award.completedAtUtc)}`);
    const button = element("button", `button ${assigned ? "secondary" : "primary"} small`, assigned ? "Undo" : "Mark Assigned");
    button.addEventListener("click", () => setRoleAssigned(award, !assigned));
    row.append(identity, earned, date, button);
    list.append(row);
  }
  if (!awards.length) {
    list.append(element("div", "empty-state", assigned ? "No role assignments have been acknowledged yet." : "All recorded collection roles are handled."));
  }
}

async function refreshRoleAwards() {
  const response = await fetch("/api/roles", { cache: "no-store" });
  if (!response.ok) throw new Error("Could not refresh Discord role awards.");
  roleAwards = await response.json();
  renderRoleAwards();
  renderOverview();
}

async function setRoleAssigned(award, assigned) {
  const action = assigned ? `mark ${award.displayName}'s ${award.roleName} role assigned` : `return ${award.displayName}'s ${award.roleName} role to the pending queue`;
  if (!window.confirm(`Are you sure you want to ${action}?`)) return;
  try {
    const response = await fetch("/api/roles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "setAssigned", userId: award.userId, collectionKey: award.collectionKey, assigned })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Role award update failed."]).join(" "));
    await refreshRoleAwards();
    await refreshBackupIndex(false);
    showNotice(assigned ? "Discord role marked assigned." : "Role award returned to the pending queue.", "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function saveRoleNames() {
  const roleNames = {};
  for (const input of document.querySelectorAll("#roleNameSettings input")) {
    const value = input.value.trim();
    if (!value) {
      showNotice("Every collection needs a Discord role name.", "error");
      input.focus();
      return;
    }
    roleNames[input.dataset.collectionKey] = value;
  }
  try {
    const response = await fetch("/api/roles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "saveRoleNames", roleNames })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Role name save failed."]).join(" "));
    await refreshRoleAwards();
    await refreshBackupIndex(false);
    showNotice("Discord role names saved with a timestamped backup.", "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function simulationModel() {
  const rates = effectiveRates();
  const parts = [];
  for (const rate of rates.rows) {
    const collection = collections.find(item => item.key === rate.key);
    const collectionParts = collection?.value.parts || [];
    const collectionTiers = collection?.value.tiers || [];
    if (!collectionParts.length || rate.percent <= 0) continue;
    const collectionProb = rate.percent / 100;

    if (collectionTiers.length > 0) {
      const totalTierWeight = collectionTiers.reduce((sum, t) => sum + Math.max(0, Number(t.weight) || 0), 0);
      for (const tier of collectionTiers) {
        const tierWeight = Math.max(0, Number(tier.weight) || 0);
        if (totalTierWeight <= 0 || tierWeight <= 0) continue;
        const tierProb = (tierWeight / totalTierWeight) * collectionProb;
        const tierParts = collectionParts.filter(p => p.tier === tier.id);
        if (!tierParts.length) continue;
        const probability = tierProb / tierParts.length;
        for (const part of tierParts) {
          parts.push({ collectionKey: rate.key, collectionName: rate.name, partName: part.name, probability });
        }
      }
      // Parts without a tier assignment fall back to equal odds across all parts
      const untiered = collectionParts.filter(p => !collectionTiers.some(t => t.id === p.tier));
      if (untiered.length) {
        const probability = collectionProb / collectionParts.length;
        for (const part of untiered) {
          parts.push({ collectionKey: rate.key, collectionName: rate.name, partName: part.name, probability });
        }
      }
    } else {
      const probability = collectionProb / collectionParts.length;
      for (const part of collectionParts) {
        parts.push({ collectionKey: rate.key, collectionName: rate.name, partName: part.name, probability });
      }
    }
  }
  return {
    rates,
    parts,
    signature: JSON.stringify(parts.map(part => [part.collectionKey, part.partName, part.probability]))
  };
}

function formatOneIn(probability) {
  if (!(probability > 0)) return "Unavailable";
  const odds = Math.round(1 / probability);
  return odds <= 1 ? "About 1 in 1" : `About 1 in ${odds.toLocaleString()}`;
}

function renderRateLab() {
  const model = simulationModel();
  const tripleProbability = model.parts.reduce((sum, p) => sum + Math.pow(p.probability, 3), 0);
  const highestPartProbability = Math.max(0, ...model.parts.map(p => p.probability));
  const activeEvents = collections.filter(c => eventState(c.value).label === "ACTIVE").length;

  const boostChip = document.getElementById("ratelabBoostChip");
  if (boostChip) boostChip.textContent = boost.enabled ? "BOOST ACTIVE" : "BASE RATES";

  const stats = document.getElementById("ratelabStats");
  if (stats) {
    stats.replaceChildren();
    const values = [
      ["ACTIVE COLLECTIONS", model.rates.rows.length, `${activeEvents} active event${activeEvents === 1 ? "" : "s"}`],
      ["ACTIVE ITEMS", model.parts.length, `Eligible items in this model`],
      ["HIGHEST ITEM CHANCE", `${(highestPartProbability * 100).toFixed(3)}%`, formatOneIn(highestPartProbability)],
      ["TRIPLE SAME ITEM", formatOneIn(tripleProbability).replace("About ", ""), `${(tripleProbability * 100).toFixed(6)}% per three-pull sequence`]
    ];
    for (const [label, value, detail] of values) {
      const node = document.getElementById("statTemplate").content.cloneNode(true);
      node.querySelector(".stat-label").textContent = label;
      node.querySelector(".stat-value").textContent = value;
      node.querySelector(".stat-detail").textContent = detail;
      stats.append(node);
    }
  }

  const editor = document.getElementById("ratelabWeightEditor");
  if (!editor) return;
  editor.replaceChildren();

  const permanent = collections.filter(c => c.value.type !== "event");
  const events = collections.filter(c => c.value.type === "event");

  if (permanent.length) {
    editor.append(element("div", "weight-section-label", "Permanent"));
    for (const col of permanent) buildWeightRow(col, editor);
  }
  if (events.length) {
    editor.append(element("div", "weight-section-label", "Events"));
    for (const col of events) buildWeightRow(col, editor);
  }
  if (!collections.length) {
    editor.append(element("div", "empty-state", "No collections defined."));
  }

  refreshWeightPercentages();
  renderRatelabSimulation();
  renderRatelabTiers();
}

function renderRatelabTiers() {
  const container = document.getElementById("ratelabTiersContent");
  if (!container) return;
  container.replaceChildren();

  const rates = effectiveRates();
  const collectionsWithTiers = collections.filter(c => {
    const active = rates.rows.find(r => r.key === c.key);
    return active && Array.isArray(c.value.tiers) && c.value.tiers.length > 0;
  });

  if (!collectionsWithTiers.length) {
    container.append(element("div", "tiers-empty-state", "No active collections have tiers configured. Add tiers in the collection editor to see a breakdown here."));
    return;
  }

  for (const col of collectionsWithTiers) {
    const rate = rates.rows.find(r => r.key === col.key);
    const collectionProb = (rate.percent / 100);
    const totalTierWeight = col.value.tiers.reduce((sum, t) => sum + Math.max(0, Number(t.weight) || 0), 0);

    const section = element("div", "tiers-section");
    const sectionLabel = element("div", "weight-section-label tiers-section-label");
    sectionLabel.append(
      element("span", "", col.value.displayName),
      element("span", "tiers-collection-pct", `${rate.percent.toFixed(1)}% of all pulls`)
    );
    section.append(sectionLabel);

    for (const tier of col.value.tiers) {
      const tierWeight = Math.max(0, Number(tier.weight) || 0);
      const tierProb = totalTierWeight > 0 ? (tierWeight / totalTierWeight) * collectionProb : 0;
      const itemsInTier = (col.value.parts || []).filter(p => p.tier === tier.id).length;
      const perItemProb = itemsInTier > 0 ? tierProb / itemsInTier : 0;

      const row = element("div", "tier-stat-row");
      const labelChip = element("span", "tier-stat-label", tier.label || tier.id);
      const itemCount = element("span", "tier-stat-count", `${itemsInTier} item${itemsInTier === 1 ? "" : "s"}`);
      const tierPct = element("span", "tier-stat-pct", `${(tierProb * 100).toFixed(2)}%`);
      const perItem = element("span", "tier-stat-per-item", perItemProb > 0 ? formatOneIn(perItemProb) : "—");
      const bar = element("div", "tier-stat-bar");
      const fill = element("div", "tier-stat-fill");
      fill.style.width = collectionProb > 0 ? `${Math.min(100, (tierProb / collectionProb) * 100).toFixed(1)}%` : "0%";
      bar.append(fill);

      row.append(labelChip, itemCount, tierPct, bar, perItem);
      section.append(row);
    }

    container.append(section);
  }
}

function buildWeightRow(col, container) {
  const isEvent = col.value.type === "event";
  const state = isEvent ? eventState(col.value) : null;

  const row = element("div", "weight-row");
  row.dataset.collectionKey = col.key;

  const nameWrap = element("div", "weight-row-name");
  nameWrap.append(element("span", "", col.value.displayName));
  if (isEvent && state) {
    nameWrap.append(element("span", `event-status ${state.className}`, state.label));
  }

  const maxWeight = Math.max(0, ...collections.map(c => Number(c.value.weight) || 0));
  const sliderMax = Math.max(50, Math.ceil((maxWeight * 1.5) / 10) * 10);
  const slider = document.createElement("input");
  slider.type = "range";
  slider.min = "0";
  slider.max = String(sliderMax);
  slider.step = "1";
  slider.className = "rate-slider";
  slider.title = "Drag to set the pull weight";
  slider.value = String(Math.max(0, Number(col.value.weight) || 0));
  const paintSlider = () => {
    const ratio = sliderMax > 0 ? Math.min(1, Math.max(0, Number(slider.value) / sliderMax)) : 0;
    slider.style.setProperty("--fill", ratio);
  };
  paintSlider();
  slider.addEventListener("input", () => {
    col.value.weight = Math.max(0, Number(slider.value) || 0);
    paintSlider();
    markDirty();
    refreshWeightPercentages();
  });

  const pctLabel = element("span", "weight-pct", "0%");

  row.append(nameWrap, slider, pctLabel);
  container.append(row);
}

function refreshWeightPercentages() {
  const rates = effectiveRates();
  const editor = document.getElementById("ratelabWeightEditor");
  if (!editor) return;
  for (const row of editor.querySelectorAll(".weight-row[data-collection-key]")) {
    const key = row.dataset.collectionKey;
    const rate = rates.rows.find(r => r.key === key);
    const pct = rate ? rate.percent : 0;
    const pctLabel = row.querySelector(".weight-pct");
    if (pctLabel) pctLabel.textContent = `${pct.toFixed(1)}%`;
  }
  if (lastSimulation) {
    const model = simulationModel();
    if (lastSimulation.signature !== model.signature) {
      lastSimulation = null;
      renderRatelabSimulation();
    }
  }
}

// Updates the Overview rate bars + percentages in place (no row rebuild, so the weight
// input keeps focus while typing).
function refreshOverviewRates() {
  const chart = document.getElementById("rateChart");
  if (!chart) return;
  const rates = effectiveRates();
  for (const wrapper of chart.querySelectorAll(".rate-row[data-collection-key]")) {
    const rate = rates.rows.find(r => r.key === wrapper.dataset.collectionKey);
    const pct = rate ? rate.percent : 0;
    const number = wrapper.querySelector(".rate-number");
    if (number) number.textContent = `${pct.toFixed(2)}%`;
  }
}

function renderRatelabSimulation() {
  const results = document.getElementById("ratelabSimResults");
  const status = document.getElementById("ratelabSimStatus");
  if (!results || !status) return;
  results.replaceChildren();
  const rates = effectiveRates();
  if (!lastSimulation) {
    status.textContent = "READY";
    results.append(element("div", "empty-state", "Run the check to compare simulated results against the pull model."));
    return;
  }
  status.textContent = `${lastSimulation.count.toLocaleString()} RUNS`;
  for (const rate of rates.rows) {
    const observedCount = lastSimulation.collectionCounts[rate.key] || 0;
    const observedPercent = lastSimulation.count ? (observedCount / lastSimulation.count) * 100 : 0;
    const row = element("div", "simulation-row");
    row.append(element("strong", "", rate.name));
    row.append(element("span", "", `${rate.percent.toFixed(2)}% expected`));
    row.append(element("span", "", `${observedCount.toLocaleString()} rolled`));
    const bar = element("div", "simulation-bar");
    const fill = document.createElement("span");
    fill.style.width = `${Math.min(100, observedPercent)}%`;
    bar.append(fill);
    row.append(bar);
    results.append(row);
  }
}

function runRatelabSim() {
  const input = document.getElementById("ratelabSimCount");
  const count = Math.max(100, Math.min(100000, Math.round(Number(input.value) || 10000)));
  input.value = count;
  const model = simulationModel();
  if (!model.parts.length) {
    showNotice("Simulation needs at least one active collection with positive weight and items.", "error");
    return;
  }
  const cumulative = [];
  let runningTotal = 0;
  for (const part of model.parts) {
    runningTotal += part.probability;
    cumulative.push({ ...part, threshold: runningTotal });
  }
  const collectionCounts = {};
  for (let i = 0; i < count; i++) {
    const roll = Math.random();
    const selected = cumulative.find(p => roll <= p.threshold) || cumulative[cumulative.length - 1];
    collectionCounts[selected.collectionKey] = (collectionCounts[selected.collectionKey] || 0) + 1;
  }
  lastSimulation = { signature: model.signature, count, collectionCounts };
  clearNotice();
  renderRatelabSimulation();
}

function patchNoteSections() {
  if (!baselineModel) return [];
  const beforeCollections = baselineModel.components?.collections || {};
  const afterModel = serializeModel();
  const afterCollections = afterModel.components?.collections || {};
  const sections = new Map();
  const add = (section, message) => {
    if (!sections.has(section)) sections.set(section, []);
    sections.get(section).push(message);
  };

  for (const [key, value] of Object.entries(afterCollections)) {
    if (!beforeCollections[key]) {
      const section = value.type === "event" ? "Events" : "New Collections";
      add(section, `Added **${value.displayName}** with ${(value.parts || []).length} components.`);
    }
  }
  for (const [key, value] of Object.entries(beforeCollections)) {
    if (!afterCollections[key]) add("Removed", `Removed **${value.displayName || key}**.`);
  }

  for (const [key, after] of Object.entries(afterCollections)) {
    const before = beforeCollections[key];
    if (!before) continue;
    const name = after.displayName || key;
    if (before.displayName !== after.displayName) add("Collection Changes", `Renamed **${before.displayName || key}** to **${name}**.`);
    if (Number(before.weight) !== Number(after.weight)) add("Balance Changes", `**${name}** pull weight: ${before.weight} -> ${after.weight}.`);
    if (Number(before.salvageValue) !== Number(after.salvageValue)) add(`${systemProfile.currencyName} Economy`, `**${name}** salvage value: ${before.salvageValue} -> ${after.salvageValue} ${systemProfile.currencyName}.`);
    if ((before.rareLabel || "") !== (after.rareLabel || "")) {
      add("Collection Changes", `**${name}** rare label: ${before.rareLabel || "none"} -> ${after.rareLabel || "none"}.`);
    }

    const beforeParts = new Map((before.parts || []).map(part => [part.id, part]));
    const afterParts = new Map((after.parts || []).map(part => [part.id, part]));
    for (const [partId, part] of afterParts) {
      if (!beforeParts.has(partId)) add("Component Changes", `Added **${part.name}** to ${name}.`);
      else if (beforeParts.get(partId).name !== part.name) add("Component Changes", `Renamed **${beforeParts.get(partId).name}** to **${part.name}** in ${name}.`);
    }
    for (const [partId, part] of beforeParts) {
      if (!afterParts.has(partId)) add("Component Changes", `Removed **${part.name}** from ${name}.`);
    }

    const beforeTiers = new Map((before.tiers || []).map(t => [t.id, t]));
    const afterTiers = new Map((after.tiers || []).map(t => [t.id, t]));
    for (const [tid, t] of afterTiers) {
      if (!beforeTiers.has(tid)) add("Tier Changes", `Added **${t.label}** tier to ${name} (weight ${t.weight}).`);
      else {
        const bt = beforeTiers.get(tid);
        if (bt.label !== t.label) add("Tier Changes", `Renamed ${name} tier **${bt.label}** to **${t.label}**.`);
        if (Number(bt.weight) !== Number(t.weight)) add("Tier Changes", `**${t.label}** tier weight in ${name}: ${bt.weight} -> ${t.weight}.`);
      }
    }
    for (const [tid, t] of beforeTiers) {
      if (!afterTiers.has(tid)) add("Tier Changes", `Removed **${t.label}** tier from ${name}.`);
    }

    const beforeVariants = new Map((before.variants || []).map(v => [v.id, v]));
    const afterVariants = new Map((after.variants || []).map(v => [v.id, v]));
    for (const [vid, v] of afterVariants) {
      if (!beforeVariants.has(vid)) add("Variant Changes", `Added **${v.label}** variant to ${name} (${(v.chance * 100).toFixed(2)}% chance).`);
      else {
        const bv = beforeVariants.get(vid);
        if (bv.label !== v.label) add("Variant Changes", `Renamed ${name} variant **${bv.label}** to **${v.label}**.`);
        if (Number(bv.chance) !== Number(v.chance)) add("Variant Changes", `**${v.label}** variant in ${name}: ${(bv.chance * 100).toFixed(2)}% -> ${(v.chance * 100).toFixed(2)}%.`);
      }
    }
    for (const [vid, v] of beforeVariants) {
      if (!afterVariants.has(vid)) add("Variant Changes", `Removed **${v.label}** variant from ${name}.`);
    }

    if (after.type === "event") {
      if (Boolean(before.enabled) !== Boolean(after.enabled)) add("Events", `**${name}** is now ${after.enabled ? "enabled" : "disabled"}.`);
      if (before.activeFromUtc !== after.activeFromUtc || before.activeUntilUtc !== after.activeUntilUtc) {
        add("Events", `Updated the event window for **${name}** to ${patchDateRange(after.activeFromUtc, after.activeUntilUtc)}.`);
      }
    }
  }

  const beforeBoost = baselineModel.boost || {};
  const afterBoost = afterModel.boost || {};
  if (Boolean(beforeBoost.enabled) !== Boolean(afterBoost.enabled)) {
    add("Featured Boost", `Featured boost is now **${afterBoost.enabled ? "enabled" : "disabled"}**.`);
  }
  if ((beforeBoost.displayName || "") !== (afterBoost.displayName || "")) {
    add("Featured Boost", `Boost name: **${beforeBoost.displayName || "none"}** -> **${afterBoost.displayName || "none"}**.`);
  }
  const multiplierKeys = new Set([
    ...Object.keys(beforeBoost.collectionMultipliers || {}),
    ...Object.keys(afterBoost.collectionMultipliers || {})
  ]);
  for (const key of multiplierKeys) {
    const beforeValue = Number(beforeBoost.collectionMultipliers?.[key] ?? 1);
    const afterValue = Number(afterBoost.collectionMultipliers?.[key] ?? 1);
    if (beforeValue === afterValue) continue;
    const name = afterCollections[key]?.displayName || beforeCollections[key]?.displayName || key;
    add("Featured Boost", `**${name}** multiplier: ${beforeValue}x -> ${afterValue}x.`);
  }

  return [...sections.entries()].map(([title, items]) => ({ title, items }));
}

function patchDateRange(startValue, endValue) {
  const start = new Date(startValue);
  const end = new Date(endValue);
  if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime())) return "an updated schedule";
  const options = { month: "short", day: "numeric", year: "numeric" };
  return `${start.toLocaleDateString(undefined, options)} - ${end.toLocaleDateString(undefined, options)}`;
}

function renderPatchNotes() {
  const sections = patchNoteSections();
  const title = document.getElementById("patchTitle").value.trim() || "Circuit Components Update";
  const intro = document.getElementById("patchIntro").value.trim();
  const extra = document.getElementById("patchExtra").value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
  const lines = [`# ${title}`, `_${new Date().toLocaleDateString(undefined, { month: "long", day: "numeric", year: "numeric" })}_`];
  if (intro) lines.push("", intro);
  for (const section of sections) {
    lines.push("", `## ${section.title}`);
    for (const item of section.items) lines.push(`- ${item}`);
  }
  if (extra.length) {
    lines.push("", "## Additional Notes");
    for (const item of extra) lines.push(`- ${item}`);
  }
  if (!sections.length && !extra.length) lines.push("", "_No editor changes detected yet._");

  const output = lines.join("\
");
  document.getElementById("patchOutput").value = output;
  const changeCount = sections.reduce((sum, section) => sum + section.items.length, 0) + extra.length;
  document.getElementById("patchChangeCount").textContent = `${changeCount} CHANGE${changeCount === 1 ? "" : "S"}`;
  const counter = document.getElementById("patchCharacterCount");
  counter.textContent = `${output.length.toLocaleString()} / 2000`;
  counter.classList.toggle("warning", output.length > 2000);
}

async function copyPatchNotes() {
  const output = document.getElementById("patchOutput");
  try {
    await navigator.clipboard.writeText(output.value);
  } catch {
    output.select();
    if (!document.execCommand("copy")) throw new Error("Clipboard access was unavailable.");
    output.setSelectionRange(0, 0);
  }
  showNotice("Patch notes copied. They are ready to paste into Discord.", "success");
}

function markPatchPublished() {
  const detail = dirty ? " The current editor also contains unsaved changes." : "";
  if (!window.confirm(`Start a new patch note cycle from the current editor values?${detail}`)) return;
  baselineModel = clone(serializeModel());
  document.getElementById("patchIntro").value = "";
  document.getElementById("patchExtra").value = "";
  renderPatchNotes();
  showNotice("Patch comparison point updated. New edits will appear in the next draft.", "success");
}

function formatBytes(value) {
  const bytes = Number(value) || 0;
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1048576).toFixed(1)} MB`;
}

function stableValue(value) {
  if (Array.isArray(value)) return value.map(stableValue);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, stableValue(value[key])]));
  }
  return value;
}

function sameJson(left, right) {
  return JSON.stringify(stableValue(left)) === JSON.stringify(stableValue(right));
}

function backupDifferenceSummary(preview) {
  if (!preview?.file) return [];
  const backup = preview.content || {};
  const live = preview.liveContent;
  if (live === null || live === undefined) return ["The live file does not exist. Restore will create it."];
  if (sameJson(backup, live)) return ["This backup currently matches the live file."];
  const messages = [];
  if (preview.file.targetKey === "components") {
    const backupCollections = backup.collections || {};
    const liveCollections = live.collections || {};
    const backupKeys = Object.keys(backupCollections);
    const liveKeys = Object.keys(liveCollections);
    const added = backupKeys.filter(key => !liveCollections[key]);
    const removed = liveKeys.filter(key => !backupCollections[key]);
    const changed = backupKeys.filter(key => liveCollections[key] && !sameJson(backupCollections[key], liveCollections[key]));
    if (added.length) messages.push(`Restore adds ${added.length} collection${added.length === 1 ? "" : "s"}: ${added.join(", ")}.`);
    if (removed.length) messages.push(`Restore removes ${removed.length} current collection${removed.length === 1 ? "" : "s"}: ${removed.join(", ")}.`);
    if (changed.length) messages.push(`Restore changes ${changed.length} collection${changed.length === 1 ? "" : "s"}: ${changed.join(", ")}.`);
  } else if (preview.file.targetKey === "boost") {
    if (Boolean(backup.enabled) !== Boolean(live.enabled)) messages.push(`Featured boost becomes ${backup.enabled ? "enabled" : "disabled"}.`);
    if (backup.displayName !== live.displayName) messages.push(`Boost name changes to "${backup.displayName || ""}".`);
    const keys = new Set([...Object.keys(backup.collectionMultipliers || {}), ...Object.keys(live.collectionMultipliers || {})]);
    const changed = [...keys].filter(key => Number(backup.collectionMultipliers?.[key] ?? 1) !== Number(live.collectionMultipliers?.[key] ?? 1));
    if (changed.length) messages.push(`Restore changes ${changed.length} collection multiplier${changed.length === 1 ? "" : "s"}.`);
  } else if (preview.file.targetKey === "roles") {
    const backupAssigned = Object.keys(backup.fulfilled || {}).length;
    const liveAssigned = Object.keys(live.fulfilled || {}).length;
    const roleKeys = new Set([...Object.keys(backup.roleNames || {}), ...Object.keys(live.roleNames || {})]);
    const changedNames = [...roleKeys].filter(key => backup.roleNames?.[key] !== live.roleNames?.[key]);
    if (backupAssigned !== liveAssigned) messages.push(`Assigned-role acknowledgements change from ${liveAssigned} to ${backupAssigned}.`);
    if (changedNames.length) messages.push(`Restore changes ${changedNames.length} Discord role name mapping${changedNames.length === 1 ? "" : "s"}.`);
  } else if (preview.file.targetKey === "profile") {
    const fields = ["gameName", "adminName", "brandKicker", "itemSingular", "itemPlural", "collectionSingular", "collectionPlural", "currencyName", "redemptionName"];
    const changedFields = fields.filter(key => backup[key] !== live[key]);
    const colorKeys = new Set([...Object.keys(backup.colors || {}), ...Object.keys(live.colors || {})]);
    const changedColors = [...colorKeys].filter(key => backup.colors?.[key] !== live.colors?.[key]);
    const commandKeys = new Set([...Object.keys(backup.commands || {}), ...Object.keys(live.commands || {})]);
    const changedCommands = [...commandKeys].filter(key => backup.commands?.[key] !== live.commands?.[key]);
    const messageKeys = new Set([...Object.keys(backup.messages || {}), ...Object.keys(live.messages || {})]);
    const changedMessages = [...messageKeys].filter(key => backup.messages?.[key] !== live.messages?.[key]);
    if (changedFields.length) messages.push(`Restore changes ${changedFields.length} branding or terminology field${changedFields.length === 1 ? "" : "s"}.`);
    if (changedColors.length) messages.push(`Restore changes ${changedColors.length} theme color${changedColors.length === 1 ? "" : "s"}.`);
    if (changedCommands.length) messages.push(`Restore changes ${changedCommands.length} chat command${changedCommands.length === 1 ? "" : "s"}.`);
    if (changedMessages.length) messages.push(`Restore changes ${changedMessages.length} chat message template${changedMessages.length === 1 ? "" : "s"}.`);
  }
  if (!messages.length) messages.push("The backup differs from live configuration in stored values or ordering.");
  return messages;
}

function renderBackups() {
  const backups = backupCenter.backups || [];
  const totalSize = backups.reduce((sum, backup) => sum + Number(backup.size || 0), 0);
  const latest = backups[0];
  const liveCount = (backupCenter.liveFiles || []).filter(file => file.exists).length;
  const values = [
    ["BACKUPS", backups.length, "Managed recovery points"],
    ["STORAGE", formatBytes(totalSize), backupCenter.backupPath || "Backup folder"],
    ["LATEST", latest ? formatRoleDate(latest.createdAtUtc).split(",")[0] : "NONE", latest ? latest.targetLabel : "No backups created yet"],
    ["LIVE FILES", `${liveCount}/${(backupCenter.liveFiles || []).length}`, "Inventory excluded from recovery"]
  ];
  const stats = document.getElementById("backupStats");
  stats.replaceChildren();
  for (const [label, value, detail] of values) {
    const node = document.getElementById("statTemplate").content.cloneNode(true);
    node.querySelector(".stat-label").textContent = label;
    node.querySelector(".stat-value").textContent = value;
    node.querySelector(".stat-detail").textContent = detail;
    stats.append(node);
  }

  const liveFiles = document.getElementById("backupLiveFiles");
  if (liveFiles) {
    liveFiles.replaceChildren();
    for (const file of backupCenter.liveFiles || []) {
      const row = element("div", `backup-live-file ${file.exists ? "exists" : "missing"}`.trim());
      row.append(element("strong", "", file.label || file.key || "Managed file"));
      const detail = file.exists
        ? `${formatBytes(file.size || 0)} - modified ${formatRoleDate(file.modifiedAtUtc)}`
        : "Not created yet";
      row.append(element("small", "", detail));
      row.append(element("span", "", file.exists ? "LIVE" : "MISSING"));
      liveFiles.append(row);
    }
    if (!liveFiles.children.length) liveFiles.append(element("div", "empty-state", "No files reported yet. Save your configuration to register tracked files."));
  }

  const filter = document.getElementById("backupFilter").value;
  const filtered = backups.filter(backup => filter === "all" || backup.targetKey === filter);
  const list = document.getElementById("backupList");
  list.replaceChildren();
  for (const backup of filtered) {
    const button = element("button", `backup-button ${backup.fileName === selectedBackupFile ? "active" : ""}`.trim());
    button.append(element("strong", "", backup.targetLabel));
    button.append(element("small", "", formatRoleDate(backup.createdAtUtc)));
    button.append(element("span", "", formatBytes(backup.size)));
    button.addEventListener("click", () => selectBackup(backup.fileName));
    list.append(button);
  }
  if (!filtered.length) list.append(element("div", "empty-state", backups.length ? "No backups match this filter." : "No backups yet. Save your configuration to create the first backup."));
  renderBackupPreview();
}

function renderBackupPreview() {
  const preview = selectedBackupPreview;
  const title = document.getElementById("backupPreviewTitle");
  const badge = document.getElementById("backupValidationBadge");
  const meta = document.getElementById("backupPreviewMeta");
  const difference = document.getElementById("backupDifference");
  const validation = document.getElementById("backupValidation");
  const json = document.getElementById("backupJson");
  const download = document.getElementById("downloadBackupButton");
  const restore = document.getElementById("restoreBackupButton");
  meta.replaceChildren();
  difference.replaceChildren();
  validation.replaceChildren();
  if (!preview?.file) {
    title.textContent = "Select a Backup";
    badge.textContent = "NO FILE";
    badge.className = "metric-chip";
    json.textContent = "Choose a backup from the history to inspect its contents.";
    download.disabled = true;
    restore.disabled = true;
    return;
  }

  title.textContent = preview.file.targetLabel;
  const errors = preview.validationErrors || [];
  badge.textContent = errors.length ? "INVALID" : "VALID";
  badge.className = `metric-chip ${errors.length ? "invalid" : "valid"}`;
  meta.append(element("span", "", preview.file.fileName));
  meta.append(element("span", "", formatRoleDate(preview.file.createdAtUtc)));
  meta.append(element("span", "", formatBytes(preview.file.size)));
  for (const message of backupDifferenceSummary(preview)) difference.append(element("div", "backup-difference-item", message));
  if (errors.length) {
    for (const error of errors) validation.append(element("div", "health-item error", error));
  } else {
    validation.append(element("div", "health-item", "JSON structure and related live configuration references are valid."));
  }
  json.textContent = JSON.stringify(preview.content, null, 2);
  download.disabled = false;
  restore.disabled = errors.length > 0;
}

async function selectBackup(fileName) {
  selectedBackupFile = fileName;
  selectedBackupPreview = null;
  renderBackups();
  document.getElementById("backupPreviewTitle").textContent = "Loading Backup...";
  try {
    const response = await fetch("/api/backups", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "preview", fileName })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Backup preview failed."]).join(" "));
    if (selectedBackupFile !== fileName) return;
    selectedBackupPreview = result;
    renderBackups();
  } catch (error) {
    showNotice(error.message, "error");
  }
}

async function refreshBackupIndex(showMessage = true) {
  const response = await fetch("/api/backups", { cache: "no-store" });
  if (!response.ok) throw new Error("Could not refresh backup history.");
  backupCenter = await response.json();
  if (selectedBackupFile && !backupCenter.backups.some(item => item.fileName === selectedBackupFile)) {
    selectedBackupFile = "";
    selectedBackupPreview = null;
  }
  renderBackups();
  if (showMessage) showNotice("Backup history refreshed.", "success");
}

function downloadSelectedBackup() {
  if (!selectedBackupPreview?.file) return;
  const blob = new Blob([JSON.stringify(selectedBackupPreview.content, null, 2)], { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = selectedBackupPreview.file.fileName;
  link.click();
  URL.revokeObjectURL(link.href);
}

async function restoreSelectedBackup() {
  const preview = selectedBackupPreview;
  if (!preview?.file || preview.validationErrors?.length) return;
  const unsavedWarning = dirty ? " Unsaved editor changes will be discarded." : "";
  if (!window.confirm(`Restore ${preview.file.fileName} over the live ${preview.file.targetLabel}? A new pre-restore backup will be created.${unsavedWarning}`)) return;
  try {
    const response = await fetch("/api/backups", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ operation: "restore", fileName: preview.file.fileName })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Backup restore failed."]).join(" "));
    selectedBackupFile = "";
    selectedBackupPreview = null;
    await loadConfiguration(true);
    switchView("backups");
    showNotice(`${result.target} restored. The previous live file was backed up first.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function element(tag, className = "", text = "") {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== "") node.textContent = text;
  return node;
}

// Runs a chat command against the editing profile's saved data via the runtime dispatch, as a
// sandbox viewer (no live data is changed) — lets the streamer preview command output without Twitch.
async function runCommandTest() {
  const input = document.getElementById("commandTestInput");
  const output = document.getElementById("commandTestOutput");
  const raw = input.value.trim();
  if (!raw) { input.focus(); return; }
  const [word, ...rest] = raw.replace(/^!/, "").split(/\s+/);
  output.hidden = false;
  output.className = "command-test-output";
  output.textContent = "Running…";
  try {
    const response = await fetch("/api/runtime/action", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        action: "command",
        profileId: profilesData.activeProfileId,
        command: word,
        arg: rest.join(" "),
        viewerId: "__command_test__",
        viewerName: "Command Tester"
      })
    });
    const result = await response.json();
    if (!response.ok || result.ok === false) {
      output.classList.add("error");
      output.textContent = (result.errors || [result.error || "Command failed."]).join(" ");
      return;
    }
    const lines = Array.isArray(result.messages) ? result.messages : [];
    output.textContent = lines.length ? lines.join("\n") : "(no output for this command)";
  } catch (error) {
    output.classList.add("error");
    output.textContent = error.message;
  }
}

function makeField(labelText, type, value, onInput, options = {}) {
  const label = element("label", "field");
  label.append(element("span", "", labelText));
  const input = document.createElement("input");
  input.type = type;
  input.value = value ?? "";
  if (options.min !== undefined) input.min = options.min;
  if (options.step !== undefined) input.step = options.step;
  if (options.maxLength) input.maxLength = options.maxLength;
  input.addEventListener("input", () => { onInput(input.value, input); markDirty(); renderOverview(); });
  label.append(input);
  return label;
}

function renderCollectionList(type) {
  const container = document.getElementById(type === "event" ? "eventCollections" : "permanentCollections");
  container.replaceChildren();
  const allMatching = collections.filter(collection => collection.value.type === type);
  const query = type === "permanent" ? document.getElementById("collectionSearch").value.trim().toLowerCase() : "";
  const matching = allMatching.filter(collection => {
    if (!query) return true;
    const collectionText = `${collection.key} ${collection.value.displayName}`.toLowerCase();
    return collectionText.includes(query) || (collection.value.parts || []).some(part =>
      `${part.id} ${part.name}`.toLowerCase().includes(query)
    );
  });
  if (type === "permanent") {
    const itemCount = matching.reduce((sum, collection) => {
      if (!query || `${collection.key} ${collection.value.displayName}`.toLowerCase().includes(query)) {
        return sum + (collection.value.parts?.length || 0);
      }
      return sum + (collection.value.parts || []).filter(part => `${part.id} ${part.name}`.toLowerCase().includes(query)).length;
    }, 0);
    document.getElementById("collectionListSummary").textContent = query
      ? `${matching.length} of ${allMatching.length} ${systemProfile.collectionPlural} | ${itemCount} matching ${itemCount === 1 ? systemProfile.itemSingular : systemProfile.itemPlural}`
      : `${allMatching.length} ${systemProfile.collectionPlural} | ${itemCount} ${systemProfile.itemPlural}`;
  }
  if (!matching.length) {
    const message = type === "event"
      ? "No events configured."
      : query ? `No ${systemProfile.collectionPlural} or ${systemProfile.itemPlural} match this search.` : `No permanent ${systemProfile.collectionPlural} configured.`;
    container.append(element("article", "panel", message));
    return;
  }
  for (const collection of matching) container.append(buildCollectionCard(collection, query));
}

function buildCollectionCard(collection, query = "") {
  const { value } = collection;
  const expanded = expandedCollectionKeys.has(collection.key);
  const card = element("article", `collection-card ${expanded ? "expanded" : ""}`.trim());
  const heading = element("div", "collection-heading");
  const toggle = element("button", "collection-toggle");
  toggle.type = "button";
  toggle.setAttribute("aria-expanded", String(expanded));
  const toggleCopy = element("div", "collection-toggle-copy");
  const titleRow = element("div", "collection-title-row");
  titleRow.append(element("h3", "collection-title", value.displayName || collection.key));
  titleRow.append(element("span", `type-badge ${value.type === "event" ? "event" : ""}`, value.type.toUpperCase()));
  if (value.type === "event") {
    const state = eventState(value);
    titleRow.append(element("span", `event-status ${state.className}`, state.label));
  }
  toggleCopy.append(titleRow);
  const summary = element("div", "collection-summary-meta");
  summary.append(
    element("span", "", `${value.parts?.length || 0} ${systemProfile.itemPlural}`),
    element("span", "", `weight ${value.weight}`)
  );
  if (query) {
    const matchCount = (value.parts || []).filter(part => `${part.id} ${part.name}`.toLowerCase().includes(query)).length;
    if (matchCount) summary.append(element("span", "", `${matchCount} item match${matchCount === 1 ? "" : "es"}`));
  }
  summary.append(element("i", "collection-chevron"));
  toggle.append(toggleCopy, summary);
  toggle.addEventListener("click", () => {
    if (expandedCollectionKeys.has(collection.key)) expandedCollectionKeys.delete(collection.key);
    else expandedCollectionKeys.add(collection.key);
    renderCollectionList(value.type);
  });
  heading.append(toggle);
  const remove = element("button", "button danger small", value.type === "event" ? "Delete Event" : "Delete");
  remove.addEventListener("click", () => {
    if (value.type !== "event" && collections.filter(item => item.value.type !== "event").length <= 1) {
      window.alert(`You need at least one ${systemProfile.collectionSingular}. Add another before deleting this one.`);
      return;
    }
    const label = value.type === "event" ? "event" : systemProfile.collectionSingular;
    if (!window.confirm(`Delete ${label} '${value.displayName || collection.key}' from the catalog? Viewer inventory is not deleted.`)) return;
    collections = collections.filter(item => item !== collection);
    expandedCollectionKeys.delete(collection.key);
    delete boost.collectionMultipliers[collection.key];
    markDirty();
    renderAll();
  });
  heading.append(remove);
  card.append(heading);
  if (!expanded) return card;

  const body = element("div", "collection-body");
  const grid = element("div", "field-grid");
  grid.append(
    makeField("Display name", "text", value.displayName, next => { value.displayName = next; }),
    makeField("Pull weight", "number", value.weight, next => { value.weight = Number(next); }, { min: 0, step: 0.1 }),
    makeField(`${systemProfile.currencyName} value`, "number", value.salvageValue, next => { value.salvageValue = Number(next); }, { min: 1, step: 1 }),
    makeField("Rare label (optional)", "text", value.rareLabel || "", next => { value.rareLabel = next; })
  );
  body.append(grid);

  if (value.type === "event") {
    const eventGrid = element("div", "event-grid");
    const enabledLabel = element("label", "field");
    enabledLabel.append(element("span", "", "Event state"));
    const toggle = element("label", "toggle");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = Boolean(value.enabled);
    checkbox.addEventListener("change", () => { value.enabled = checkbox.checked; markDirty(); renderAll(); });
    toggle.append(checkbox, document.createElement("span"), document.createTextNode("Enabled"));
    enabledLabel.append(toggle);
    eventGrid.append(
      enabledLabel,
      makeField("Starts (local time)", "datetime-local", toLocalDateTime(value.activeFromUtc), next => { value.activeFromUtc = fromLocalDateTime(next); }),
      makeField("Ends (local time)", "datetime-local", toLocalDateTime(value.activeUntilUtc), next => { value.activeUntilUtc = fromLocalDateTime(next); })
    );
    body.append(eventGrid);
  }

  const collectionTextMatches = !query || `${collection.key} ${value.displayName}`.toLowerCase().includes(query);
  const visibleParts = value.parts.map((part, index) => ({ part, index })).filter(({ part }) =>
    collectionTextMatches || `${part.id} ${part.name}`.toLowerCase().includes(query)
  );
  const partsHeader = element("div", "parts-header");
  const itemCount = visibleParts.length === value.parts.length ? value.parts.length : `${visibleParts.length} of ${value.parts.length}`;
  partsHeader.append(element("div", "subsection-title", `${titleCase(systemProfile.itemPlural)} (${itemCount})`));
  const addPart = element("button", "button secondary small", `Add ${titleCase(systemProfile.itemSingular)}`);
  addPart.addEventListener("click", () => {
    // The id is hidden/auto-generated, so make it unique up front (it's the stable inventory key).
    const existing = new Set(value.parts.map(p => p.id));
    let n = value.parts.length + 1;
    let id = `${collection.key}_item_${n}`;
    while (existing.has(id)) { n++; id = `${collection.key}_item_${n}`; }
    value.parts.push({ id, name: `New ${titleCase(systemProfile.itemSingular)}` });
    markDirty();
    renderAll();
  });
  partsHeader.append(addPart);
  body.append(partsHeader);

  const hasTiers = Array.isArray(value.tiers) && value.tiers.length > 0;
  if (hasTiers) {
    const bulkRow = element("div", "bulk-assign-row");
    bulkRow.append(element("span", "bulk-assign-label", "Bulk assign:"));
    const tierSelect = document.createElement("select");
    tierSelect.className = "bulk-assign-select";
    for (const t of value.tiers) {
      const opt = document.createElement("option");
      opt.value = t.id;
      opt.textContent = t.label || t.id;
      tierSelect.append(opt);
    }
    const assignAll = element("button", "button secondary small", "Assign all");
    assignAll.type = "button";
    assignAll.addEventListener("click", () => {
      const tid = tierSelect.value;
      for (const part of value.parts) part.tier = tid;
      markDirty();
      renderAll();
    });
    const assignUnassigned = element("button", "button secondary small", "Assign unassigned");
    assignUnassigned.type = "button";
    assignUnassigned.addEventListener("click", () => {
      const tid = tierSelect.value;
      const validIds = new Set(value.tiers.map(t => t.id));
      for (const part of value.parts) {
        if (!part.tier || !validIds.has(part.tier)) part.tier = tid;
      }
      markDirty();
      renderAll();
    });
    bulkRow.append(tierSelect, assignAll, assignUnassigned);
    body.append(bulkRow);
  }

  const list = element("div", "parts-list parts-list-scroll");
  visibleParts.forEach(({ part, index }) => {
    const row = element("div", hasTiers ? "part-row part-row-tiered" : "part-row");
    row.append(
      makeField("Display name", "text", part.name, next => { part.name = next; })
    );
    if (hasTiers) {
      const tierField = element("div", "field");
      tierField.append(element("span", "", "Tier"));
      const tierSelect = document.createElement("select");
      tierSelect.append(Object.assign(document.createElement("option"), { value: "", textContent: "— unassigned —" }));
      for (const t of value.tiers) {
        const opt = document.createElement("option");
        opt.value = t.id;
        opt.textContent = t.label || t.id;
        if (part.tier === t.id) opt.selected = true;
        tierSelect.append(opt);
      }
      tierSelect.addEventListener("change", () => {
        part.tier = tierSelect.value || undefined;
        markDirty();
      });
      tierField.append(tierSelect);
      row.append(tierField);
    }
    const remove = element("button", "button danger small", "Remove");
    remove.addEventListener("click", () => { value.parts.splice(index, 1); markDirty(); renderAll(); });
    row.append(remove);
    list.append(row);
  });
  if (!visibleParts.length) list.append(element("div", "empty-state", `No ${systemProfile.itemPlural} in this ${systemProfile.collectionSingular} match the current search.`));
  body.append(list);

  const tiersHeader = element("div", "parts-header");
  tiersHeader.append(element("div", "subsection-title", "Rarity Tiers (optional)"));
  const addTier = element("button", "button secondary small", "Add Tier");
  addTier.addEventListener("click", () => {
    if (!Array.isArray(value.tiers)) value.tiers = [];
    value.tiers.push({ id: `${collection.key}_t${value.tiers.length + 1}`, label: "NEW TIER", weight: 1 });
    markDirty();
    renderAll();
  });
  tiersHeader.append(addTier);
  body.append(tiersHeader);
  body.append(element("p", "variant-help", "Tiers give items different pull weights within a collection. When tiers are defined, every item must be assigned to one."));

  const tierList = element("div", "variant-list");
  if (!Array.isArray(value.tiers)) value.tiers = [];
  value.tiers.forEach((tier, index) => {
    const row = element("div", "tier-row");
    row.append(
      makeField("ID", "text", tier.id, (next, input) => {
        const oldId = tier.id;
        tier.id = next.toLowerCase().replace(/[^a-z0-9_]/g, "");
        input.value = tier.id;
        if (oldId !== tier.id) {
          for (const part of value.parts) {
            if (part.tier === oldId) part.tier = tier.id;
          }
        }
      }),
      makeField("Label", "text", tier.label, next => { tier.label = next.toUpperCase(); }),
      makeField("Weight", "number", tier.weight, next => { tier.weight = Math.max(0.0001, Number(next)); }, { min: 0.0001, step: 1 })
    );
    const assignUnassigned = element("button", "button secondary small", "← Unassigned");
    assignUnassigned.title = "Assign all items with no tier (or an unknown tier) to this tier";
    assignUnassigned.addEventListener("click", () => {
      const validIds = new Set(value.tiers.map(t => t.id));
      for (const part of value.parts) {
        if (!part.tier || !validIds.has(part.tier)) part.tier = tier.id;
      }
      markDirty();
      renderAll();
    });
    const removeTier = element("button", "button danger small", "Remove");
    removeTier.addEventListener("click", () => {
      value.tiers.splice(index, 1);
      if (value.tiers.length === 0) {
        for (const part of value.parts) delete part.tier;
      }
      markDirty();
      renderAll();
    });
    row.append(assignUnassigned, removeTier);
    tierList.append(row);
  });
  if (!value.tiers.length) tierList.append(element("div", "empty-state", "No tiers — all items in this collection pull at equal odds."));
  body.append(tierList);

  const variantsHeader = element("div", "parts-header");
  variantsHeader.append(element("div", "subsection-title", "Variants (optional)"));
  const addVariant = element("button", "button secondary small", "Add Variant");
  addVariant.addEventListener("click", () => {
    if (!Array.isArray(value.variants)) value.variants = [];
    value.variants.push({ id: `${collection.key}_v${value.variants.length + 1}`, label: "NEW", chance: 0.05 });
    markDirty();
    renderAll();
  });
  variantsHeader.append(addVariant);
  body.append(variantsHeader);
  body.append(element("p", "variant-help", "Variants fire independently after an item is selected. Each pull can carry up to two variant tags at once."));

  const variantList = element("div", "variant-list");
  if (!Array.isArray(value.variants)) value.variants = [];
  value.variants.forEach((variant, index) => {
    const row = element("div", "variant-row");
    row.append(
      makeField("ID", "text", variant.id, (next, input) => {
        variant.id = next.toLowerCase().replace(/[^a-z0-9_]/g, "");
        input.value = variant.id;
      }),
      makeField("Label", "text", variant.label, next => { variant.label = next.toUpperCase(); }),
      makeField("Chance (0–1)", "number", variant.chance, next => { variant.chance = Math.min(0.9999, Math.max(0.0001, Number(next))); }, { min: 0.0001, max: 0.9999, step: 0.01 })
    );
    const removeVariant = element("button", "button danger small", "Remove");
    removeVariant.addEventListener("click", () => { value.variants.splice(index, 1); markDirty(); renderAll(); });
    row.append(removeVariant);
    variantList.append(row);
  });
  if (!value.variants.length) variantList.append(element("div", "empty-state", "No variants — pulls from this collection always return the base item."));
  body.append(variantList);

  card.append(body);
  return card;
}

function toLocalDateTime(iso) {
  const date = new Date(iso);
  if (!Number.isFinite(date.getTime())) return "";
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
}

function fromLocalDateTime(value) {
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date.toISOString() : "";
}

function renderBoost() {
  const enabled = document.getElementById("boostEnabled");
  const name = document.getElementById("boostName");
  enabled.checked = Boolean(boost.enabled);
  name.value = boost.displayName || "";
  enabled.onchange = () => { boost.enabled = enabled.checked; markDirty(); renderAll(); };
  name.oninput = () => { boost.displayName = name.value; markDirty(); renderOverview(); };

  const grid = document.getElementById("boostMultipliers");
  grid.replaceChildren();
  for (const collection of collections) {
    const row = element("label", "multiplier-row");
    const copy = element("div");
    copy.append(element("strong", "", collection.value.displayName), element("small", "", collection.key));
    const input = document.createElement("input");
    input.type = "number";
    input.min = "0.01";
    input.step = "0.1";
    input.value = boost.collectionMultipliers[collection.key] ?? 1;
    input.addEventListener("input", () => {
      const value = Number(input.value);
      if (value === 1 || input.value === "") delete boost.collectionMultipliers[collection.key];
      else boost.collectionMultipliers[collection.key] = value;
      markDirty();
      renderOverview();
    });
    row.append(copy, input);
    grid.append(row);
  }
}

function uniqueKey(prefix) {
  let index = 1;
  let key = prefix;
  const existing = new Set(collections.map(c => c.key));
  while (existing.has(key)) key = `${prefix}_${index++}`;
  return key;
}

function parseDelimitedRows(text) {
  const firstLine = text.split(/\r?\n/, 1)[0] || "";
  const delimiter = firstLine.includes("\t") && !firstLine.includes(",") ? "\t" : ",";
  const rows = [];
  let row = [];
  let cell = "";
  let quoted = false;
  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    if (character === '"') {
      if (quoted && text[index + 1] === '"') { cell += '"'; index++; }
      else quoted = !quoted;
    } else if (character === delimiter && !quoted) {
      row.push(cell.trim());
      cell = "";
    } else if ((character === "\
" || character === "\\r") && !quoted) {
      if (character === "\\r" && text[index + 1] === "\
") index++;
      row.push(cell.trim());
      if (row.some(value => value !== "")) rows.push(row);
      row = [];
      cell = "";
    } else {
      cell += character;
    }
  }
  if (quoted) throw new Error("CSV contains an unterminated quoted value.");
  row.push(cell.trim());
  if (row.some(value => value !== "")) rows.push(row);
  return rows;
}

function parseImportItems(text, selectedFormat) {
  const trimmed = text.trim();
  if (!trimmed) return [];
  const format = selectedFormat === "auto"
    ? (trimmed.includes(",") || trimmed.includes("\t") ? "id-name" : "names")
    : selectedFormat;
  if (format === "names") {
    return trimmed.split(/\r?\n/).map(name => ({ rawId: "", name: name.trim() })).filter(item => item.name);
  }

  const rows = parseDelimitedRows(trimmed);
  if (!rows.length) return [];
  const header = rows[0].map(value => value.toLowerCase());
  const hasHeader = header.includes("name") || header.includes("displayname") || header.includes("display name");
  const idIndex = hasHeader ? header.indexOf("id") : 0;
  const nameIndex = hasHeader
    ? Math.max(header.indexOf("name"), header.indexOf("displayname"), header.indexOf("display name"))
    : 1;
  const tierIndex = hasHeader ? header.indexOf("tier") : -1;
  return rows.slice(hasHeader ? 1 : 0).map(columns => ({
    rawId: idIndex >= 0 ? String(columns[idIndex] || "").trim() : "",
    name: String(columns[nameIndex] || (columns.length === 1 ? columns[0] : "")).trim(),
    rawTier: tierIndex >= 0 ? String(columns[tierIndex] || "").trim() : ""
  })).filter(item => item.rawId || item.name);
}

function normalizeImportId(value) {
  return String(value || "").trim().toLowerCase().replace(/[\s-]+/g, "_").replace(/[^a-z0-9_]/g, "").replace(/_+/g, "_").replace(/^_+|_+$/g, "");
}

function renderImportDestinationFields() {
  const existing = document.getElementById("collectionImportMode").value === "existing";
  document.getElementById("collectionImportNameField").hidden = existing;
  document.getElementById("collectionImportTargetField").hidden = !existing;
  document.getElementById("collectionImportWeightField").hidden = existing;
  document.getElementById("collectionImportValueField").hidden = existing;
}

function populateImportTargets() {
  const target = document.getElementById("collectionImportTarget");
  const previous = target.value;
  target.replaceChildren();
  for (const collection of collections.filter(item => item.value.type === "permanent")) {
    const option = document.createElement("option");
    option.value = collection.key;
    option.textContent = `${collection.value.displayName} (${collection.value.parts?.length || 0} ${systemProfile.itemPlural})`;
    target.append(option);
  }
  if ([...target.options].some(option => option.value === previous)) target.value = previous;
}

function buildCollectionImportPreview() {
  const mode = document.getElementById("collectionImportMode").value;
  const name = document.getElementById("collectionImportName").value.trim();
  const targetKey = document.getElementById("collectionImportTarget").value;
  const target = collections.find(item => item.key === targetKey);
  const collectionKey = mode === "new" ? uniqueKey(wizardSlug(name, "imported_collection")) : targetKey;
  const errors = [];
  const warnings = [];
  const weight = Number(document.getElementById("collectionImportWeight").value);
  const salvageValue = Number(document.getElementById("collectionImportValue").value);
  let rawItems = [];

  if (mode === "new" && !name) errors.push("Enter a name for the new collection.");
  if (mode === "existing" && !target) errors.push("Choose an existing collection.");
  if (mode === "new" && (!Number.isFinite(weight) || weight < 0)) errors.push("Pull weight must be zero or greater.");
  if (mode === "new" && (!Number.isInteger(salvageValue) || salvageValue <= 0)) errors.push("Salvage value must be a positive whole number.");
  try {
    rawItems = parseImportItems(document.getElementById("collectionImportSource").value, document.getElementById("collectionImportFormat").value);
  } catch (error) {
    errors.push(error.message);
  }
  if (!rawItems.length) errors.push(`Enter at least one ${systemProfile.itemSingular}.`);

  const occupiedIds = new Set(collections.flatMap(collection => (collection.value.parts || []).map(part => String(part.id).toLowerCase())));
  const targetNames = new Set((target?.value.parts || []).map(part => String(part.name || "").trim().toLowerCase()));
  const targetTierIds = new Set((target?.value.tiers || []).map(t => String(t.id)));
  const importIds = new Set();
  const importNames = new Set();
  const parts = rawItems.map((raw, index) => {
    const rowErrors = [];
    const rowWarnings = [];
    const normalizedProvidedId = normalizeImportId(raw.rawId);
    const generatedBase = `${collectionKey}_${wizardSlug(raw.name, `item_${index + 1}`)}`;
    let id = normalizedProvidedId || generatedBase;
    if (!raw.name) rowErrors.push("Missing display name");
    if (raw.rawId && !normalizedProvidedId) rowErrors.push("ID has no usable characters");
    if (raw.rawId && normalizedProvidedId !== raw.rawId) rowWarnings.push("ID normalized");
    if (!raw.rawId) {
      let suffix = 2;
      const original = id;
      while (occupiedIds.has(id) || importIds.has(id)) id = `${original}_${suffix++}`;
      if (id !== original) rowWarnings.push("ID made unique");
    } else if (occupiedIds.has(id) || importIds.has(id)) {
      rowErrors.push("ID already exists");
    }
    const normalizedName = raw.name.toLowerCase();
    if (normalizedName && (targetNames.has(normalizedName) || importNames.has(normalizedName))) rowErrors.push("Name already exists");
    if (id) importIds.add(id);
    if (normalizedName) importNames.add(normalizedName);
    const rawTier = raw.rawTier || "";
    let tier = rawTier || undefined;
    if (rawTier && targetTierIds.size > 0 && !targetTierIds.has(rawTier)) {
      rowWarnings.push(`Unknown tier "${rawTier}"`);
    }
    return { id, name: raw.name, tier, errors: rowErrors, warnings: rowWarnings };
  });

  if (parts.some(part => part.errors.length)) errors.push("Resolve the item errors shown in the preview.");
  if (parts.some(part => part.warnings.length)) warnings.push("Some IDs were normalized or adjusted; review them before applying.");
  return { mode, name, targetKey, collectionKey, weight, salvageValue, parts, errors: [...new Set(errors)], warnings };
}

function renderCollectionImportPreview() {
  collectionImportPreview = buildCollectionImportPreview();
  renderImportPreviewUI({
    preview: document.getElementById("collectionImportPreview"),
    issues: document.getElementById("collectionImportIssues"),
    status: document.getElementById("collectionImportStatus"),
    summary: document.getElementById("collectionImportSummary"),
    apply: document.getElementById("applyCollectionImportButton"),
    skip: document.getElementById("skipCollectionImportButton"),
    data: collectionImportPreview,
    destinationLabel: collectionImportPreview.mode === "new"
      ? collectionImportPreview.name || "new collection"
      : collections.find(c => c.key === collectionImportPreview.targetKey)?.value.displayName || "existing collection"
  });
}

function renderEventImportPreview() {
  eventImportPreview = buildEventImportPreview();
  renderImportPreviewUI({
    preview: document.getElementById("eventImportPreview"),
    issues: document.getElementById("eventImportIssues"),
    status: document.getElementById("eventImportStatus"),
    summary: document.getElementById("eventImportSummary"),
    apply: document.getElementById("applyEventImportButton"),
    skip: document.getElementById("skipEventImportButton"),
    data: eventImportPreview,
    destinationLabel: eventImportPreview.mode === "new"
      ? eventImportPreview.name || "new event"
      : collections.find(c => c.key === eventImportPreview.targetKey)?.value.displayName || "existing event"
  });
}

function renderImportPreviewUI({ preview, issues, status, summary, apply, skip, data, destinationLabel }) {
  preview.replaceChildren();
  issues.replaceChildren();

  const errorParts = data.parts.filter(p => p.errors.length > 0);
  const readyParts = data.parts.filter(p => p.errors.length === 0);
  const hasErrors = errorParts.length > 0;

  for (const message of data.warnings) issues.append(element("div", "import-issue", message));

  if (errorParts.length) {
    const block = element("div", "import-issue error");
    block.append(element("strong", "", `${errorParts.length} item${errorParts.length === 1 ? "" : "s"} have errors and will be skipped:`));
    const list = element("ul", "import-error-list");
    for (const part of errorParts.slice(0, 10)) {
      const li = document.createElement("li");
      li.textContent = `${part.name || part.id || "—"}: ${part.errors.join(", ")}`;
      list.append(li);
    }
    if (errorParts.length > 10) {
      const li = document.createElement("li");
      li.textContent = `…and ${errorParts.length - 10} more`;
      list.append(li);
    }
    block.append(list);
    issues.append(block);
  }

  const hasTierColumn = data.parts.some(p => p.tier);
  const header = preview.closest(".import-table-wrap")?.querySelector(".import-table-header");
  if (header) {
    header.classList.toggle("has-tier", hasTierColumn);
    const existing = header.querySelector(".tier-header-cell");
    if (hasTierColumn && !existing) {
      const cell = element("span", "tier-header-cell", "Tier");
      header.insertBefore(cell, header.lastElementChild);
    } else if (!hasTierColumn && existing) {
      existing.remove();
    }
  }

  for (const [index, part] of readyParts.entries()) {
    const row = element("div", hasTierColumn ? "import-table has-tier" : "import-table");
    const rowStatus = part.warnings.length ? "CHECK" : "READY";
    const statusClass = part.warnings.length ? "warning" : "";
    const cells = [
      element("span", "", String(index + 1)),
      element("code", "", part.id || "-"),
      element("span", "", part.name || "-")
    ];
    if (hasTierColumn) cells.push(element("span", "import-tier-cell", part.tier || "—"));
    cells.push(element("span", `import-row-status ${statusClass}`.trim(), rowStatus));
    row.append(...cells);
    preview.append(row);
  }
  if (!data.parts.length) {
    preview.append(element("div", "empty-state", `No ${systemProfile.itemPlural} to preview.`));
  } else if (!readyParts.length) {
    preview.append(element("div", "empty-state", "All items have errors. Fix the source data and try again."));
  }

  summary.textContent = hasErrors
    ? `${readyParts.length} of ${data.parts.length} ${systemProfile.itemPlural} ready to add to ${destinationLabel}.`
    : `${data.parts.length} ${data.parts.length === 1 ? systemProfile.itemSingular : systemProfile.itemPlural} will be added to ${destinationLabel}.`;
  status.textContent = hasErrors ? "NEEDS WORK" : data.warnings.length ? "REVIEW" : "READY";
  status.className = `metric-chip ${hasErrors ? "invalid" : "valid"}`;
  apply.disabled = hasErrors;
  if (skip) {
    skip.hidden = !hasErrors || readyParts.length === 0;
    skip.textContent = `Skip ${errorParts.length} Error${errorParts.length === 1 ? "" : "s"} — Apply ${readyParts.length}`;
  }
}

function openCollectionImport() {
  populateImportTargets();
  renderImportDestinationFields();
  document.getElementById("collectionImportModal").hidden = false;
  renderCollectionImportPreview();
  document.getElementById("collectionImportSource").focus();
}

function closeCollectionImport() {
  document.getElementById("collectionImportModal").hidden = true;
}

function resetCollectionImport() {
  document.getElementById("collectionImportMode").value = "new";
  document.getElementById("collectionImportName").value = "Imported Collection";
  document.getElementById("collectionImportFormat").value = "auto";
  document.getElementById("collectionImportWeight").value = "10";
  document.getElementById("collectionImportValue").value = "1";
  document.getElementById("collectionImportSource").value = "";
  collectionImportPreview = null;
}

function renderEventImportDestinationFields() {
  const existing = document.getElementById("eventImportMode").value === "existing";
  document.getElementById("eventImportNameField").hidden = existing;
  document.getElementById("eventImportTargetField").hidden = !existing;
  document.getElementById("eventImportWeightField").hidden = existing;
  document.getElementById("eventImportValueField").hidden = existing;
  document.getElementById("eventImportStartField").hidden = existing;
  document.getElementById("eventImportEndField").hidden = existing;
}

function populateEventImportTargets() {
  const target = document.getElementById("eventImportTarget");
  const previous = target.value;
  target.replaceChildren();
  for (const collection of collections.filter(c => c.value.type === "event")) {
    const option = document.createElement("option");
    option.value = collection.key;
    option.textContent = collection.value.displayName || collection.key;
    target.append(option);
  }
  if (previous) target.value = previous;
}

function buildEventImportPreview() {
  const mode = document.getElementById("eventImportMode").value;
  const name = document.getElementById("eventImportName").value.trim();
  const targetKey = document.getElementById("eventImportTarget").value;
  const target = collections.find(item => item.key === targetKey);
  const collectionKey = mode === "new" ? uniqueKey(wizardSlug(name, "imported_event")) : targetKey;
  const errors = [];
  const warnings = [];
  const weight = Number(document.getElementById("eventImportWeight").value);
  const salvageValue = Number(document.getElementById("eventImportValue").value);
  const startValue = document.getElementById("eventImportStart").value;
  const endValue = document.getElementById("eventImportEnd").value;
  let rawItems = [];

  if (mode === "new" && !name) errors.push("Event name is required.");
  if (mode === "new" && (isNaN(weight) || weight < 0)) errors.push("Pull weight must be zero or greater.");
  if (mode === "new" && (!Number.isInteger(salvageValue) || salvageValue <= 0)) errors.push("Salvage value must be a positive whole number.");
  if (mode === "new") {
    const start = new Date(startValue);
    const end = new Date(endValue);
    if (!startValue || !Number.isFinite(start.getTime())) errors.push("Start date is required.");
    if (!endValue || !Number.isFinite(end.getTime())) errors.push("End date is required.");
    if (startValue && endValue && Number.isFinite(start.getTime()) && Number.isFinite(end.getTime()) && end <= start)
      errors.push("End date must be after start date.");
  }
  if (mode === "existing" && !targetKey) errors.push("Select an existing event to add items to.");

  try {
    rawItems = parseImportItems(document.getElementById("eventImportSource").value, document.getElementById("eventImportFormat").value);
  } catch (error) {
    errors.push(error.message);
  }

  const occupiedIds = new Set(collections.flatMap(collection => (collection.value.parts || []).map(part => String(part.id).toLowerCase())));
  const targetNames = new Set((target?.value.parts || []).map(part => String(part.name || "").trim().toLowerCase()));
  const importIds = new Set();
  const importNames = new Set();
  const parts = rawItems.map((raw, index) => {
    const rowErrors = [];
    const rowWarnings = [];
    const normalizedProvidedId = normalizeImportId(raw.rawId);
    const generatedBase = `${collectionKey}_${wizardSlug(raw.name, `item_${index + 1}`)}`;
    let id = normalizedProvidedId || generatedBase;
    if (!raw.name) rowErrors.push("Missing display name");
    if (raw.rawId && !normalizedProvidedId) rowErrors.push("ID has no usable characters");
    if (!normalizedProvidedId) {
      let suffix = 2;
      const original = id;
      while (occupiedIds.has(id) || importIds.has(id)) id = `${original}_${suffix++}`;
      if (id !== original) rowWarnings.push("ID made unique");
    } else if (occupiedIds.has(id) || importIds.has(id)) {
      rowErrors.push("ID already exists");
    }
    const normalizedName = raw.name.toLowerCase();
    if (normalizedName && (targetNames.has(normalizedName) || importNames.has(normalizedName))) rowErrors.push("Name already exists");
    if (id) importIds.add(id);
    if (normalizedName) importNames.add(normalizedName);
    return { id, name: raw.name, errors: rowErrors, warnings: rowWarnings };
  });

  return { mode, name, collectionKey, targetKey, weight, salvageValue, startValue, endValue, errors, warnings, parts };
}

function openEventImport() {
  populateEventImportTargets();
  renderEventImportDestinationFields();
  const now = new Date();
  const end = new Date(now.getTime() + 7 * 86400000);
  document.getElementById("eventImportStart").value = toLocalDateTime(now.toISOString());
  document.getElementById("eventImportEnd").value = toLocalDateTime(end.toISOString());
  document.getElementById("eventImportModal").hidden = false;
  renderEventImportPreview();
  document.getElementById("eventImportSource").focus();
}

function closeEventImport() {
  document.getElementById("eventImportModal").hidden = true;
}

function resetEventImport() {
  document.getElementById("eventImportMode").value = "new";
  document.getElementById("eventImportName").value = "Imported Event";
  document.getElementById("eventImportFormat").value = "auto";
  document.getElementById("eventImportWeight").value = "10";
  document.getElementById("eventImportValue").value = "2";
  document.getElementById("eventImportSource").value = "";
  eventImportPreview = null;
}

function applyEventImport() {
  const preview = buildEventImportPreview();
  if (preview.errors.length) { eventImportPreview = preview; renderEventImportPreview(); return; }
  applyEventImportParts(preview, preview.parts);
}

function applyEventImportSkipErrors() {
  const preview = buildEventImportPreview();
  const readyParts = preview.parts.filter(p => p.errors.length === 0);
  if (!readyParts.length) { showNotice("No ready items to apply.", "error"); return; }
  applyEventImportParts(preview, readyParts);
}

function applyEventImportParts(preview, parts) {
  const previousCollections = clone(collections);
  if (preview.mode === "new") {
    collections.push({
      key: preview.collectionKey,
      value: {
        displayName: preview.name,
        type: "event",
        enabled: false,
        activeFromUtc: new Date(preview.startValue).toISOString(),
        activeUntilUtc: new Date(preview.endValue).toISOString(),
        weight: preview.weight,
        salvageValue: preview.salvageValue,
        parts: parts.map(p => ({ id: p.id, name: p.name }))
      }
    });
  } else {
    const target = collections.find(item => item.key === preview.targetKey);
    if (!target) { showNotice("Target event no longer exists.", "error"); return; }
    target.value.parts = [...(target.value.parts || []), ...parts.map(p => ({ id: p.id, name: p.name }))];
  }
  const errors = validateModel();
  if (errors.length) {
    collections = previousCollections;
    showNotice(`Import could not be applied: ${errors.join(" ")}`, "error");
    return;
  }
  markDirty();
  closeEventImport();
  resetEventImport();
  renderAll();
  switchView("events");
  showNotice(`${parts.length} ${parts.length === 1 ? systemProfile.itemSingular : systemProfile.itemPlural} added to the editor. Review and save when ready.`, "success");
}

function applyCollectionImport() {
  const preview = buildCollectionImportPreview();
  if (preview.errors.length) { collectionImportPreview = preview; renderCollectionImportPreview(); return; }
  applyCollectionImportParts(preview, preview.parts);
}

function applyCollectionImportSkipErrors() {
  const preview = buildCollectionImportPreview();
  const readyParts = preview.parts.filter(p => p.errors.length === 0);
  if (!readyParts.length) { showNotice("No ready items to apply.", "error"); return; }
  applyCollectionImportParts(preview, readyParts);
}

function applyCollectionImportParts(preview, parts) {
  const previousCollections = clone(collections);
  if (preview.mode === "new") {
    collections.push({
      key: preview.collectionKey,
      value: {
        displayName: preview.name,
        type: "permanent",
        weight: preview.weight,
        salvageValue: preview.salvageValue,
        parts: parts.map(({ id, name, tier }) => tier ? { id, name, tier } : { id, name })
      }
    });
    expandedCollectionKeys.add(preview.collectionKey);
  } else {
    const target = collections.find(item => item.key === preview.targetKey);
    target.value.parts.push(...parts.map(({ id, name, tier }) => tier ? { id, name, tier } : { id, name }));
    expandedCollectionKeys.add(preview.targetKey);
  }
  const errors = validateModel();
  if (errors.length) {
    collections = previousCollections;
    showNotice(`Import could not be applied: ${errors.join(" ")}`, "error");
    return;
  }
  markDirty();
  closeCollectionImport();
  resetCollectionImport();
  renderAll();
  switchView("collections");
  showNotice(`${parts.length} ${parts.length === 1 ? systemProfile.itemSingular : systemProfile.itemPlural} added to the editor. Review and save when ready.`, "success");
}

async function resetViewer(viewerId, displayName) {
  if (!window.confirm(`Remove all of ${displayName}'s items and data? A backup will be created first.`)) return;
  try {
    const response = await fetch("/api/inventory/reset-viewer", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ viewerId })
    });
    const result = await response.json();
    if (!result.ok) { showNotice(result.errors?.join(" ") || "Reset failed.", "error"); return; }
    selectedViewerId = "";
    const loaded = await fetch("/api/analytics", { cache: "no-store" });
    analytics = await loaded.json();
    renderViewerInspector();
    showNotice(`${displayName}'s inventory has been reset. A backup was created.`, "success");
  } catch {
    showNotice("Could not reach the runtime. Is CircuitOS running?", "error");
  }
}

async function removeInventoryItem(viewerId, displayName, itemId, itemName) {
  if (!window.confirm(`Remove ${itemName} from ${displayName}'s inventory? A backup will be created first.`)) return;
  try {
    const response = await fetch("/api/inventory/remove-item", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ viewerId, itemId })
    });
    const result = await response.json();
    if (!result.ok) { showNotice(result.errors?.join(" ") || "Remove failed.", "error"); return; }
    const loaded = await fetch("/api/analytics", { cache: "no-store" });
    analytics = await loaded.json();
    renderViewerInspector();
    showNotice(`${itemName} removed from ${displayName}'s inventory. A backup was created.`, "success");
  } catch {
    showNotice("Could not reach the runtime. Is CircuitOS running?", "error");
  }
}

function addCollection(type) {
  const key = uniqueKey(type === "event" ? "event_new" : "collection_new");
  const now = new Date();
  const end = new Date(now.getTime() + 7 * 86400000);
  const value = {
    displayName: type === "event" ? "New Event Collection" : "New Collection",
    type,
    weight: 10,
    salvageValue: 2,
    parts: [{ id: `${key}_part_one`, name: "Part One" }]
  };
  if (type === "event") {
    value.enabled = false;
    value.activeFromUtc = now.toISOString();
    value.activeUntilUtc = end.toISOString();
  }
  collections.push({ key, value });
  expandedCollectionKeys.add(key);
  markDirty();
  renderAll();
}

async function _saveCatalogData() {
  saveButton.disabled = true;
  try {
    const response = await fetch("/api/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(serializeModel())
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Save failed."]).join(" "));
    const [analyticsResponse, rolesResponse, backupsResponse] = await Promise.all([
      fetch("/api/analytics", { cache: "no-store" }),
      fetch("/api/roles", { cache: "no-store" }),
      fetch("/api/backups", { cache: "no-store" })
    ]);
    if (analyticsResponse.ok) analytics = await analyticsResponse.json();
    if (rolesResponse.ok) roleAwards = await rolesResponse.json();
    if (backupsResponse.ok) backupCenter = await backupsResponse.json();
    markClean();
    renderAll();
    return result.backups?.length || 0;
  } finally {
    saveButton.disabled = false;
  }
}

async function saveConfiguration() {
  const errors = validateModel();
  if (errors.length) {
    showNotice(errors.join(" "), "error");
    switchView("overview");
    return;
  }
  try {
    const backupCount = await _saveCatalogData();
    showNotice(`Configuration saved. ${backupCount} backup files created.`, "success");
  } catch (error) {
    showNotice(error.message, "error");
  }
}

function exportConfiguration() {
  const blob = new Blob([JSON.stringify(serializeModel(), null, 2)], { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `circuit-config-${new Date().toISOString().slice(0, 10)}.json`;
  link.click();
  URL.revokeObjectURL(link.href);
}

async function downloadDiagnostics() {
  const model = serializeModel();
  const encoded = new TextEncoder().encode(JSON.stringify(model));
  const digest = await crypto.subtle.digest("SHA-256", encoded);
  const fingerprint = [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, "0")).join("").toUpperCase();
  const eventCollections = collections.filter(collection => collection.value.type === "event");
  const report = {
    schemaVersion: 1,
    createdUtc: new Date().toISOString(),
    application: { product: platformName, ...runtimeInfo },
    configuration: {
      profileConfigured,
      dirty,
      profileDirty,
      validationErrors: validateModel(),
      collectionCount: collections.length,
      permanentCollectionCount: collections.length - eventCollections.length,
      eventCollectionCount: eventCollections.length,
      itemCount: collections.reduce((sum, collection) => sum + (collection.value.parts?.length || 0), 0),
      enabledEventCount: eventCollections.filter(collection => collection.value.enabled !== false).length,
      boostEnabled: Boolean(boost.enabled),
      fingerprintSha256: fingerprint
    },
    inventoryAggregates: {
      viewerCount: Number(analytics.summary?.viewerCount || 0),
      duplicateUnits: Number(analytics.summary?.duplicateUnits || 0),
      totalCurrency: Number(analytics.summary?.totalScrap || 0)
    },
    discordRoleAggregates: {
      pending: Number(roleAwards.summary?.pending || 0),
      assigned: Number(roleAwards.summary?.assigned || 0)
    },
    recovery: {
      indexedBackupCount: Number(backupCenter.backups?.length || 0),
      liveFileCount: Number(backupCenter.liveFiles?.length || 0)
    },
    privacy: "Viewer identities, inventory contents, message templates, and local paths are intentionally excluded."
  };
  const blob = new Blob([JSON.stringify(report, null, 2)], { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `circuitos-diagnostics-${new Date().toISOString().slice(0, 10)}.json`;
  link.click();
  URL.revokeObjectURL(link.href);
  showNotice("Privacy-safe diagnostics report downloaded.", "success");
}

async function importConfiguration(file) {
  try {
    const payload = JSON.parse(await file.text());
    if (!payload.components?.collections || !payload.boost) throw new Error("Import must contain components and boost objects.");
    normalizeModel({ ...payload, dataPath });
    markDirty();
    renderAll();
    showNotice("Configuration imported into the editor. Review it before saving live.");
  } catch (error) {
    showNotice(`Import failed: ${error.message}`, "error");
  }
}

function updateOverlayPreview() {
  const frame = document.getElementById("overlayPreviewFrame");
  if (frame?.contentWindow) {
    frame.contentWindow.postMessage({ type: "overlayPreviewConfig", config: overlayConfig }, "*");
  }
}

function overlayField(labelText, type, value, onChange, options = {}) {
  const label = element("label", "field");
  label.append(element("span", "", labelText));
  if (type === "range") {
    const display = element("span", "overlay-range-value", String(value));
    const input = document.createElement("input");
    input.type = "range";
    input.value = value ?? 0;
    if (options.min !== undefined) input.min = options.min;
    if (options.max !== undefined) input.max = options.max;
    if (options.step !== undefined) input.step = options.step;
    input.addEventListener("input", () => { display.textContent = input.value; });
    input.addEventListener("change", () => {
      onChange(Number(input.value));
      overlayDirty = true;
      document.getElementById("saveOverlayButton").disabled = false;
      updateOverlayPreview();
    });
    label.append(input, display);
    return label;
  }
  if (type === "select") {
    const input = document.createElement("select");
    for (const [val, text] of Object.entries(options.options || {})) {
      const opt = document.createElement("option");
      opt.value = val;
      opt.textContent = text;
      if (val === String(value)) opt.selected = true;
      input.append(opt);
    }
    input.addEventListener("change", () => {
      onChange(input.value);
      overlayDirty = true;
      document.getElementById("saveOverlayButton").disabled = false;
      updateOverlayPreview();
    });
    label.append(input);
    return label;
  }
  const input = document.createElement("input");
  input.type = type;
  input.value = value ?? "";
  if (options.min !== undefined) input.min = options.min;
  if (options.max !== undefined) input.max = options.max;
  if (options.step !== undefined) input.step = options.step;
  if (options.maxLength) input.maxLength = options.maxLength;
  const commitChange = () => {
    onChange(type === "number" ? Number(input.value) : input.value);
    overlayDirty = true;
    document.getElementById("saveOverlayButton").disabled = false;
    updateOverlayPreview();
  };
  input.addEventListener("input", commitChange);
  if (type === "number" || type === "text") input.addEventListener("change", commitChange);
  label.append(input);
  return label;
}

function overlayCheckbox(labelText, checked, onChange) {
  const label = element("label", "toggle");
  const checkbox = document.createElement("input");
  checkbox.type = "checkbox";
  checkbox.checked = checked;
  checkbox.addEventListener("change", () => {
    onChange(checkbox.checked);
    overlayDirty = true;
    document.getElementById("saveOverlayButton").disabled = false;
    updateOverlayPreview();
  });
  label.append(checkbox, document.createElement("span"), document.createTextNode(labelText));
  return label;
}

function buildBgImageField(cfg) {
  const wrap = element("div", "overlay-bg-field");

  const titleRow = element("div", "overlay-bg-title-row");
  titleRow.append(element("span", "overlay-bg-label", "Background image"));

  const controls = element("div", "overlay-bg-controls");

  const fileInput = document.createElement("input");
  fileInput.type = "file";
  fileInput.accept = "image/png,image/jpeg,image/gif,image/webp";
  fileInput.style.display = "none";

  const uploadBtn = element("button", "button secondary small", "Upload Image");
  uploadBtn.type = "button";
  uploadBtn.addEventListener("click", () => fileInput.click());

  const clearBtn = element("button", "button secondary small", "Clear");
  clearBtn.type = "button";

  const status = element("span", "overlay-bg-status");

  function updateStatus() {
    const val = cfg.appearance.backgroundImage || "";
    if (!val) {
      status.textContent = "No image";
      clearBtn.hidden = true;
    } else {
      status.textContent = "Uploaded image";
      clearBtn.hidden = false;
    }
  }
  updateStatus();

  fileInput.addEventListener("change", async () => {
    const file = fileInput.files?.[0];
    if (!file) return;
    uploadBtn.disabled = true;
    uploadBtn.textContent = "Uploading…";
    try {
      const buffer = await file.arrayBuffer();
      const response = await fetch("/api/overlay-image", {
        method: "POST",
        headers: { "Content-Type": file.type },
        body: buffer
      });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error((result.errors || ["Upload failed."]).join(" "));
      cfg.appearance.backgroundImage = result.filename;
      overlayDirty = true;
      document.getElementById("saveOverlayButton").disabled = false;
      updateStatus();
      showNotice("Image uploaded. Save to apply it to the overlay.", "success");
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      uploadBtn.disabled = false;
      uploadBtn.textContent = "Upload Image";
      fileInput.value = "";
    }
  });

  clearBtn.addEventListener("click", () => {
    cfg.appearance.backgroundImage = "";
    overlayDirty = true;
    document.getElementById("saveOverlayButton").disabled = false;
    updateStatus();
    updateOverlayPreview();
  });

  controls.append(uploadBtn, clearBtn, status, fileInput);
  wrap.append(titleRow, controls);
  return wrap;
}

// Per-state background upload (rare/complete/duplicate). Empty = the state uses the global background.
function buildStateBgField(state, stateLabel) {
  const wrap = element("div", "overlay-bg-field");
  const titleRow = element("div", "overlay-bg-title-row");
  titleRow.append(element("span", "overlay-bg-label", `${stateLabel} background`));
  const controls = element("div", "overlay-bg-controls");
  const fileInput = document.createElement("input");
  fileInput.type = "file";
  fileInput.accept = "image/png,image/jpeg,image/gif,image/webp";
  fileInput.style.display = "none";
  const uploadBtn = element("button", "button secondary small", "Upload");
  uploadBtn.type = "button";
  uploadBtn.addEventListener("click", () => fileInput.click());
  const clearBtn = element("button", "button secondary small", "Clear");
  clearBtn.type = "button";
  const status = element("span", "overlay-bg-status");
  const slot = () => (overlayConfig.stateColors[state] ||= {});
  function updateStatus() {
    const val = slot().backgroundImage || "";
    status.textContent = val ? "Custom image" : "Uses global";
    clearBtn.hidden = !val;
  }
  updateStatus();
  fileInput.addEventListener("change", async () => {
    const file = fileInput.files?.[0];
    if (!file) return;
    uploadBtn.disabled = true;
    uploadBtn.textContent = "Uploading…";
    try {
      const buffer = await file.arrayBuffer();
      const response = await fetch(`/api/overlay-image?state=${encodeURIComponent(state)}`, {
        method: "POST", headers: { "Content-Type": file.type }, body: buffer
      });
      const result = await response.json();
      if (!response.ok || !result.ok) throw new Error((result.errors || ["Upload failed."]).join(" "));
      slot().backgroundImage = result.filename;
      overlayDirty = true;
      document.getElementById("saveOverlayButton").disabled = false;
      updateStatus();
      updateOverlayPreview();
      showNotice(`${stateLabel} image uploaded. Save to apply it to the overlay.`, "success");
    } catch (error) {
      showNotice(error.message, "error");
    } finally {
      uploadBtn.disabled = false;
      uploadBtn.textContent = "Upload";
      fileInput.value = "";
    }
  });
  clearBtn.addEventListener("click", () => {
    slot().backgroundImage = "";
    overlayDirty = true;
    document.getElementById("saveOverlayButton").disabled = false;
    updateStatus();
    updateOverlayPreview();
  });
  controls.append(uploadBtn, clearBtn, status, fileInput);
  wrap.append(titleRow, controls);
  return wrap;
}

function renderStateColorFields() {
  const container = document.getElementById("overlayStateColorsFields");
  const note = document.getElementById("overlayStateColorsNote");
  if (!container) return;
  const state = activeOverlayPreviewState;
  if (state === "normal") {
    if (note) note.hidden = false;
    container.replaceChildren();
    return;
  }
  if (note) note.hidden = true;
  if (!overlayConfig.stateColors) overlayConfig.stateColors = {};
  if (!overlayConfig.stateColors[state]) overlayConfig.stateColors[state] = {};
  const sc = overlayConfig.stateColors[state];
  const stateLabel = { rare: "Rare", complete: "Complete", duplicate: "Duplicate" }[state] || state;
  container.replaceChildren(
    overlayField(`${stateLabel} accent`, "color", sc.accentColor || overlayConfig.appearance?.accentColor || "#ff1821", v => { overlayConfig.stateColors[state].accentColor = v; }),
    overlayField(`${stateLabel} label`, "color", sc.labelColor || overlayConfig.appearance?.labelColor || "#ff3b43", v => { overlayConfig.stateColors[state].labelColor = v; }),
    overlayField(`${stateLabel} bar fill`, "color", sc.barColor || overlayConfig.appearance?.barColor || "#ff1821", v => { overlayConfig.stateColors[state].barColor = v; }),
    buildStateBgField(state, stateLabel)
  );
}

function renderOverlayEditor() {
  if (!overlayConfig) return;
  const cfg = overlayConfig;
  const obsPathEl = document.getElementById("obsFilePath");
  if (obsPathEl && overlayFilePath) obsPathEl.textContent = overlayFilePath;
  document.getElementById("overlayLayoutFields").replaceChildren(
    overlayField("Position", "select", cfg.layout?.position ?? "bottom-center", v => { cfg.layout.position = v; }, {
      options: { "bottom-center": "Bottom Center", "bottom-left": "Bottom Left", "bottom-right": "Bottom Right", "top-left": "Top Left", "top-right": "Top Right" }
    }),
    overlayField("Width (px)", "number", cfg.layout?.width ?? 1500, v => { cfg.layout.width = v; }, { min: 320, max: 1600, step: 10 }),
    overlayField("Min height (px)", "number", cfg.layout?.minHeight ?? 178, v => { cfg.layout.minHeight = v; }, { min: 100, max: 600, step: 1 }),
    overlayField("H offset (px)", "number", cfg.layout?.offsetX ?? 0, v => { cfg.layout.offsetX = v; }, { min: 0, max: 1000, step: 1 }),
    overlayField("V offset (px)", "number", cfg.layout?.offsetY ?? 54, v => { cfg.layout.offsetY = v; }, { min: 0, max: 1000, step: 1 }),
    overlayField("Bar height (px)", "number", cfg.layout?.barHeight ?? 8, v => { cfg.layout.barHeight = v; }, { min: 2, max: 32, step: 1 })
  );
  document.getElementById("overlayAppearanceFields").replaceChildren(
    overlayField("Background", "color", cfg.appearance?.backgroundColor ?? "#00101f", v => { cfg.appearance.backgroundColor = v; }),
    overlayField("Panel", "color", cfg.appearance?.panelColor ?? "#06233e", v => { cfg.appearance.panelColor = v; }),
    overlayField("Accent", "color", cfg.appearance?.accentColor ?? "#ff1821", v => { cfg.appearance.accentColor = v; }),
    overlayField("Label", "color", cfg.appearance?.labelColor ?? "#ff3b43", v => { cfg.appearance.labelColor = v; }),
    overlayField("Bar fill", "color", cfg.appearance?.barColor ?? "#ff1821", v => { cfg.appearance.barColor = v; }),
    overlayField("Text", "color", cfg.appearance?.textColor ?? "#f8fbff", v => { cfg.appearance.textColor = v; }),
    overlayField("Muted", "color", cfg.appearance?.mutedColor ?? "#8094ad", v => { cfg.appearance.mutedColor = v; }),
    overlayField("Opacity", "range", cfg.appearance?.backgroundOpacity ?? 0.98, v => { cfg.appearance.backgroundOpacity = v; }, { min: 0, max: 1, step: 0.01 })
  );
  document.getElementById("overlayAppearanceFields").appendChild(buildBgImageField(cfg));
  document.getElementById("overlayTimingFields").replaceChildren(
    overlayField("Display (sec)", "number", cfg.timing?.displaySeconds ?? 8, v => { cfg.timing.displaySeconds = v; }, { min: 2, max: 60, step: 1 }),
    overlayField("Enter (ms)", "number", cfg.timing?.enterMilliseconds ?? 620, v => { cfg.timing.enterMilliseconds = v; }, { min: 0, max: 5000, step: 10 }),
    overlayField("Exit (ms)", "number", cfg.timing?.exitMilliseconds ?? 430, v => { cfg.timing.exitMilliseconds = v; }, { min: 0, max: 5000, step: 10 }),
    overlayField("Animation", "select", cfg.animation?.style ?? "slide", v => { cfg.animation.style = v; }, {
      options: { slide: "Slide", fade: "Fade", none: "None" }
    }),
    overlayField("Font family", "text", cfg.typography?.fontFamily ?? "Segoe UI", v => { cfg.typography.fontFamily = v; }, { maxLength: 80 }),
    overlayField("Viewer name (px)", "number", cfg.typography?.viewerNameSize ?? 38, v => { cfg.typography.viewerNameSize = v; }, { min: 16, max: 72, step: 1 }),
    overlayField("Item name (px)", "number", cfg.typography?.partNameSize ?? 34, v => { cfg.typography.partNameSize = v; }, { min: 14, max: 64, step: 1 }),
    overlayField("Label size (px)", "number", cfg.typography?.labelSize ?? 14, v => { cfg.typography.labelSize = v; }, { min: 8, max: 24, step: 1 })
  );
  document.getElementById("overlayContentFields").replaceChildren(
    overlayCheckbox("Overlay enabled", cfg.enabled !== false, v => { cfg.enabled = v; }),
    overlayCheckbox("Show collection name", cfg.content?.showCollection !== false, v => { cfg.content.showCollection = v; }),
    overlayCheckbox("Show progress bar", cfg.content?.showProgress !== false, v => { cfg.content.showProgress = v; }),
    overlayCheckbox("Show CircuitOS branding", cfg.content?.showCircuitOSBranding !== false, v => { cfg.content.showCircuitOSBranding = v; })
  );
  if (!cfg.labels) cfg.labels = {};
  document.getElementById("overlayLabelFields").replaceChildren(
    overlayField("Scan label", "text", cfg.labels.eyebrow ?? "CIRCUIT SCAN", v => { cfg.labels.eyebrow = v; }, { maxLength: 60 }),
    overlayField("Component label", "text", cfg.labels.componentAcquired ?? "COMPONENT ACQUIRED", v => { cfg.labels.componentAcquired = v; }, { maxLength: 60 }),
    overlayField("Progress label", "text", cfg.labels.collectionProgress ?? "COLLECTION PROGRESS", v => { cfg.labels.collectionProgress = v; }, { maxLength: 60 }),
    overlayField("New item badge", "text", cfg.labels.newItem ?? "NEW COMPONENT", v => { cfg.labels.newItem = v; }, { maxLength: 60 }),
    overlayField("Complete badge", "text", cfg.labels.collectionComplete ?? "COLLECTION COMPLETE", v => { cfg.labels.collectionComplete = v; }, { maxLength: 60 }),
    overlayField("Duplicate badge", "text", cfg.labels.duplicate ?? "DUPLICATE", v => { cfg.labels.duplicate = v; }, { maxLength: 40 })
  );
  renderStateColorFields();
}

function scaleOverlayPreview() {
  const wrap = document.querySelector(".overlay-preview-wrap");
  const frame = document.getElementById("overlayPreviewFrame");
  if (!wrap || !frame) return;
  const scale = wrap.clientWidth / 1920;
  // The overlay is a lower-third, so most of the 1080-tall canvas is empty transparent space.
  // Reveal only the bottom band (full width) and crop the empty area above, so the overlay fills
  // the frame and is readable instead of a thin strip in a full 16:9 preview.
  const visibleBand = 420; // px of the 1080 canvas to show, anchored to the bottom
  frame.style.transformOrigin = "top left";
  frame.style.transform = `translateY(${-scale * (1080 - visibleBand)}px) scale(${scale})`;
  wrap.style.height = `${visibleBand * scale}px`;
}

async function saveOverlayConfig() {
  const button = document.getElementById("saveOverlayButton");
  button.disabled = true;
  try {
    const response = await fetch("/api/overlay-config", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ config: overlayConfig })
    });
    const result = await response.json();
    if (!response.ok || !result.ok) throw new Error((result.errors || ["Save failed."]).join(" "));
    overlayDirty = false;
    const frame = document.getElementById("overlayPreviewFrame");
    if (frame) frame.src = `/overlay/index.html?preview=1&t=${Date.now()}`;
    showNotice("Overlay config saved. Preview refreshed.", "success");
  } catch (error) {
    button.disabled = false;
    showNotice(error.message, "error");
  }
}

function switchView(view) {
  const navButton = document.querySelector(`.nav-button[data-view="${view}"]`);
  if (navButton) {
    const parentGroup = navButton.closest("details.nav-group");
    if (parentGroup) parentGroup.open = true;
  }
  document.querySelectorAll(".view").forEach(node => node.classList.toggle("active", node.id === `${view}View`));
  document.querySelectorAll(".nav-button").forEach(node => node.classList.toggle("active", node.dataset.view === view));
  document.getElementById("viewTitle").textContent = getViewTitle(view);
  renderViewOnDemand(view);
  window.scrollTo({ top: 0 });
}

document.querySelectorAll(".nav-button").forEach(button => button.addEventListener("click", () => switchView(button.dataset.view)));
document.querySelectorAll("[data-jump-view]").forEach(target => target.addEventListener("click", event => {
  const interactive = event.target.closest("input, select, textarea, button, a");
  if (interactive && interactive !== target) return;
  switchView(target.dataset.jumpView);
}));

// Optional: let the user hide the System Check card from the Overview (persisted locally).
function applySystemCheckVisibility() {
  const hidden = localStorage.getItem("circuitos.hideSystemCheck") === "1";
  const panel = document.getElementById("systemCheckPanel");
  const showButton = document.getElementById("showSystemCheckButton");
  if (panel) panel.hidden = hidden;
  if (showButton) showButton.hidden = !hidden;
}
document.getElementById("hideSystemCheckButton").addEventListener("click", () => {
  localStorage.setItem("circuitos.hideSystemCheck", "1");
  applySystemCheckVisibility();
});
document.getElementById("showSystemCheckButton").addEventListener("click", () => {
  localStorage.removeItem("circuitos.hideSystemCheck");
  applySystemCheckVisibility();
});
applySystemCheckVisibility();
document.getElementById("collectionSearch").addEventListener("input", () => renderCollectionList("permanent"));
document.getElementById("expandCollectionsButton").addEventListener("click", () => {
  for (const collection of collections.filter(item => item.value.type === "permanent")) expandedCollectionKeys.add(collection.key);
  renderCollectionList("permanent");
});
document.getElementById("collapseCollectionsButton").addEventListener("click", () => {
  for (const collection of collections.filter(item => item.value.type === "permanent")) expandedCollectionKeys.delete(collection.key);
  renderCollectionList("permanent");
});
document.getElementById("importCollectionButton").addEventListener("click", openCollectionImport);
document.getElementById("closeCollectionImportButton").addEventListener("click", closeCollectionImport);
document.getElementById("cancelCollectionImportButton").addEventListener("click", closeCollectionImport);
document.getElementById("skipCollectionImportButton").addEventListener("click", applyCollectionImportSkipErrors);
document.getElementById("applyCollectionImportButton").addEventListener("click", applyCollectionImport);
document.getElementById("loadCollectionCsvButton").addEventListener("click", () => document.getElementById("collectionCsvFile").click());
document.getElementById("collectionCsvFile").addEventListener("change", async event => {
  const file = event.target.files[0];
  if (file) {
    document.getElementById("collectionImportSource").value = await file.text();
    document.getElementById("collectionImportFormat").value = "auto";
    renderCollectionImportPreview();
  }
  event.target.value = "";
});
for (const id of ["collectionImportName", "collectionImportTarget", "collectionImportFormat", "collectionImportWeight", "collectionImportValue", "collectionImportSource"]) {
  const input = document.getElementById(id);
  input.addEventListener(input.tagName === "SELECT" ? "change" : "input", renderCollectionImportPreview);
}
document.getElementById("collectionImportMode").addEventListener("change", () => {
  renderImportDestinationFields();
  renderCollectionImportPreview();
});
document.getElementById("addCollectionButton").addEventListener("click", () => addCollection("permanent"));
document.getElementById("addEventButton").addEventListener("click", () => addCollection("event"));
document.getElementById("importEventButton").addEventListener("click", openEventImport);
document.getElementById("closeEventImportButton").addEventListener("click", closeEventImport);
document.getElementById("cancelEventImportButton").addEventListener("click", closeEventImport);
document.getElementById("skipEventImportButton").addEventListener("click", applyEventImportSkipErrors);
document.getElementById("applyEventImportButton").addEventListener("click", applyEventImport);
document.getElementById("loadEventCsvButton").addEventListener("click", () => document.getElementById("eventCsvFile").click());
document.getElementById("eventCsvFile").addEventListener("change", async event => {
  const file = event.target.files[0];
  if (file) {
    document.getElementById("eventImportSource").value = await file.text();
    document.getElementById("eventImportFormat").value = "auto";
    renderEventImportPreview();
  }
  event.target.value = "";
});
for (const id of ["eventImportName", "eventImportTarget", "eventImportFormat", "eventImportWeight", "eventImportValue", "eventImportStart", "eventImportEnd", "eventImportSource"]) {
  const input = document.getElementById(id);
  input.addEventListener(input.tagName === "SELECT" ? "change" : "input", renderEventImportPreview);
}
document.getElementById("eventImportMode").addEventListener("change", () => {
  renderEventImportDestinationFields();
  renderEventImportPreview();
});
document.getElementById("saveButton").addEventListener("click", saveConfiguration);
document.getElementById("reloadButton").addEventListener("click", refreshLiveData);
document.getElementById("exportButton").addEventListener("click", exportConfiguration);
document.getElementById("downloadDiagnosticsButton").addEventListener("click", () => downloadDiagnostics().catch(error => showNotice(`Diagnostics failed: ${error.message}`, "error")));
document.getElementById("importButton").addEventListener("click", () => document.getElementById("importFile").click());
document.getElementById("importFile").addEventListener("change", event => {
  const file = event.target.files[0];
  if (file) importConfiguration(file);
  event.target.value = "";
});
document.getElementById("viewerSearch").addEventListener("input", renderViewerInspector);
document.getElementById("runRatelabSimButton").addEventListener("click", runRatelabSim);
document.getElementById("patchTitle").addEventListener("input", event => { event.target.dataset.userEdited = "true"; renderPatchNotes(); });
document.getElementById("patchIntro").addEventListener("input", renderPatchNotes);
document.getElementById("patchExtra").addEventListener("input", renderPatchNotes);
document.getElementById("copyPatchButton").addEventListener("click", copyPatchNotes);
document.getElementById("markPublishedButton").addEventListener("click", markPatchPublished);
document.getElementById("saveRoleNamesButton").addEventListener("click", saveRoleNames);
document.getElementById("backupFilter").addEventListener("change", renderBackups);
document.getElementById("refreshBackupsButton").addEventListener("click", () => refreshBackupIndex().catch(error => showNotice(error.message, "error")));
document.getElementById("downloadBackupButton").addEventListener("click", downloadSelectedBackup);
document.getElementById("restoreBackupButton").addEventListener("click", restoreSelectedBackup);
for (const id of ["profileGameName", "profileAdminName", "profileRedemptionName", "profileItemSingular", "profileItemPlural", "profileCollectionSingular", "profileCollectionPlural", "profileCurrencyName", "profileCooldown", "profileDupProtection", "profileRedemptionCost"]) {
  document.getElementById(id).addEventListener("input", updateProfileFromInputs);
}
document.getElementById("saveProfileButton").addEventListener("click", saveSystemProfile);
document.getElementById("saveAppearanceButton").addEventListener("click", saveSystemProfile);
document.getElementById("resetProfileButton").addEventListener("click", resetSystemProfile);
document.getElementById("commandTestButton").addEventListener("click", runCommandTest);
document.getElementById("testAppwriteButton").addEventListener("click", testAppwriteConnection);
document.getElementById("saveAppwriteButton").addEventListener("click", saveAppwriteConnectionAndNotify);
document.getElementById("openDataFolderButton").addEventListener("click", openDataFolder);
document.getElementById("hideSystemCheckSetting").addEventListener("change", event => {
  if (event.target.checked) localStorage.setItem("circuitos.hideSystemCheck", "1");
  else localStorage.removeItem("circuitos.hideSystemCheck");
  applySystemCheckVisibility();
});
document.getElementById("commandTestInput").addEventListener("keydown", event => { if (event.key === "Enter") runCommandTest(); });
document.getElementById("regenerateSetupButton").addEventListener("click", () => generateStreamerBotSetup().catch(error => showNotice(error.message, "error")));
document.getElementById("saveMessagesButton").addEventListener("click", saveSystemProfile);
document.getElementById("resetAllMessagesButton").addEventListener("click", resetAllMessages);
document.querySelectorAll("[data-wizard-preset]").forEach(button => {
  button.addEventListener("click", () => selectWizardPreset(button.dataset.wizardPreset));
});
document.getElementById("wizardBackButton").addEventListener("click", () => setWizardStep(wizardStep - 1));
document.getElementById("wizardNextButton").addEventListener("click", () => {
  const error = validateWizardStep();
  if (error) { wizardSetError(error); return; }
  setWizardStep(wizardStep + 1);
});
document.getElementById("wizardCompleteButton").addEventListener("click", completeFirstRun);

document.addEventListener("keydown", event => {
  if (event.key === "Escape" && !document.getElementById("collectionImportModal").hidden) closeCollectionImport();
  if (event.key === "Escape" && !document.getElementById("eventImportModal").hidden) closeEventImport();
  if (event.key === "Escape" && !document.getElementById("profileSwitchModal").hidden) closeProfileSwitchConfirm();
  if (event.key === "Escape" && !document.getElementById("profileSwitcherDropdown").hidden) closeProfileSwitcher();
});

document.addEventListener("click", event => {
  const wrap = document.getElementById("profileSwitcherTrigger")?.closest(".profile-switcher-wrap");
  if (wrap && !wrap.contains(event.target)) closeProfileSwitcher();
});

document.getElementById("profileSwitcherTrigger").addEventListener("click", toggleProfileSwitcher);
document.getElementById("profileSwitcherManage").addEventListener("click", () => { closeProfileSwitcher(); switchView("profiles"); });

document.getElementById("createProfileButton").addEventListener("click", createProfile);
document.getElementById("exportModuleButton").addEventListener("click", exportModule);
document.getElementById("importModuleButton").addEventListener("click", () => document.getElementById("moduleImportFile").click());
document.getElementById("moduleImportFile").addEventListener("change", event => {
  const file = event.target.files[0];
  if (file) importModule(file);
  event.target.value = "";
});
document.getElementById("cancelProfileSwitchButton").addEventListener("click", closeProfileSwitchConfirm);
document.getElementById("confirmProfileSwitchButton").addEventListener("click", confirmProfileSwitch);
document.getElementById("saveOverlayButton").addEventListener("click", saveOverlayConfig);
document.getElementById("copyObsPathButton").addEventListener("click", () => {
  const path = document.getElementById("obsFilePath")?.textContent;
  if (!path || path === "(loading…)") return;
  navigator.clipboard.writeText(path).then(() => {
    const btn = document.getElementById("copyObsPathButton");
    const prev = btn.textContent;
    btn.textContent = "Copied!";
    setTimeout(() => { btn.textContent = prev; }, 1500);
  }).catch(() => {});
});
document.getElementById("refreshOverlayPreviewButton").addEventListener("click", () => {
  const frame = document.getElementById("overlayPreviewFrame");
  if (frame) frame.src = `/overlay/index.html?preview=1&t=${Date.now()}`;
});
document.querySelectorAll("[data-preview-state]").forEach(btn => {
  btn.addEventListener("click", () => {
    document.querySelectorAll("[data-preview-state]").forEach(b => b.classList.remove("active"));
    btn.classList.add("active");
    activeOverlayPreviewState = btn.dataset.previewState;
    const frame = document.getElementById("overlayPreviewFrame");
    if (frame?.contentWindow) {
      frame.contentWindow.postMessage({ type: "overlayPreviewState", state: btn.dataset.previewState }, "*");
    }
    renderStateColorFields();
  });
});
window.addEventListener("resize", scaleOverlayPreview);

window.addEventListener("beforeunload", event => {
  if (!dirty && !profileDirty) return;
  event.preventDefault();
  event.returnValue = "";
});

window.setInterval(() => {
  if (document.visibilityState !== "visible") return;
  refreshOperationalData().catch(error => console.warn(error.message));
}, 15000);

document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") {
    refreshOperationalData().catch(error => console.warn(error.message));
  }
});

loadConfiguration(true).catch(error => showNotice(error.message, "error"));







