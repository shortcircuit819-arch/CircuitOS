const tracker = document.getElementById("tracker");
const viewerName = document.getElementById("viewerName");
const collectionName = document.getElementById("collectionName");
const partName = document.getElementById("partName");
const statusBadge = document.getElementById("statusBadge");
const progressText = document.getElementById("progressText");
const progressBar = document.getElementById("progressBar");
const tags = document.getElementById("tags");

const previewMode = new URLSearchParams(window.location.search).get("preview") === "1";
let lastUpdate = "";
let hideTimer = null;
let overlayConfig = null;
let activePreviewState = "normal";

const defaultOverlayConfig = {
  schemaVersion: 1,
  enabled: true,
  layout: { position: "bottom-center", width: 1500, minHeight: 178, offsetX: 0, offsetY: 54, barHeight: 8 },
  timing: { displaySeconds: 8, enterMilliseconds: 620, exitMilliseconds: 430 },
  appearance: {
    backgroundColor: "#00101f", panelColor: "#06233e", accentColor: "#ff1821",
    textColor: "#f8fbff", mutedColor: "#8094ad",
    labelColor: "#ff3b43", barColor: "#ff1821",
    backgroundImage: "", backgroundOpacity: 0.98
  },
  typography: { fontFamily: "Segoe UI", viewerNameSize: 38, partNameSize: 34, labelSize: 14 },
  content: { showCollection: true, showProgress: true, showCircuitOSBranding: true },
  animation: { style: "slide" },
  stateColors: {
    rare: { accentColor: "", labelColor: "", barColor: "" },
    complete: { accentColor: "", labelColor: "", barColor: "" },
    duplicate: { accentColor: "", labelColor: "", barColor: "" }
  },
  labels: {
    eyebrow: "CIRCUIT SCAN", componentAcquired: "COMPONENT ACQUIRED",
    collectionProgress: "COLLECTION PROGRESS", newItem: "NEW COMPONENT",
    collectionComplete: "COLLECTION COMPLETE", duplicate: "DUPLICATE"
  }
};

function makeDummyState(kind) {
  const base = {
    updatedAtUtc: new Date().toISOString(),
    visibleUntilUtc: new Date(Date.now() + 3600000).toISOString(),
    viewerName: "shortcircuit_tv", collectionName: "Collection Name", partName: "Item Name",
    ownedCount: 42, totalCount: 100, quantity: 1,
    isDuplicate: false, newlyCompleted: false, rareLabel: "", featuredBoost: "",
    variantLabels: [], tierLabel: ""
  };
  if (kind === "rare") return { ...base, partName: "Rare Item", rareLabel: "RARE PULL", tierLabel: "RARE" };
  if (kind === "complete") return { ...base, partName: "Final Item", ownedCount: 100, newlyCompleted: true };
  if (kind === "duplicate") return { ...base, quantity: 3, isDuplicate: true };
  if (kind === "normal") return { ...base, variantLabels: ["SHINY"], tierLabel: "COMMON" };
  return base;
}

function hexToRgb(hex) {
  const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
  return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

function numberInRange(value, fallback, minimum, maximum) {
  const number = Number(value);
  return Number.isFinite(number) && number >= minimum && number <= maximum ? number : fallback;
}

function validColor(value, fallback) {
  return /^#[0-9a-f]{6}$/i.test(String(value || "")) ? value : fallback;
}

function normalizeStateColor(sc) {
  return {
    accentColor: validColor(sc?.accentColor, ""),
    labelColor: validColor(sc?.labelColor, ""),
    barColor: validColor(sc?.barColor, "")
  };
}

function normalizeOverlayConfig(value = {}) {
  const source = value && typeof value === "object" ? value : {};
  const layout = source.layout || {};
  const timing = source.timing || {};
  const appearance = source.appearance || {};
  const content = source.content || {};
  const stateColors = source.stateColors || {};
  const positions = ["bottom-center", "bottom-left", "bottom-right", "top-left", "top-right"];
  return {
    schemaVersion: 1,
    enabled: source.enabled !== false,
    layout: {
      position: positions.includes(layout.position) ? layout.position : defaultOverlayConfig.layout.position,
      width: numberInRange(layout.width, 1500, 320, 1600),
      minHeight: numberInRange(layout.minHeight, 178, 100, 600),
      offsetX: numberInRange(layout.offsetX, 0, 0, 1000),
      offsetY: numberInRange(layout.offsetY, 54, 0, 1000),
      barHeight: numberInRange(layout.barHeight, 8, 2, 32)
    },
    timing: {
      displaySeconds: numberInRange(timing.displaySeconds, 8, 2, 60),
      enterMilliseconds: numberInRange(timing.enterMilliseconds, 620, 0, 5000),
      exitMilliseconds: numberInRange(timing.exitMilliseconds, 430, 0, 5000)
    },
    appearance: {
      backgroundColor: validColor(appearance.backgroundColor, "#00101f"),
      panelColor: validColor(appearance.panelColor, "#06233e"),
      accentColor: validColor(appearance.accentColor, "#ff1821"),
      textColor: validColor(appearance.textColor, "#f8fbff"),
      mutedColor: validColor(appearance.mutedColor, "#8094ad"),
      labelColor: validColor(appearance.labelColor, "#ff3b43"),
      barColor: validColor(appearance.barColor, "#ff1821"),
      backgroundOpacity: numberInRange(appearance.backgroundOpacity, 0.98, 0, 1),
      backgroundImage: String(appearance.backgroundImage || "")
    },
    typography: {
      fontFamily: String(source.typography?.fontFamily || "Segoe UI").slice(0, 80),
      viewerNameSize: numberInRange(source.typography?.viewerNameSize, 38, 16, 72),
      partNameSize: numberInRange(source.typography?.partNameSize, 34, 14, 64),
      labelSize: numberInRange(source.typography?.labelSize, 14, 8, 24)
    },
    content: {
      showCollection: content.showCollection !== false,
      showProgress: content.showProgress !== false,
      showCircuitOSBranding: content.showCircuitOSBranding !== false
    },
    animation: { style: ["slide", "fade", "none"].includes(source.animation?.style) ? source.animation.style : "slide" },
    stateColors: {
      rare: normalizeStateColor(stateColors.rare),
      complete: normalizeStateColor(stateColors.complete),
      duplicate: normalizeStateColor(stateColors.duplicate)
    },
    labels: {
      eyebrow: String(source.labels?.eyebrow || "CIRCUIT SCAN").slice(0, 60).toUpperCase(),
      componentAcquired: String(source.labels?.componentAcquired || "COMPONENT ACQUIRED").slice(0, 60).toUpperCase(),
      collectionProgress: String(source.labels?.collectionProgress || "COLLECTION PROGRESS").slice(0, 60).toUpperCase(),
      newItem: String(source.labels?.newItem || "NEW COMPONENT").slice(0, 60).toUpperCase(),
      collectionComplete: String(source.labels?.collectionComplete || "COLLECTION COMPLETE").slice(0, 60).toUpperCase(),
      duplicate: String(source.labels?.duplicate || "DUPLICATE").slice(0, 40).toUpperCase()
    }
  };
}

function cssUrl(value) {
  const safe = String(value || "").replace(/\\/g, "/").replace(/"/g, "%22");
  return safe ? `url("${safe}")` : "none";
}

function applyColorSet(root, accentColor, labelColor, barColor) {
  root.style.setProperty("--signal", accentColor);

  const labelRgb = hexToRgb(labelColor);
  root.style.setProperty("--label-color", labelColor);
  root.style.setProperty("--label-border", labelRgb ? `rgba(${labelRgb}, 0.62)` : "rgba(255, 59, 67, 0.62)");
  root.style.setProperty("--label-bg", labelRgb ? `rgba(${labelRgb}, 0.1)` : "rgba(255, 59, 67, 0.1)");
  root.style.setProperty("--label-glow", labelRgb ? `rgba(${labelRgb}, 0.35)` : "rgba(255, 59, 67, 0.35)");

  const barRgb = hexToRgb(barColor);
  root.style.setProperty("--bar-color", barColor);
  root.style.setProperty("--bar-glow", barRgb ? `rgba(${barRgb}, 0.7)` : "rgba(255, 24, 33, 0.7)");
  root.style.setProperty("--bar-track-border", barRgb ? `rgba(${barRgb}, 0.22)` : "rgba(255, 24, 33, 0.22)");
}

function applyOverlayConfig(config) {
  const root = document.documentElement;
  root.style.setProperty("--overlay-width", `${config.layout.width}px`);
  root.style.setProperty("--overlay-min-height", `${config.layout.minHeight}px`);
  root.style.setProperty("--offset-x", `${config.layout.offsetX}px`);
  root.style.setProperty("--offset-y", `${config.layout.offsetY}px`);
  root.style.setProperty("--bar-height", `${config.layout.barHeight}px`);
  root.style.setProperty("--navy-950", config.appearance.backgroundColor);
  root.style.setProperty("--navy-800", config.appearance.panelColor);
  root.style.setProperty("--ink", config.appearance.textColor);
  root.style.setProperty("--muted", config.appearance.mutedColor);
  root.style.setProperty("--panel-opacity", config.appearance.backgroundOpacity);
  root.style.setProperty("--enter-duration", `${config.timing.enterMilliseconds}ms`);
  root.style.setProperty("--exit-duration", `${config.timing.exitMilliseconds}ms`);
  root.style.setProperty("--overlay-font", `"${config.typography.fontFamily.replace(/["\\]/g, "")}"`);
  root.style.setProperty("--bg-image", cssUrl(config.appearance.backgroundImage));
  root.style.setProperty("--viewer-name-size", `${config.typography.viewerNameSize}px`);
  root.style.setProperty("--part-name-size", `${config.typography.partNameSize}px`);
  root.style.setProperty("--label-size", `${config.typography.labelSize}px`);

  applyColorSet(root, config.appearance.accentColor, config.appearance.labelColor, config.appearance.barColor);

  document.body.className = `position-${config.layout.position}`;
  document.body.dataset.animation = config.animation.style;

  const lb = config.labels;
  document.querySelector(".eyebrow").textContent = lb.eyebrow;
  document.querySelector(".label").textContent = lb.componentAcquired;
  document.querySelector(".progress-copy span").textContent = lb.collectionProgress;

  collectionName.hidden = !config.content.showCollection;
  document.querySelector(".progress-copy").hidden = !config.content.showProgress;
  document.querySelector(".progress-track").hidden = !config.content.showProgress;
  document.querySelector(".circuit-mark").hidden = !config.content.showCircuitOSBranding;
}

function applyStateColors(stateName, config) {
  if (stateName === "normal") return;
  const sc = config.stateColors?.[stateName];
  if (!sc) return;
  const root = document.documentElement;
  const accentColor = validColor(sc.accentColor, "") || config.appearance.accentColor;
  const labelColor = validColor(sc.labelColor, "") || config.appearance.labelColor;
  const barColor = validColor(sc.barColor, "") || config.appearance.barColor;
  applyColorSet(root, accentColor, labelColor, barColor);
}

async function loadOverlayConfig() {
  try {
    // overlay-config.json is co-located in the same folder (published by CircuitOS on
    // startup and on every save). No cross-directory fetch needed in any mode.
    const response = await fetch(`overlay-config.json?t=${Date.now()}`, { cache: "no-store" });
    overlayConfig = normalizeOverlayConfig(response.ok ? await response.json() : defaultOverlayConfig);
  } catch {
    overlayConfig = normalizeOverlayConfig(defaultOverlayConfig);
  }
  applyOverlayConfig(overlayConfig);
}

function setText(element, value, fallback = "") {
  element.textContent = typeof value === "string" && value.trim() ? value : fallback;
}

function addTag(text) {
  if (!text) return;
  const tag = document.createElement("span");
  tag.className = "tag";
  tag.textContent = text;
  tags.appendChild(tag);
}

function hideTracker() {
  if (!tracker.classList.contains("visible")) return;
  tracker.classList.remove("visible");
  tracker.classList.add("hiding");
  tracker.setAttribute("aria-hidden", "true");
}

function scheduleHide(visibleUntilUtc) {
  window.clearTimeout(hideTimer);
  if (previewMode) return;

  const stateRemaining = new Date(visibleUntilUtc).getTime() - Date.now();
  const configuredRemaining = (overlayConfig?.timing.displaySeconds || 8) * 1000;
  const remaining = Math.min(stateRemaining, configuredRemaining);
  if (!Number.isFinite(remaining) || remaining <= 0) {
    hideTracker();
    return;
  }

  hideTimer = window.setTimeout(hideTracker, remaining);
}

function renderState(state) {
  if (overlayConfig?.enabled === false) { hideTracker(); return; }
  const updatedAt = String(state.updatedAtUtc || "");
  if (!updatedAt || (updatedAt === lastUpdate && !previewMode)) return;
  lastUpdate = updatedAt;

  const visibleUntil = new Date(state.visibleUntilUtc).getTime();
  if (!previewMode && (!Number.isFinite(visibleUntil) || visibleUntil <= Date.now())) {
    hideTracker();
    return;
  }

  const owned = Math.max(0, Number(state.ownedCount) || 0);
  const total = Math.max(0, Number(state.totalCount) || 0);
  const quantity = Math.max(1, Number(state.quantity) || 1);
  const percent = total > 0 ? Math.min(100, Math.round((owned / total) * 100)) : 0;
  const isRare = Boolean(String(state.rareLabel || "").trim());
  const isComplete = Boolean(state.newlyCompleted);
  const isDuplicate = Boolean(state.isDuplicate);
  const variantLabels = Array.isArray(state.variantLabels) ? state.variantLabels.filter(v => typeof v === "string" && v.trim()) : [];
  const tierLabel = String(state.tierLabel || "").trim();

  setText(viewerName, String(state.viewerName || ""), "Viewer");
  setText(collectionName, String(state.collectionName || ""), "Collection");
  setText(partName, String(state.partName || ""), "Component");
  progressText.textContent = `${owned} / ${total}`;

  tracker.className = "tracker";
  if (isRare) tracker.classList.add("rare");
  if (isComplete) tracker.classList.add("complete");
  if (isDuplicate) tracker.classList.add("duplicate");

  const lb = overlayConfig?.labels || defaultOverlayConfig.labels;
  if (isComplete) statusBadge.textContent = lb.collectionComplete;
  else if (isRare) statusBadge.textContent = String(state.rareLabel).toUpperCase();
  else if (isDuplicate) statusBadge.textContent = `${lb.duplicate} x${quantity}`;
  else statusBadge.textContent = lb.newItem;

  tags.replaceChildren();
  for (const v of variantLabels) addTag(v);
  if (tierLabel && !isRare) addTag(tierLabel);
  if (state.featuredBoost) addTag(String(state.featuredBoost).toUpperCase());
  if (isDuplicate && (isRare || isComplete)) addTag(`DUPLICATE x${quantity}`);

  const activeStateName = isRare ? "rare" : isComplete ? "complete" : isDuplicate ? "duplicate" : "normal";
  applyStateColors(activeStateName, overlayConfig || defaultOverlayConfig);

  progressBar.style.width = "0%";
  tracker.classList.remove("hiding");
  void tracker.offsetWidth;
  tracker.classList.add("visible");
  tracker.setAttribute("aria-hidden", "false");
  window.requestAnimationFrame(() => {
    progressBar.style.width = `${percent}%`;
  });

  scheduleHide(state.visibleUntilUtc);
}

async function refreshState() {
  try {
    const response = await fetch(`overlay-state.json?t=${Date.now()}`, { cache: "no-store" });
    if (!response.ok) {
      if (previewMode) renderState(makeDummyState(activePreviewState));
      return;
    }
    const state = await response.json();
    if ((!state.updatedAtUtc || !state.viewerName) && previewMode) {
      renderState(makeDummyState(activePreviewState));
      return;
    }
    renderState(state);
  } catch {
    if (previewMode) renderState(makeDummyState(activePreviewState));
  }
}

async function initializeOverlay() {
  await loadOverlayConfig();
  await refreshState();
  if (!previewMode) window.setInterval(refreshState, 500);
}

window.addEventListener("message", event => {
  if (!event.data || !previewMode) return;
  if (event.data.type === "overlayPreviewConfig") {
    overlayConfig = normalizeOverlayConfig(event.data.config);
    applyOverlayConfig(overlayConfig);
    if (overlayConfig.enabled === false) {
      hideTracker();
    } else {
      renderState(makeDummyState(activePreviewState));
    }
  } else if (event.data.type === "overlayPreviewState") {
    activePreviewState = event.data.state || "normal";
    renderState(makeDummyState(activePreviewState));
  }
});

initializeOverlay();
