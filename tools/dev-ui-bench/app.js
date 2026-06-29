const screens = [
  "Welcome Step 1",
  "Welcome Step 2",
  "Welcome Step 3",
  "Welcome Step 4",
  "Overview",
  "Game Profile",
  "Appearance",
  "Overlay Editor",
  "Messages",
  "Streamer.bot",
  "Main Collections",
  "Events",
  "Featured Boost",
  "Inventory",
  "Discord Roles",
  "Economy",
  "Patch Notes",
  "Backups",
  "Profiles",
  "Rate Lab"
];

const screenViewIds = {
  "Welcome Step 1": "wizard-step-1",
  "Welcome Step 2": "wizard-step-2",
  "Welcome Step 3": "wizard-step-3",
  "Welcome Step 4": "wizard-step-4",
  "Overview": "overviewView",
  "Game Profile": "brandingView",
  "Appearance": "appearanceView",
  "Overlay Editor": "overlayView",
  "Messages": "messagesView",
  "Streamer.bot": "setupView",
  "Main Collections": "collectionsView",
  "Events": "eventsView",
  "Featured Boost": "boostView",
  "Inventory": "viewersView",
  "Discord Roles": "rolesView",
  "Economy": "economyView",
  "Patch Notes": "patchnotesView",
  "Backups": "backupsView",
  "Profiles": "profilesView",
  "Rate Lab": "ratelabView"
};

const componentTypes = [
  { type: "button", label: "Button" },
  { type: "panel", label: "Panel / Card" },
  { type: "alert", label: "Alert" },
  { type: "metric", label: "Metric Chip" },
  { type: "field", label: "Field Row" },
  { type: "toggle", label: "Toggle" },
  { type: "toolbar", label: "Toolbar" }
];

const storageKey = "circuitos-ui-bench-draft-v1";
const defaultTheme = {
  bg: "#000d19",
  panel: "#061a2b",
  "panel-2": "#092239",
  line: "#193a55",
  red: "#ff1a24",
  "red-soft": "rgba(255, 26, 36, 0.13)",
  text: "#eef5fb",
  muted: "#8295a8",
  green: "#5ee5a0",
  amber: "#ffc857",
  blue: "#56a8ff",
  danger: "#ff6670"
};
const editableThemeKeys = Object.keys(defaultTheme);
let state = loadState();
let selectedId = state.components[0]?.id || "";

const els = {
  screenSelect: document.getElementById("screenSelect"),
  screenTitle: document.getElementById("screenTitle"),
  componentPalette: document.getElementById("componentPalette"),
  previewCanvas: document.getElementById("previewCanvas"),
  componentCount: document.getElementById("componentCount"),
  propertyTitle: document.getElementById("propertyTitle"),
  propertyEmpty: document.getElementById("propertyEmpty"),
  propertyForm: document.getElementById("propertyForm"),
  deleteComponentButton: document.getElementById("deleteComponentButton"),
  newDraftButton: document.getElementById("newDraftButton"),
  copyTicketButton: document.getElementById("copyTicketButton"),
  downloadTicketButton: document.getElementById("downloadTicketButton"),
  layoutFileInput: document.getElementById("layoutFileInput"),
  layoutPasteInput: document.getElementById("layoutPasteInput"),
  applyLayoutPasteButton: document.getElementById("applyLayoutPasteButton"),
  clearLayoutImportButton: document.getElementById("clearLayoutImportButton"),
  layoutStatus: document.getElementById("layoutStatus"),
  styleFileInput: document.getElementById("styleFileInput"),
  stylePasteInput: document.getElementById("stylePasteInput"),
  applyStylePasteButton: document.getElementById("applyStylePasteButton"),
  resetStyleButton: document.getElementById("resetStyleButton"),
  styleStatus: document.getElementById("styleStatus"),
  themeEditor: document.getElementById("themeEditor"),
  ticketOutput: document.getElementById("ticketOutput"),
  saveState: document.getElementById("saveState"),
  propLabel: document.getElementById("propLabel"),
  propId: document.getElementById("propId"),
  propLocation: document.getElementById("propLocation"),
  propStyle: document.getElementById("propStyle"),
  propSize: document.getElementById("propSize"),
  propHidden: document.getElementById("propHidden"),
  moveComponentUpButton: document.getElementById("moveComponentUpButton"),
  moveComponentDownButton: document.getElementById("moveComponentDownButton"),
  propHelp: document.getElementById("propHelp"),
  propVisible: document.getElementById("propVisible"),
  propDisabled: document.getElementById("propDisabled"),
  propBehavior: document.getElementById("propBehavior"),
  propSuccess: document.getElementById("propSuccess"),
  propError: document.getElementById("propError"),
  propAcceptance: document.getElementById("propAcceptance")
};

function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID();
  return `draft-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function defaultState() {
  return {
    screen: "Overview",
    layoutSource: "Blank mockup",
    lastLayoutImport: null,
    importedLayoutHtml: "",
    styleSource: "Default CircuitOS bench style",
    importedStyleCss: "",
    lastImport: null,
    theme: { ...defaultTheme },
    components: [
      {
        id: uuid(),
        type: "button",
        label: "Open Twitch Settings",
        idSuggestion: "openTwitchSettingsButton",
        location: "Overview > Action Center",
        style: "secondary",
        help: "Quick access when native Twitch needs setup or re-login.",
        visible: "Always visible on Overview.",
        disabled: "",
        behavior: "Navigate to the Twitch Settings view.",
        success: "Twitch Settings screen opens.",
        error: "No error state needed unless navigation fails.",
        acceptance: "Button appears in Action Center.\nClicking does not dirty the catalog.\nKeyboard focus is visible."
      }
    ]
  };
}

function loadState() {
  try {
    const parsed = JSON.parse(localStorage.getItem(storageKey) || "null");
    if (parsed && Array.isArray(parsed.components)) {
      return {
        ...parsed,
        layoutSource: parsed.layoutSource || "Blank mockup",
        lastLayoutImport: parsed.lastLayoutImport || null,
        importedLayoutHtml: parsed.importedLayoutHtml || "",
        styleSource: parsed.styleSource || "Default CircuitOS bench style",
        importedStyleCss: parsed.importedStyleCss || "",
        lastImport: parsed.lastImport || null,
        theme: normalizeTheme(parsed.theme)
      };
    }
  } catch {
    // Ignore malformed local drafts and start fresh.
  }
  return defaultState();
}

function normalizeTheme(theme) {
  return { ...defaultTheme, ...(theme || {}) };
}

function saveState() {
  localStorage.setItem(storageKey, JSON.stringify(state));
  els.saveState.textContent = "Draft saved locally";
}

function createElement(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function selectedComponent() {
  return state.components.find(component => component.id === selectedId) || null;
}

function titleCaseType(type) {
  return componentTypes.find(item => item.type === type)?.label || type;
}

function suggestId(type) {
  const base = type === "button" ? "newActionButton" : `new${type[0].toUpperCase()}${type.slice(1)}`;
  const used = new Set(state.components.map(component => component.idSuggestion));
  if (!used.has(base)) return base;
  let index = 2;
  while (used.has(`${base}${index}`)) index += 1;
  return `${base}${index}`;
}

function addComponent(type) {
  const component = {
    id: uuid(),
    type,
    label: titleCaseType(type),
    idSuggestion: suggestId(type),
    location: `${state.screen} > New placement`,
    style: type === "alert" ? "info" : "secondary",
    help: "",
    visible: "",
    disabled: "",
    behavior: "",
    success: "",
    error: "",
    acceptance: ""
  };
  state.components.push(component);
  selectedId = component.id;
  render();
}

function updateSelected(field, value) {
  const component = selectedComponent();
  if (!component) return;
  component[field] = value;
  render();
}

function deleteSelected() {
  if (!selectedId) return;
  state.components = state.components.filter(component => component.id !== selectedId);
  selectedId = state.components[0]?.id || "";
  render();
}

function resetDraft() {
  if (!confirm("Start a new UI Bench draft? This clears the local draft only.")) return;
  state = defaultState();
  selectedId = state.components[0]?.id || "";
  render();
}

function cleanText(value) {
  return (value || "").replace(/\s+/g, " ").trim();
}

function screenRoot(documentModel) {
  const viewId = screenViewIds[state.screen];
  if (!viewId) return null;
  if (viewId.startsWith("wizard-step-")) {
    const step = viewId.replace("wizard-step-", "");
    return documentModel.querySelector(`[data-wizard-step="${step}"]`);
  }
  return documentModel.getElementById(viewId);
}

function selectorForNode(node, root) {
  if (!node || node === root) return "";
  if (node.id) return `#${CSS.escape(node.id)}`;
  const parts = [];
  let current = node;
  while (current && current !== root && current.nodeType === Node.ELEMENT_NODE) {
    const tag = current.tagName.toLowerCase();
    const parent = current.parentElement;
    if (!parent) break;
    const siblings = Array.from(parent.children).filter(child => child.tagName === current.tagName);
    const index = siblings.indexOf(current) + 1;
    parts.unshift(`${tag}:nth-of-type(${index})`);
    current = parent;
  }
  return parts.join(" > ");
}

function labelTarget(node) {
  if (!node) return null;
  if (node.matches("button")) return node;
  if (node.matches("label")) return node.querySelector("span") || node;
  if (node.matches("article, .panel")) return node.querySelector("h1, h2, h3, .panel-kicker") || node;
  if (node.matches('[class*="toolbar"], .top-actions, .toolbar-actions, .profile-actions, .backup-actions, .patch-actions, .panel-header-actions')) {
    return node.querySelector("h1, h2, h3, p") || node;
  }
  return node;
}

function nearestPanelName(node) {
  const panel = node.closest?.(".panel, article, section");
  if (!panel) return state.screen;
  return cleanText(panel.querySelector("h1, h2, h3")?.textContent)
    || cleanText(panel.querySelector(".panel-kicker, .subsection-title")?.textContent)
    || state.screen;
}

function buttonStyle(button) {
  if (button.classList.contains("primary")) return "primary";
  if (button.classList.contains("danger")) return "danger";
  if (button.classList.contains("secondary")) return "secondary";
  return "secondary";
}

function importedComponent(type, label, node, extras = {}) {
  const idSuggestion = node?.id || node?.getAttribute?.("data-view") || node?.getAttribute?.("data-jump-view")
    || node?.getAttribute?.("data-preview-state") || "";
  const panelName = nearestPanelName(node);
  return {
    id: uuid(),
    type,
    label: cleanText(label) || titleCaseType(type),
    idSuggestion,
    location: `${state.screen} > ${panelName}`,
    style: extras.style || "secondary",
    help: extras.help || `Imported from current admin layout${node?.className ? ` (${node.className})` : ""}.`,
    visible: extras.visible || "Imported from the selected screen's static HTML.",
    disabled: extras.disabled || "",
    behavior: extras.behavior || "",
    hidden: extras.hidden || false,
    size: extras.size || "",
    sourceSelector: extras.sourceSelector || "",
    success: "",
    error: "",
    acceptance: extras.acceptance || "Imported component appears in the proposed screen layout.\nAI wiring preserves the intended control id/class where practical.",
    ...extras
  };
}

function importAdminLayout(htmlText, sourceName) {
  const documentModel = new DOMParser().parseFromString(htmlText || "", "text/html");
  const parseError = documentModel.querySelector("parsererror");
  if (parseError) {
    els.layoutStatus.textContent = "Could not parse that HTML file.";
    return;
  }

  for (const [screen, viewId] of Object.entries(screenViewIds)) {
    const detected = viewId.startsWith("wizard-step-")
      ? documentModel.querySelector(`[data-wizard-step="${viewId.replace("wizard-step-", "")}"]`)
      : documentModel.getElementById(viewId);
    if (detected) {
      state.screen = screen;
      break;
    }
  }

  const viewId = screenViewIds[state.screen];
  const view = screenRoot(documentModel);
  if (!view) {
    els.layoutStatus.textContent = `No ${state.screen} view found in that HTML.`;
    return;
  }

  const components = [];
  const seen = new Set();
  const add = component => {
    const key = `${component.type}|${component.idSuggestion}|${component.label}|${component.location}`;
    if (seen.has(key)) return;
    seen.add(key);
    components.push(component);
  };
  const withSource = (node, extras = {}) => ({
    ...extras,
    sourceSelector: selectorForNode(node, view)
  });

  for (const toolbar of view.querySelectorAll('[class*="toolbar"], .top-actions, .toolbar-actions, .profile-actions, .backup-actions, .patch-actions, .panel-header-actions')) {
    const title = cleanText(toolbar.querySelector("h1, h2, h3")?.textContent)
      || cleanText(toolbar.parentElement?.querySelector("h1, h2, h3")?.textContent)
      || "Toolbar";
    add(importedComponent("toolbar", title, toolbar, withSource(toolbar, {
      help: "Imported toolbar/action row from current admin layout.",
      behavior: "Use this to plan moving, grouping, or renaming the controls inside this row."
    })));
  }

  for (const panel of view.querySelectorAll("article.panel, .panel")) {
    const title = cleanText(panel.querySelector("h1, h2, h3")?.textContent)
      || cleanText(panel.querySelector(".panel-kicker, .subsection-title")?.textContent)
      || "Panel";
    add(importedComponent("panel", title, panel, withSource(panel, {
      help: "Imported panel/card from current admin layout.",
      behavior: "Use this to plan card placement, grouping, or heading changes."
    })));
  }

  for (const field of view.querySelectorAll("label.field, label[class*='filter'], label[class*='search'], label[class*='count']")) {
    const label = cleanText(field.querySelector("span")?.textContent) || cleanText(field.textContent);
    const control = field.querySelector("input, select, textarea");
    add(importedComponent("field", label, control || field, withSource(field, {
      idSuggestion: control?.id || field.id || "",
      help: "Imported editable field/control from current admin layout.",
      behavior: "Describe validation, save behavior, and placement changes here."
    })));
  }

  for (const toggle of view.querySelectorAll("label.toggle")) {
    const label = cleanText(toggle.textContent) || toggle.querySelector("input")?.id || "Toggle";
    const input = toggle.querySelector("input");
    add(importedComponent("toggle", label, input || toggle, withSource(toggle, {
      idSuggestion: input?.id || toggle.id || "",
      help: "Imported toggle from current admin layout.",
      behavior: "Describe what should happen when this toggle changes."
    })));
  }

  for (const button of view.querySelectorAll("button")) {
    const label = cleanText(button.textContent) || button.id || "Button";
    add(importedComponent("button", label, button, withSource(button, {
      style: buttonStyle(button),
      disabled: button.disabled ? "Disabled by default in current HTML." : "",
      behavior: button.dataset.jumpView
        ? `Navigate to ${button.dataset.jumpView}.`
        : button.dataset.previewState
          ? `Switch overlay preview state to ${button.dataset.previewState}.`
          : "Imported button; describe the intended click behavior or placement change."
    })));
  }

  if (!components.length) {
    els.layoutStatus.textContent = `No importable controls found in ${state.screen}.`;
    return;
  }

  state.components = components;
  state.layoutSource = sourceName || "Imported admin index.html";
  state.importedLayoutHtml = htmlText || "";
  state.lastLayoutImport = {
    source: state.layoutSource,
    screen: state.screen,
    viewId,
    count: components.length
  };
  if (els.layoutPasteInput) els.layoutPasteInput.value = "";
  selectedId = state.components[0]?.id || "";
  render();
}

function clearLayoutImport() {
  state.layoutSource = "Blank mockup";
  state.lastLayoutImport = null;
  state.importedLayoutHtml = "";
  state.components = [];
  selectedId = "";
  render();
}

function isHexColor(value) {
  return /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test((value || "").trim());
}

function parseCssVariables(cssText) {
  const parsed = {};
  const regex = /--([a-zA-Z0-9-]+)\s*:\s*([^;]+);/g;
  let match;
  while ((match = regex.exec(cssText || "")) !== null) {
    const key = match[1].trim();
    if (editableThemeKeys.includes(key)) {
      parsed[key] = match[2].trim();
    }
  }
  return parsed;
}

function applyThemeToDocument() {
  state.theme = normalizeTheme(state.theme);
  for (const [key, value] of Object.entries(state.theme)) {
    document.documentElement.style.setProperty(`--${key}`, value);
  }
}

function importTheme(cssText, sourceName) {
  const imported = parseCssVariables(cssText);
  const importedKeys = Object.keys(imported);
  state.importedStyleCss = cssText || "";
  if (!importedKeys.length) {
    if (cssText && cssText.trim()) {
      state.styleSource = sourceName || "Pasted CSS";
      state.lastImport = {
        source: state.styleSource,
        importedCount: 0,
        changedCount: 0,
        keys: [],
        changedKeys: []
      };
      els.stylePasteInput.value = "";
      render();
      return;
    }
    els.styleStatus.textContent = "No known CircuitOS theme variables found.";
    return;
  }
  const currentTheme = normalizeTheme(state.theme);
  const changedKeys = importedKeys.filter(key => currentTheme[key] !== imported[key]);
  state.theme = normalizeTheme({ ...state.theme, ...imported });
  state.styleSource = sourceName || "Pasted CSS";
  state.lastImport = {
    source: state.styleSource,
    importedCount: importedKeys.length,
    changedCount: changedKeys.length,
    keys: importedKeys,
    changedKeys
  };
  els.stylePasteInput.value = "";
  render();
}

function resetTheme() {
  state.theme = { ...defaultTheme };
  state.styleSource = "Default CircuitOS bench style";
  state.importedStyleCss = "";
  state.lastImport = null;
  render();
}

function updateThemeValue(key, value) {
  state.theme = normalizeTheme(state.theme);
  state.theme[key] = value;
  state.styleSource = "Edited in UI Bench";
  state.lastImport = null;
  applyThemeToDocument();
  els.styleStatus.textContent = "Style source: Edited in UI Bench";
  renderTicket();
  saveState();
}

function renderScreenOptions() {
  if (!screens.includes(state.screen)) state.screen = "Overview";
  els.screenSelect.replaceChildren();
  for (const screen of screens) {
    const option = createElement("option", "", screen);
    option.value = screen;
    option.selected = screen === state.screen;
    els.screenSelect.append(option);
  }
}

function renderPalette() {
  els.componentPalette.replaceChildren();
  for (const item of componentTypes) {
    const button = createElement("button", "", `Add ${item.label}`);
    button.type = "button";
    button.addEventListener("click", () => addComponent(item.type));
    els.componentPalette.append(button);
  }
}

function groupForScreen(screen) {
  if (["Game Profile", "Appearance", "Overlay Editor", "Messages"].includes(screen)) return "Configure";
  if (["Main Collections", "Events", "Featured Boost"].includes(screen)) return "Catalog";
  if (["Inventory", "Discord Roles", "Economy", "Patch Notes"].includes(screen)) return "Community";
  if (["Profiles", "Rate Lab", "Backups"].includes(screen)) return "Tools";
  if (screen === "Streamer.bot") return "Streamer.bot";
  return "Overview";
}

function renderLayoutStatus() {
  if (state.lastLayoutImport) {
    els.layoutStatus.textContent =
      `Imported ${state.lastLayoutImport.count} component${state.lastLayoutImport.count === 1 ? "" : "s"} from ` +
      `${state.lastLayoutImport.source} > ${state.lastLayoutImport.viewId}.`;
  } else {
    els.layoutStatus.textContent = "Import the current build layout to edit its controls.";
  }
}

function findRenderedNode(root, component) {
  if (!component.sourceSelector) return null;
  try {
    if (component.sourceSelector.startsWith("#")) return root.querySelector(component.sourceSelector);
    return root.querySelector(component.sourceSelector);
  } catch {
    return null;
  }
}

function moveComponent(componentId, direction) {
  const index = state.components.findIndex(component => component.id === componentId);
  const target = index + direction;
  if (index < 0 || target < 0 || target >= state.components.length) return;
  const [component] = state.components.splice(index, 1);
  state.components.splice(target, 0, component);
  selectedId = component.id;
  render();
}

function appendNode(doc, parent, tag, className, text) {
  if (!parent) return null;
  const node = doc.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  parent.append(node);
  return node;
}

function hydrateOverviewFakeData(root) {
  const doc = root.ownerDocument;
  const actionCenter = root.querySelector("#actionCenter");
  if (actionCenter && !actionCenter.children.length) {
    const item = appendNode(doc, actionCenter, "div", "action-item warning");
    appendNode(doc, item, "strong", "", "Season 0: System Reboot cannot be pulled");
    appendNode(doc, item, "span", "", "Its active collection weight is zero.");
  }

  const rateChart = root.querySelector("#rateChart");
  if (rateChart && !rateChart.children.length) {
    const rows = [
      ["Basic Collection", 39.53],
      ["Power Collection", 60.47],
      ["Advanced Collection", 0],
      ["Broken Collection", 0],
      ["Quantum Collection", 0],
      ["Season 0: System Reboot", 0]
    ];
    for (const [name, pct] of rows) {
      const row = appendNode(doc, rateChart, "div", "rate-row");
      appendNode(doc, row, "span", "rate-name", name);
      const input = appendNode(doc, row, "input", "rate-slider");
      input.type = "range";
      input.value = String(pct);
      input.disabled = true;
      appendNode(doc, row, "div", "rate-number", `${pct.toFixed(2)}%`);
    }
  }

  const health = root.querySelector("#overviewCollections");
  if (health && !health.children.length) {
    const header = appendNode(doc, health, "div", "health-table-row header");
    ["COLLECTION", "OWNERS", "DUPES", "SCRAP", "PULL RATE"].forEach(label => appendNode(doc, header, "span", "", label));
    const rows = [
      ["Basic Collection", 6, 28, 28, "39.53%"],
      ["Power Collection", 4, 7, 14, "60.47%"],
      ["Advanced Collection", 4, 4, 12, "0.00%"],
      ["Broken Collection", 5, 5, 25, "0.00%"],
      ["Quantum Collection", 4, 0, 0, "0.00%"],
      ["Season 0: System Reboot", 6, 2, 4, "0.00%"]
    ];
    for (const rowData of rows) {
      const row = appendNode(doc, health, "div", "health-table-row");
      appendNode(doc, row, "strong", "", rowData[0]);
      rowData.slice(1).forEach(value => appendNode(doc, row, "span", "", String(value)));
    }
  }

  const events = root.querySelector("#overviewEvents");
  if (events && !events.children.length) {
    const row = appendNode(doc, events, "div", "timeline-row");
    const copy = appendNode(doc, row, "div", "timeline-copy");
    appendNode(doc, copy, "strong", "", "Season 0: System Reboot");
    appendNode(doc, copy, "small", "", "Jun 20, 2026 - Jul 31, 2026");
    appendNode(doc, row, "span", "event-status active", "ACTIVE");
    appendNode(doc, row, "span", "timeline-date", "Ends in 35 days");
  }

  const economy = root.querySelector("#overviewEconomy");
  if (economy && !economy.children.length) {
    [["SCRAP IN CIRCULATION", 321], ["UNCLAIMED", 83], ["DUPLICATE UNITS", 46], ["MEDIAN BALANCE", 0]].forEach(([label, value]) => {
      const item = appendNode(doc, economy, "div", "pulse-item");
      appendNode(doc, item, "span", "", label);
      appendNode(doc, item, "strong", "", String(value));
    });
  }

  const leaders = root.querySelector("#overviewLeaders");
  if (leaders && !leaders.children.length) {
    [["#1", "deftinnja_", "30 unique / 4 complete"], ["#2", "shortcircuit_tv", "18 unique / 2 complete"]].forEach(([rank, name, score]) => {
      const row = appendNode(doc, leaders, "div", "leader-row");
      appendNode(doc, row, "span", "leader-rank", rank);
      appendNode(doc, row, "strong", "", name);
      appendNode(doc, row, "span", "collector-score", score);
    });
  }
}

function hydrateFakeData(root) {
  if (state.screen === "Overview") hydrateOverviewFakeData(root);
}

function canvasEditorCss() {
  return `
    :host { display: block; color: var(--text, #eef5fb); }
    .visual-view { display: block !important; min-width: 1080px; padding: 0; }
    .wizard-step { display: block !important; }
    .view { display: block !important; }
    .canvas-editable { position: relative; outline: 1px dashed rgba(86, 168, 255, 0.35) !important; outline-offset: 3px; }
    .canvas-editable:hover { outline-color: rgba(86, 168, 255, 0.8) !important; box-shadow: 0 0 0 2px rgba(86, 168, 255, 0.12) !important; }
    .canvas-selected { outline: 2px solid var(--red, #ff1a24) !important; box-shadow: 0 0 0 3px rgba(255, 26, 36, 0.18), 0 0 18px rgba(255, 26, 36, 0.18) !important; }
    .canvas-hidden { opacity: 0.28 !important; filter: grayscale(0.7); }
    .canvas-size-compact { max-width: 320px !important; }
    .canvas-size-half { width: min(50%, 520px) !important; }
    .canvas-size-wide { grid-column: span 2 !important; min-width: min(100%, 620px); }
    .canvas-size-full { grid-column: 1 / -1 !important; width: 100% !important; }
    button, input, select, textarea { pointer-events: none; }
  `;
}

function renderVisualCanvas() {
  const documentModel = new DOMParser().parseFromString(state.importedLayoutHtml || "", "text/html");
  const sourceRoot = screenRoot(documentModel);
  if (!sourceRoot) return false;

  const canvasShell = createElement("div", "visual-canvas");
  const styleMode = state.importedStyleCss ? "using imported styles.css" : "using fallback canvas styling";
  const hint = createElement("div", "canvas-hint", `Click anything in this imported screen to edit it. Size/hide controls preview on the canvas; move controls export layout intent without disturbing source order. Canvas is ${styleMode}.`);
  const appShell = createElement("div", "visual-app-shell");
  const viewClone = sourceRoot.cloneNode(true);
  viewClone.hidden = false;
  viewClone.classList.add("visual-view");
  hydrateFakeData(viewClone);

  const nodeByComponent = new Map();
  for (const component of state.components) {
    const node = findRenderedNode(viewClone, component);
    if (!node) continue;
    nodeByComponent.set(component.id, node);
    node.classList.add("canvas-editable");
    if (component.id === selectedId) node.classList.add("canvas-selected");
    if (component.hidden) node.classList.add("canvas-hidden");
    if (component.size) node.classList.add(`canvas-size-${component.size}`);
    node.dataset.componentId = component.id;
    node.draggable = true;
    const target = labelTarget(node);
    if (target && component.label) target.textContent = component.label;
  }

  viewClone.addEventListener("click", event => {
    const target = event.target.closest(".canvas-editable");
    if (!target) return;
    event.preventDefault();
    event.stopPropagation();
    selectedId = target.dataset.componentId || "";
    render();
  });

  viewClone.addEventListener("dragstart", event => {
    const target = event.target.closest(".canvas-editable");
    if (!target) return;
    event.dataTransfer.setData("text/plain", target.dataset.componentId || "");
  });

  viewClone.addEventListener("dragover", event => {
    if (event.target.closest(".canvas-editable")) event.preventDefault();
  });

  viewClone.addEventListener("drop", event => {
    const target = event.target.closest(".canvas-editable");
    const draggedId = event.dataTransfer.getData("text/plain");
    if (!target || !draggedId || target.dataset.componentId === draggedId) return;
    event.preventDefault();
    const from = state.components.findIndex(component => component.id === draggedId);
    const to = state.components.findIndex(component => component.id === target.dataset.componentId);
    if (from < 0 || to < 0) return;
    const [component] = state.components.splice(from, 1);
    state.components.splice(to, 0, component);
    selectedId = component.id;
    render();
  });

  const shadow = appShell.attachShadow({ mode: "open" });
  const style = document.createElement("style");
  style.textContent = `${state.importedStyleCss || ""}\n${canvasEditorCss()}`;
  shadow.append(style, viewClone);
  canvasShell.append(hint, appShell);
  els.previewCanvas.replaceChildren(canvasShell);
  return true;
}

function renderPreview() {
  els.screenTitle.textContent = state.screen;
  els.componentCount.textContent = `${state.components.length} component${state.components.length === 1 ? "" : "s"}`;

  if (state.importedLayoutHtml && renderVisualCanvas()) return;

  const screen = createElement("div", "mock-screen");
  const sidebar = createElement("div", "mock-sidebar");
  sidebar.append(createElement("div", "eyebrow", "CIRCUITOS"));
  for (const name of ["Overview", "Configure", "Catalog", "Community", "Tools"]) {
    sidebar.append(createElement("div", `mock-nav-item ${name === groupForScreen(state.screen) ? "active" : ""}`, name));
  }

  const content = createElement("div", "mock-content");
  const toolbar = createElement("div", "mock-toolbar");
  toolbar.append(createElement("div", "panel-kicker", state.screen.toUpperCase()));
  toolbar.append(createElement("div", "metric-chip", "Mock preview"));
  content.append(toolbar);

  const grid = createElement("div", "mock-grid");
  for (const component of state.components) {
    const card = createElement("button", `component-card ${component.id === selectedId ? "selected" : ""}`);
    card.type = "button";
    card.addEventListener("click", () => {
      selectedId = component.id;
      render();
    });
    card.append(createElement("span", `component-style ${component.style}`, component.style || "secondary"));
    card.append(createElement("div", "component-type", titleCaseType(component.type)));
    card.append(createElement("strong", "", component.label || "Untitled component"));
    card.append(createElement("p", "", component.help || component.location || "Describe how this should behave."));
    grid.append(card);
  }

  if (!state.components.length) {
    grid.append(createElement("div", "empty-state", "Add a component from the palette to start the screen proposal."));
  }

  content.append(grid);
  screen.append(sidebar, content);
  els.previewCanvas.replaceChildren(screen);
}

function renderProperties() {
  const component = selectedComponent();
  const hasSelection = Boolean(component);

  els.propertyEmpty.hidden = hasSelection;
  els.propertyForm.hidden = !hasSelection;
  els.deleteComponentButton.disabled = !hasSelection;

  if (!component) {
    els.propertyTitle.textContent = "Select a component";
    return;
  }

  els.propertyTitle.textContent = titleCaseType(component.type);
  els.propLabel.value = component.label || "";
  els.propId.value = component.idSuggestion || "";
  els.propLocation.value = component.location || "";
  els.propStyle.value = component.style || "secondary";
  els.propSize.value = component.size || "";
  els.propHidden.checked = Boolean(component.hidden);
  els.propHelp.value = component.help || "";
  els.propVisible.value = component.visible || "";
  els.propDisabled.value = component.disabled || "";
  els.propBehavior.value = component.behavior || "";
  els.propSuccess.value = component.success || "";
  els.propError.value = component.error || "";
  els.propAcceptance.value = component.acceptance || "";
}

function renderThemeEditor() {
  applyThemeToDocument();
  if (state.lastImport) {
    els.styleStatus.textContent =
      `Imported ${state.lastImport.importedCount} variable${state.lastImport.importedCount === 1 ? "" : "s"} from ` +
      `${state.lastImport.source}; ${state.lastImport.changedCount} changed.`;
  } else {
    els.styleStatus.textContent = `Style source: ${state.styleSource || "Default CircuitOS bench style"}`;
  }
  els.themeEditor.replaceChildren();

  for (const key of editableThemeKeys) {
    const row = createElement("label", "theme-row");
    if (state.lastImport?.keys?.includes(key)) row.classList.add("imported");
    if (state.lastImport?.changedKeys?.includes(key)) row.classList.add("changed");
    const label = createElement("span", "", `--${key}`);
    const input = createElement("input");
    input.type = "text";
    input.value = state.theme[key] || "";
    input.addEventListener("input", event => updateThemeValue(key, event.target.value));

    row.append(label);
    if (isHexColor(state.theme[key])) {
      const color = createElement("input", "theme-color");
      color.type = "color";
      color.value = state.theme[key];
      color.addEventListener("input", event => updateThemeValue(key, event.target.value));
      row.append(color);
    } else {
      row.classList.add("no-color");
    }
    row.append(input);
    els.themeEditor.append(row);
  }
}

function bulletLines(text) {
  return (text || "")
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean)
    .map(line => `- ${line}`)
    .join("\n");
}

function componentTicket(component, index) {
  return [
    `### Component ${index + 1}: ${component.label || "Untitled"}`,
    "",
    `Component: ${titleCaseType(component.type)}`,
    `Label: ${component.label || ""}`,
    `ID suggestion: ${component.idSuggestion || ""}`,
    `Location: ${component.location || ""}`,
    `Style: ${component.style || "secondary"}`,
    `Proposal order: ${index + 1}`,
    `Canvas size: ${component.size || "default"}`,
    `Hidden in proposal: ${component.hidden ? "yes" : "no"}`,
    `Source selector: ${component.sourceSelector || "Not imported from layout"}`,
    `Visible when: ${component.visible || "Not specified"}`,
    `Disabled when: ${component.disabled || "Not specified"}`,
    `Click behavior: ${component.behavior || "Not specified"}`,
    `Success state: ${component.success || "Not specified"}`,
    `Error state: ${component.error || "Not specified"}`,
    "",
    "Helper text:",
    component.help || "Not specified",
    "",
    "Acceptance checks:",
    bulletLines(component.acceptance) || "- Not specified"
  ].join("\n");
}

function buildTicket() {
  const body = state.components.length
    ? state.components.map(componentTicket).join("\n\n")
    : "No components added yet.";
  const themeLines = editableThemeKeys
    .map(key => `- --${key}: ${state.theme?.[key] || defaultTheme[key]}`)
    .join("\n");

  return [
    "## UI Wiring Ticket",
    "",
    `Screen: ${state.screen}`,
    "Source: CircuitOS UI Bench",
    "Mode: Proposal-only; no user data or production source was edited.",
    `Layout source: ${state.layoutSource || "Blank mockup"}`,
    "",
    body,
    "",
    "## Imported Theme Variables",
    "",
    `Style source: ${state.styleSource || "Default CircuitOS bench style"}`,
    themeLines,
    "",
    "## Implementation Notes",
    "",
    "- Wire only after reviewing the intended behavior.",
    "- Keep changes scoped to the named screen/components.",
    "- Add browser/UI verification when the real app is changed."
  ].join("\n");
}

async function copyTicket() {
  const ticket = buildTicket();
  try {
    await navigator.clipboard.writeText(ticket);
    els.saveState.textContent = "Ticket copied";
  } catch {
    els.ticketOutput.focus();
    els.ticketOutput.select();
    els.saveState.textContent = "Copy blocked; select text manually";
  }
}

function downloadTicket() {
  const safeScreen = state.screen.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  const blob = new Blob([buildTicket()], { type: "text/markdown;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = createElement("a");
  link.href = url;
  link.download = `ui-ticket-${safeScreen || "screen"}.md`;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function renderTicket() {
  els.ticketOutput.value = buildTicket();
}

function render() {
  state.theme = normalizeTheme(state.theme);
  renderScreenOptions();
  renderLayoutStatus();
  renderThemeEditor();
  renderPreview();
  renderProperties();
  renderTicket();
  saveState();
}

function bind() {
  els.screenSelect.addEventListener("change", () => {
    state.screen = els.screenSelect.value;
    for (const component of state.components) {
      if (!component.location || component.location.includes("> New placement")) {
        component.location = `${state.screen} > New placement`;
      }
    }
    render();
  });

  els.deleteComponentButton.addEventListener("click", deleteSelected);
  els.newDraftButton.addEventListener("click", resetDraft);
  els.copyTicketButton.addEventListener("click", copyTicket);
  els.downloadTicketButton.addEventListener("click", downloadTicket);
  els.moveComponentUpButton.addEventListener("click", () => moveComponent(selectedId, -1));
  els.moveComponentDownButton.addEventListener("click", () => moveComponent(selectedId, 1));
  els.applyLayoutPasteButton.addEventListener("click", () => importAdminLayout(els.layoutPasteInput.value, "Pasted layout HTML"));
  els.clearLayoutImportButton.addEventListener("click", clearLayoutImport);
  els.layoutFileInput.addEventListener("change", event => {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.addEventListener("load", () => importAdminLayout(String(reader.result || ""), file.name));
    reader.addEventListener("error", () => {
      els.layoutStatus.textContent = "Could not read that HTML file.";
    });
    reader.readAsText(file);
    event.target.value = "";
  });
  els.applyStylePasteButton.addEventListener("click", () => importTheme(els.stylePasteInput.value, "Pasted CSS"));
  els.resetStyleButton.addEventListener("click", resetTheme);
  els.styleFileInput.addEventListener("change", event => {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.addEventListener("load", () => importTheme(String(reader.result || ""), file.name));
    reader.addEventListener("error", () => {
      els.styleStatus.textContent = "Could not read that CSS file.";
    });
    reader.readAsText(file);
    event.target.value = "";
  });

  const mappings = [
    ["propLabel", "label"],
    ["propId", "idSuggestion"],
    ["propLocation", "location"],
    ["propStyle", "style"],
    ["propSize", "size"],
    ["propHelp", "help"],
    ["propVisible", "visible"],
    ["propDisabled", "disabled"],
    ["propBehavior", "behavior"],
    ["propSuccess", "success"],
    ["propError", "error"],
    ["propAcceptance", "acceptance"]
  ];

  for (const [elementKey, field] of mappings) {
    els[elementKey].addEventListener("input", event => updateSelected(field, event.target.value));
  }
  els.propHidden.addEventListener("change", event => updateSelected("hidden", event.target.checked));
}

renderPalette();
bind();
render();
