# CircuitOS — HANDOFF Archive (historical session log)

Archived from HANDOFF.md on 2026-07-01 to keep the active handoff focused on the current 0.7
milestone. This is the historical session log — 0.4 through 0.6.0.8, the early 0.7 groundwork,
and a few out-of-order early-0.7 Codex closeout entries. **Nothing here is current state**;
see HANDOFF.md for that.

---

### 2026-06-23 — Claude (claude-opus-4-8) — Session 0.6.0.8 (live test pass + variant-message fix)

**Goal:** User ran the live integration test on stream. Tiers + variants pull correctly
("CAFFINATED Capacitor" landed and was tracked). The test surfaced one chat-message bug.

**Bug found on stream:** The optional variant-pull message doubled the variant label —
*"shortcircuit_tv found a CAFFINATED CAFFINATED Capacitor"* — when the template used both
`{variantLabels}` and `{item}`.

**Root cause:** In `StreamerbotReedeem.txt`, the variant-pull message passed `displayPartName`
(variant-prefixed) for `{item}`, so `{variantLabels} {item}` rendered the label twice. This
was the only message exposing both placeholders.

**Fix:** Variant-pull `{item}` now uses the base `partName`; `{variantLabels} {item}` composes
to "CAFFINATED Capacitor". All other messages keep `displayPartName` for `{item}` (correct —
they don't expose `{variantLabels}`).

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/*` (csproj, Program.cs, Core.cs, Modules.cs) | Version → 0.6.0.8 |
| `streamerbot-actions/StreamerbotReedeem.txt` | Variant-pull `{item}` = base `partName` (was `displayPartName`) + clarifying comment |
| `tools/admin/app.js` | variantPull message description explains `{item}` is the base name; added `variantLabels` sample value so the live preview composes |
| `README.md` | Version → 0.6.0.8 |
| `docs/patch-notes/v0.6.0.8.md` | Created |

**Built and packaged:** `dist/CircuitOS-Update-0.6.0.8.zip`.

**⚠️ Requires regenerating the Streamer.bot Redemption action** (the fix is in the action).

**Status:** 0.6 is now validated end-to-end on stream. Remaining before 0.7: optional polish
(two overlay UX nits, dist cleanup of orphaned 0.5.0.9/0.5.1), then begin the cloud milestone.

---

### 2026-06-23 — Claude (claude-opus-4-8) — Session 0.6.0.7 (consolidation: docs audit + tier polish)

**Goal:** Full documentation audit (READMEs, HANDOFF, docs, memory, stale info) and a
0.6 code audit before live integration testing. User chose to stabilize/polish 0.6 before
starting the 0.7 cloud milestone.

**Documentation audit — fixed stale info across:**

| File | Change |
|------|--------|
| `README.md` | Feature list gained variants/tiers/bulk-assign/CSV-tier; marked 0.4 complete (was "in progress") and 0.6 complete; version-locations paragraph 3→5 files |
| `HANDOFF.md` | Project Identity header 0.5.0.2→0.6.0.x; Version String Locations table 4→5; app.js line count 2,650→3,800; 0.4 overlay "remaining work" reframed as resolved (0.5.0.6–0.5.0.8); live data path corrected |
| `AGENTS.md` | Full rewrite — was double-escaped markdown listing all-shipped features as "planned" |
| `docs/configuration-editor.md` | Pull Lab→Rate Lab, Branding→Game Profile, 12→13 messages, added variants/tiers/bulk-assign to editable list, tier-aware Rate Lab |
| `docs/collection-importer.md` | Save Live Config→Save Catalog, Import Items→Import Components, added CSV tier column section |
| `docs/obs-lower-quarter.md` | Rewrote for auto-publish flow (was manual file-copy, outdated since 0.5.0.7); added variant/tier tracker tags |
| `docs/versioning.md` | Aligned to milestone-based four-part scheme; release checklist now lists 5 version locations + patch-note/HANDOFF step |
| `docs/maintainer-quick-fixes.md` | Fixed "4 version locations" list (one was wrong, two missing → canonical 5); version rules aligned to milestone scheme |
| memory `project_circuitos.md` + `MEMORY.md` | Version 0.5.0.1→0.6.0.6, milestone, corrected live data path |

**Stale data path finding:** The live data path was wrong in 5 places — docs said the
pre-0.5 `C:\Users\nicho\OneDrive\Documents\CircuitComponents`, but the app's save dialog
(seen in the user's screen recording) shows it is now
`C:\Users\nicho\Documents\CircuitOS\Data\profiles\circuit-components`. Corrected everywhere.

**Orphaned dist artifacts (flagged, NOT deleted):** `dist/CircuitOS-Update-0.5.0.9` and
`dist/CircuitOS-Update-0.5.1` (the latter built at noon, a mis-numbered/aborted build) have
no patch note or HANDOFF entry. Left in place pending user confirmation to clean up.

**0.6 code audit (before live test):**
- Reviewed catalog editor (variants/tiers/bulk-assign/CSV import), `simulationModel`,
  `renderRatelabTiers`, the Streamer.bot rolling logic, and `overlay.js`.
- Streamer.bot tier-weighted roll + variant rolling and overlay tag rendering are correct
  and well-guarded — no changes needed. Overlay shows base item name + variant labels as
  separate tags (chat uses the variant-prefixed name); no redundancy.
- **Fixed:** renaming a tier ID orphaned its assigned items (`part.tier` kept the old id →
  save failed validation). Now migrates `part.tier` references on rename, mirroring the
  collection-key rename. (`app.js` ~2635)
- **Fixed (cosmetic):** `renderRatelabTiers` produced an invalid bar width when a tiered
  collection's effective rate is 0; now renders an empty bar.

**Changes made (code):**

| File | Change |
|------|--------|
| `tools/runtime/*` (csproj, Program.cs, Core.cs, Modules.cs) | Version → 0.6.0.7 |
| `tools/admin/app.js` | Tier-ID rename migrates `part.tier`; zero-rate tier bar width guard |
| `README.md` | Version → 0.6.0.7 |
| `docs/patch-notes/v0.6.0.7.md` | Created |

**Built and packaged:** `dist/CircuitOS-Update-0.6.0.7.zip`.

**Next steps:**
- USER: install 0.6.0.7, then run the live integration test (regenerate + repaste the
  Streamer.bot Redemption action — required since 0.6.0.3 — then test pulls with tiers +
  variants; confirm overlay tags, tier-weighted odds, and the variant-pull message).
- Batch any live-test findings into 0.6.0.8.
- Optional 0.6.x polish: the two minor overlay UX nits in "Known Remaining Work"; dist cleanup.
- After 0.6 is confirmed solid on stream: begin 0.7 (Cloud Platform + Twitch).

---

### 2026-06-23 — Claude (claude-opus-4-8) — Session 0.6.0.6 (hotfix)

**Goal:** Fix editor crash reported via screen recording — "Cannot access 'hasTiers' before initialization".

**Root cause:** In `buildCollectionCard` (`tools/admin/app.js`), the 0.6.0.5 bulk-assign
toolbar block at line ~2545 read `hasTiers` inside `if (hasTiers)`, but `const hasTiers`
was not declared until line ~2579 — a temporal dead zone violation. Every collection-card
body render threw (expand card, add event, etc.), breaking the editor.

**Fix:** Moved `const hasTiers = Array.isArray(value.tiers) && value.tiers.length > 0;`
to just above the bulk-assign block; removed the now-duplicate declaration further down.

**Diagnosis note:** Issue was reported as a 24s OBS `.mkv`. Installed ffmpeg via winget,
extracted frames at 2s intervals, read them as images — final frame showed the error banner.
ffmpeg frame extraction is now a usable tool for future screen-recording bug reports.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.6.0.6 |
| `tools/runtime/Program.cs` | Version → "0.6.0.6" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.6.0.6" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.6.0.6" |
| `tools/admin/app.js` | Moved `const hasTiers` above its first use in `buildCollectionCard`; removed duplicate declaration |
| `README.md` | Version → 0.6.0.6 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.6.0.6.md` | Created |

**Built and packaged:** `dist/CircuitOS-Update-0.6.0.6.zip`.

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.6.0.5

**Goal:** Bulk tier assignment UI + CSV importer tier column support.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.6.0.5 |
| `tools/runtime/Program.cs` | Version → "0.6.0.5" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.6.0.5" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.6.0.5" |
| `tools/admin/app.js` | Bulk assign toolbar above items list (Assign all / Assign unassigned); `←Unassigned` button on each tier row; `parseImportItems` extracts `rawTier` from "tier" header column; `buildCollectionImportPreview` passes through tier with light validation against target collection's tier IDs; `applyCollectionImportParts` writes `{ id, name, tier }` when tier present; `renderImportPreviewUI` adds dynamic "Tier" column to preview table |
| `tools/admin/styles.css` | `.import-table.has-tier` 5-col grid; `.import-tier-cell`; `.tier-row` 5-col grid for ← Unassigned button; `.bulk-assign-row`, `.bulk-assign-label`, `.bulk-assign-select`; mobile responsive variants |
| `README.md` | Version → 0.6.0.5 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.6.0.5.md` | Created |

**0.6 milestone is now fully feature complete:**
- Catalog editor: variants + tiers + item tier dropdown + bulk assign toolbar + CSV tier column (0.6.0.1–0.6.0.5)
- Rolling logic: tier-weighted pull + variant rolling in Streamer.bot action (0.6.0.3)
- Overlay: variantLabels and tierLabel tags rendered (0.6.0.3)
- variantPull optional message template (0.6.0.3)
- Rate Lab: tier breakdown panel + tier-aware simulation (0.6.0.4)
- Bulk tier assignment: toolbar (Assign all / Assign unassigned) + per-tier ← Unassigned button (0.6.0.5)
- CSV import: tier column support with preview table tier column (0.6.0.5)

**Next steps:**
- Integration test: install 0.6.0.5 update, configure a collection with tiers + variants, regenerate Streamer.bot action, do live test pull
- Check that tier-weighted pulls land at expected frequency over ~50 test pulls
- If all good, declare 0.6 complete and plan 0.7

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.6.0.4

**Goal:** Rate Lab Rarity Tiers breakdown panel + tier-aware pull simulation.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.6.0.4 |
| `tools/runtime/Program.cs` | Version → "0.6.0.4" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.6.0.4" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.6.0.4" |
| `tools/admin/index.html` | Replaced static "COMING IN 0.6" placeholder in ratelab-tiers-panel with `<div id="ratelabTiersContent">` for dynamic rendering; updated help-tip text |
| `tools/admin/app.js` | `simulationModel()` is now tier-aware: items are weighted by `(tierWeight/totalTierWeight) * collectionProb / itemsInTier` when tiers exist; untiered items fall back to equal odds; `renderRateLab()` calls new `renderRatelabTiers()`; `renderRatelabTiers()` builds per-collection tier breakdown (tier label, item count, % of all pulls, proportional bar, per-item 1-in-N odds) |
| `tools/admin/styles.css` | Added `.tiers-empty-state`, `.tiers-section`, `.tiers-section-label`, `.tiers-collection-pct`, `.tier-stat-row`, `.tier-stat-label`, `.tier-stat-count`, `.tier-stat-pct`, `.tier-stat-bar`, `.tier-stat-fill`, `.tier-stat-per-item` |
| `README.md` | Version → 0.6.0.4 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.6.0.4.md` | Created |

**0.6 milestone is now feature complete:**
- Catalog editor: variants section + tiers section + item tier dropdown (0.6.0.1–0.6.0.2)
- Rolling logic: tier-weighted pull + variant rolling in Streamer.bot action (0.6.0.3)
- Overlay: variantLabels and tierLabel tags rendered (0.6.0.3)
- variantPull optional message template (0.6.0.3)
- Rate Lab: tier breakdown panel + tier-aware simulation (0.6.0.4)

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.6.0.3

**Goal:** Implement rolling logic for tiers and variants; add variantPull message; overlay tag rendering.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.6.0.3 |
| `tools/runtime/Program.cs` | Version → "0.6.0.3" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.6.0.3"; added `variantPull` to `MessagePlaceholders` with `[variantLabels, viewer, item, collection]`; added `OptionalMessages` set (empty string allowed for optional fields); added `variantPull` default `""` to `DefaultProfile`; wired `VariantPullTemplate` into Streamer.bot redeem generator |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.6.0.3" |
| `streamerbot-actions/StreamerbotReedeem.txt` | Tier-weighted item selection (groups eligible parts by tier, rolls weighted tier, picks item from tier); variant rolling (independent rolls, cap 2, no duplicate labels); `displayPartName` = variantPrefix + partName used in all messages; `VariantPullTemplate` constant + fire when variants land; `SaveOverlayStateSafely` extended with `variantLabels` + `tierLabel` → written to overlay-state.json |
| `tools/admin/app.js` | Added `variantPull` to `messageDefinitions` (marked optional); added to `defaultSystemProfile.messages` as `""`; added `variantLabels` to `placeholderDescriptions` |
| `overlays/lower-quarter/overlay.js` | `renderState` extracts `variantLabels` array and `tierLabel`; tags row renders variants first, then tier label (if not already a rare pull), then featured boost |
| `README.md` | Version → 0.6.0.3 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.6.0.3.md` | Created |

**Key behavior:**
- Tier-weighted pull: roll tier → pick item from tier. Dup-protection excludes entire tiers whose items are all owned.
- Variant roll: each variant in the collection's `variants` array gets an independent `Random.NextDouble() < chance` check, cap at 2, no duplicate labels.
- `{item}` in ALL message templates (redeemSuccess, rarePull, triplePull) now includes the variant prefix automatically.
- `variantPull` template is optional (empty = no extra message). Fires after the standard messages.
- `overlay-state.json` gains `variantLabels: string[]` and `tierLabel: string`.
- Overlay tags row: variant labels → tier label (skipped if rare label also shown) → featured boost → duplicate overflow.

**IMPORTANT:** Users must regenerate and repaste the Redemption action from Streamer.bot Setup after updating.

**Next steps (0.6.0.4):**
- Rate Lab: Rarity Tiers breakdown panel (per-tier effective %, per-item effective odds within tier)
- Pull simulator: tier-aware and variant-aware simulation

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.6.0.2

**Goal:** Add Rarity Tiers catalog layer — tier definitions per collection + item tier assignment.

**Design decisions (from user):**
- If a collection has tiers, every item MUST be assigned — validation error if not.
- Tier config lives in the collection editor; Rate Lab shows a read-only breakdown (0.6.0.4).
- Removing all tiers from a collection clears tier assignments from all items.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.6.0.2 |
| `tools/runtime/Program.cs` | Version → "0.6.0.2" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.6.0.2"; validates `tiers` array (id slug, label, weight > 0, unique); validates all items assigned to valid tier when tiers exist |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.6.0.2" |
| `tools/admin/app.js` | Collection normalization includes `tiers`; `serializeModel` strips empty tiers; `buildCollectionCard` adds Rarity Tiers editor (id/label/weight + remove); item rows get Tier dropdown when tiers exist; removing all tiers clears item `tier` fields; patch-note diff tracks tier changes |
| `tools/admin/styles.css` | Added `.tier-row`, `.part-row-tiered` styles |
| `README.md` | Version → 0.6.0.2 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.6.0.2.md` | Created |

**Tier catalog schema (backward compatible — `tiers` is optional):**
```json
"pokemon": {
  "tiers": [
    { "id": "common", "label": "COMMON", "weight": 70 },
    { "id": "rare", "label": "RARE", "weight": 25 },
    { "id": "ultra", "label": "ULTRA RARE", "weight": 5 }
  ],
  "parts": [
    { "id": "bulbasaur", "name": "Bulbasaur", "tier": "common" },
    { "id": "charizard", "name": "Charizard", "tier": "rare" },
    { "id": "mewtwo", "name": "Mewtwo", "tier": "ultra" }
  ]
}
```

**Next steps (0.6.0.3):**
- `StreamerbotReedeem.txt`: tier-weighted item selection after collection roll; variant rolls after item selection; write `variantLabels` + `tierLabel` to overlay state; add optional `variantPull` message template
- `overlay-state.json`: add `variantLabels: string[]` and `tierLabel: string` fields
- `overlay.js`: render variant labels as tags; tier label as an additional badge

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.6.0.1

**Goal:** Begin 0.6 Item Variants — catalog data model, backend validation, and admin editor UI.

**Design decisions (from user):**
- Variant = same base item with up to two tags (e.g., SHINY, LARGE). Inventory stays keyed on base item ID.
- Duplicate check = base item ownership only (any variant counts as owning the item).
- Variants defined per collection (not per item).
- Up to 2 variant tags can fire on a single pull (independent rolls, sequential, no same tag twice).
- Variant `{item}` placeholder in chat will auto-prefix variant labels (e.g., "SHINY Bulbasaur").

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.6.0.1 |
| `tools/runtime/Program.cs` | Version → "0.6.0.1" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.6.0.1"; added variant validation in `ValidateConfiguration` (id format, label required, chance 0–1 exclusive) |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.6.0.1" |
| `tools/admin/app.js` | Collection normalization includes `variants` array; `serializeModel` strips empty variants; `buildCollectionCard` adds variant editor section (id, label, chance % fields + remove button); patch-note diff tracks variant add/remove/change |
| `tools/admin/styles.css` | Added `.variant-list`, `.variant-row`, `.variant-help` styles |
| `README.md` | Version → 0.6.0.1 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.6.0.1.md` | Created |

**Catalog schema addition (backward compatible — `variants` is optional):**
```json
"basic": {
  "displayName": "Basic Components",
  "variants": [
    { "id": "shiny", "label": "SHINY", "chance": 0.05 },
    { "id": "large", "label": "LARGE", "chance": 0.03 }
  ],
  "parts": [...]
}
```

**Next steps (0.6.0.2):**
- `StreamerbotReedeem.txt`: roll variants after item selection; build `displayPartName`; write `variantLabels` array to overlay state
- `overlay-state.json`: add `variantLabels: string[]` field
- `overlay.js`: render variant labels as tags in the overlay
- `CircuitService.Core.cs` / `app.js`: add `variantPull` optional message template

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.8

**Goal:** Fix overlay background image gone after 0.5.0.7 Local file mode change.

**Root cause:** Background image was stored as `/overlay-bg` (HTTP endpoint URL) in overlay-config.json.
In file:// mode, `url("/overlay-bg")` resolves to `file:///overlay-bg` — nothing. The image
file itself (`bg.*`) is co-located with the HTML, so a relative filename `"bg.png"` works in both modes.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.5.0.8 |
| `tools/runtime/Program.cs` | `SendOverlayFileAsync` now serves `bg.png/jpg/gif/webp` from DataPath/overlay/ under `/overlay/bg.*`; version → "0.5.0.8" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.8" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.8" |
| `overlays/lower-quarter/overlay.js` | Added `normalizeBackgroundImage()`: remaps `/overlay-bg*` → `"bg.png"` in file:// mode for backward compat; hooked into `normalizeOverlayConfig` |
| `tools/admin/app.js` | Upload now stores `result.filename` (`"bg.png"` etc.) instead of `/overlay-bg?t=...`; `updateStatus()` simplified |
| `README.md` | Version → 0.5.0.8 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.5.0.8.md` | Created |

**Next steps:**
- Build and package dist/CircuitOS-Update-0.5.0.8.zip
- Move to **0.6 — Item Variants**

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.7

**Goal:** Fix OBS overlay not updating when Streamer.bot triggers a redeem.

**Root cause:** The install package puts overlay HTML in `Overlay\` but Streamer.bot writes
`overlay-state.json` to `DataPath\profiles\<id>\overlay\`. These are different directories,
so `fetch("overlay-state.json")` from a local file:// URL resolves to the wrong path.

**Fix:** On startup and after every profile switch, `Program.cs` copies `index.html`,
`overlay.js`, and `styles.css` from the `Overlay\` folder into `DataPath\overlay\` — the
same directory where Streamer.bot writes state. OBS browser sources using Local file mode
now point to `DataPath\overlay\index.html`, which is co-located with the state file and one
level above `overlay-config.json` (correct relative path for both fetches).

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.5.0.7 |
| `tools/runtime/Program.cs` | Added `PublishOverlayStatics()` — copies Overlay statics to DataPath/overlay/ on startup and after profile switch; health response now includes `overlayFilePath`; version → "0.5.0.7" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.7" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.7" |
| `tools/admin/index.html` | Added "OBS SETUP" panel at top of overlay editor showing the local file path with a Copy button |
| `tools/admin/app.js` | Added `overlayFilePath` global; populated from health response; `renderOverlayEditor()` sets obsFilePath element; copy button handler |
| `tools/admin/styles.css` | Added `.obs-source-panel`, `.obs-path-row`, `.obs-path-code` styles |
| `tools/package/package-files/OBS SETUP.txt` | Updated to reference the Overlay Editor panel for the file path |
| `README.md` | Version → 0.5.0.7 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.5.0.7.md` | Created |

**Profile data layout now includes published overlay statics:**
```
DataPath/profiles/<id>/overlay/
├── index.html        ← published from Overlay\ on startup/switch
├── overlay.js        ← published from Overlay\ on startup/switch
├── styles.css        ← published from Overlay\ on startup/switch
├── overlay-state.json  ← written by Streamer.bot on redeem
└── bg.*              ← uploaded background image (if any)
```

**Next steps:**
- Build and package dist/CircuitOS-Update-0.5.0.7.zip
- Move to **0.6 — Item Variants**

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.6

**Goal:** Overlay customization (label color, font sizes, bar controls) and live preview fix.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.5.0.6 |
| `tools/runtime/Program.cs` | Version → "0.5.0.6" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.6" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.6" |
| `overlays/lower-quarter/overlay.js` | Added hexToRgb(), makeDummyState(), activePreviewState; expanded defaultOverlayConfig and normalizeOverlayConfig with labelColor, barColor, barHeight, viewerNameSize, partNameSize, labelSize; applyOverlayConfig sets all new CSS vars + derived RGBA values; refreshState falls back to dummy state in preview mode; window.addEventListener("message") handles overlayPreviewConfig and overlayPreviewState postMessages |
| `overlays/lower-quarter/styles.css` | Added --label-color, --label-border, --label-bg, --label-glow, --bar-color, --bar-glow, --bar-track-border, --bar-height, --viewer-name-size, --part-name-size, --label-size CSS vars; .eyebrow/.label use var(--label-color/--label-size/--label-glow); .viewer-name/.part-name use size vars instead of clamp(); .status-badge/.tag use label vars; .progress-track uses --bar-height/--bar-track-border; .progress-bar uses --bar-color/--bar-glow |
| `tools/admin/index.html` | Overlay preview panel: replaced static note with Normal/Rare/Complete/Duplicate state picker buttons |
| `tools/admin/app.js` | Added updateOverlayPreview(); overlayField and overlayCheckbox both call updateOverlayPreview() on change; buildBgImageField clearBtn calls updateOverlayPreview(); renderOverlayEditor adds Label color, Bar fill, Bar height, Viewer name size, Item name size, Label size fields; event listeners for [data-preview-state] buttons send overlayPreviewState postMessage |
| `tools/admin/styles.css` | Added .overlay-preview-states and .overlay-preview-states .button.active styles; removed .overlay-preview-note |
| `README.md` | Version → 0.5.0.6 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.5.0.6.md` | Created |

**Next steps:**
- Build and package dist/CircuitOS-Update-0.5.0.6.zip
- Move to **0.6 — Item Variants**

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.5

**Goal:** Viewer inventory cleanup and import error UX improvements.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.5.0.5 |
| `tools/runtime/Program.cs` | Version → "0.5.0.5"; added POST /api/inventory/reset-viewer and /api/inventory/remove-item routes |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.5" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.5" |
| `tools/runtime/CircuitService.AnalyticsRoles.cs` | Added ResetViewer() and RemoveInventoryItem() — both read inventory, mutate, WriteAtomic with backup |
| `tools/admin/index.html` | Both import modal footers: added Skip Errors button (hidden by default) |
| `tools/admin/app.js` | renderViewerInspector: removed Twitch ID and scrap balance from list; renderViewerDetail: removed Twitch ID subtitle, replaced READ ONLY chip with Reset Inventory button; parts rendering: added × remove button per owned item; renderCollectionImportPreview and renderEventImportPreview unified via renderImportPreviewUI() helper — error summary compact list, READY-only preview table, Skip Errors button wiring; added applyCollectionImportSkipErrors, applyEventImportSkipErrors, applyCollectionImportParts, applyEventImportParts; added async resetViewer() and removeInventoryItem() with confirm + reload |
| `tools/admin/styles.css` | .viewer-button simplified (display:block, no sub-elements); .viewer-part updated to flex with span:flex-1; added .viewer-part-remove (reveal on hover, danger on hover); added .import-error-list |
| `README.md` | Version → 0.5.0.5 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.5.0.5.md` | Created |

**Next steps:**
- Build and package dist/CircuitOS-Update-0.5.0.5.zip
- Move to **0.6 — Item Variants** (tiers + variant second-roll)

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.4

**Goal:** Rate Lab — replaces Simulator view with a combined weight editor + distribution checker. Design discussions on rarity tiers (optional, profile-level, user-named) and variants (separate system, second roll after item selection, 0.6 feature).

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version 0.5.0.3 → 0.5.0.4 |
| `tools/runtime/Program.cs` | Health endpoint version → "0.5.0.4" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.4" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.4" |
| `tools/admin/index.html` | Nav "Simulator" → "Rate Lab" (data-view="ratelab"); Overview panel: "WEIGHT MODEL" → "PULL ODDS", dynamic h2 id="rateChartTitle", "Tune in Rate Lab" button, "BASE RATES"/"BOOST ACTIVE" chip; Replaced simulatorView section with ratelabView — weight editor panel, distribution check panel, rarity tiers placeholder panel |
| `tools/admin/app.js` | viewTitles.ratelab = "Rate Lab"; renderAll no longer calls renderSimulator; renderViewOnDemand handles ratelab; renderOverview updates rateStateChip + rateChartTitle dynamically; replaced renderSimulator/runSimulation with renderRateLab, buildWeightRow, refreshWeightPercentages, renderRatelabSimulation, runRatelabSim; event listener updated to runRatelabSimButton |
| `tools/admin/styles.css` | Replaced simulator-toolbar/part-odds styles with ratelab-toolbar, ratelab-layout, weight-editor, weight-row, weight-input, weight-pct, weight-mini-bar, weight-mini-fill, weight-section-label, help-tip (CSS tooltip via data-tip), tiers-placeholder, rate-panel-actions, metric-chip.active |
| `README.md` | Version → 0.5.0.4 |
| `HANDOFF.md` | Current State version bump; this session log entry |
| `docs/patch-notes/v0.5.0.4.md` | Created Discord-ready patch notes |

**Design decisions recorded:**
- Rarity tiers are optional, profile-level (not per-collection), user-named — Circuit Components can ignore entirely
- Tiers ≠ Variants: tiers control intra-collection pull probability; variants are a second roll after item selection
- Tiers are a 0.6 feature; Rate Lab UI has a placeholder panel with "COMING IN 0.6" chip
- `?` help-tip pattern established — CSS tooltip via `data-tip` attribute, no CDN dependency

**Next steps:**
- Build and package `dist/CircuitOS-Update-0.5.0.4.zip`
- Move to **0.6 — Item Variants** (tiers + variant second-roll system)

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.3

**Goal:** Sidebar overhaul — inline profile switcher, nav restructure (Community group, Inventory rename, Patch Notes moved), brand/footer cleanup, chevron indicators.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version 0.5.0.2 → 0.5.0.3 |
| `tools/runtime/Program.cs` | Health endpoint version → "0.5.0.3" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.3" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.3" |
| `tools/admin/index.html` | Brand: removed "CIRCUITOS PLATFORM" kicker; Active profile block → profile switcher button + dropdown (with scrollable list + Manage link); Removed "Profiles" nav item; "Viewers" group → "Community"; "Inspector" → "Inventory"; Patch Notes moved from Tools to Community; Footer: removed "CIRCUITOS LOCAL ENGINE" label |
| `tools/admin/styles.css` | Profile switcher styles (wrap, dropdown, list, items, manage button); nav-group chevron indicator (CSS border trick replaces +/−); brand-title margin-top removed; eyebrow/panel-kicker selector cleaned up |
| `tools/admin/app.js` | viewTitles.viewers → "Viewer Inventory"; added renderProfileSwitcher(), openProfileSwitcher(), closeProfileSwitcher(), toggleProfileSwitcher(); loadProfiles() now calls renderProfileSwitcher(); event handlers for trigger click, outside-click close, Escape close, Manage link |
| `README.md` | Version → 0.5.0.3 |
| `HANDOFF.md` | Version bump; this session log entry |
| `docs/patch-notes/v0.5.0.3.md` | Created Discord-ready patch notes |

**Next steps:**
- Build and package `dist/CircuitOS-Update-0.5.0.3.zip`
- Move to **0.6 — Item Variants**

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.2

**Goal:** Remaining UI audit items from the 0.5.0.1 first-run review — nav clarity, label polish, wizard preset naming.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version 0.5.0.1 → 0.5.0.2 |
| `tools/runtime/Program.cs` | Health endpoint version → "0.5.0.2" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.2" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.2" |
| `tools/admin/index.html` | "Collections" group → "Catalog"; "Settings" group → "Configure" (moved above Catalog); "Branding" nav item → "Game Profile"; panel h2 "Branding & Terminology" → "Game Profile"; "Dup protection (turns)" → "Dupe protection (pulls)" with clearer tooltip; "Export Active" → "Export Active Profile"; wizard preset "Circuit Components" → "Circuit Components Starter" (×2: button and header description) |
| `tools/admin/app.js` | viewTitles branding → "Game Profile" |
| `README.md` | Version → 0.5.0.2 |
| `HANDOFF.md` | Current State version bump; this session log entry |
| `docs/patch-notes/v0.5.0.2.md` | Created Discord-ready patch notes |

**Next steps:**
- Package and distribute `dist/CircuitOS-Update-0.5.0.2.zip`
- Move to **0.5.0.3** — Overlay UX improvements (user has ideas to discuss)

---

### 2026-06-23 — Claude (claude-sonnet-4-6) — Session 0.5.0.1

**Goal:** 0.5 milestone wrap-up: debug dual-ACTIVE profile bug, UI audit and label cleanup, version bump to 0.5.0.1.

**Root cause of dual-ACTIVE profiles (resolved by user):** When copying data files between profile folders, the user accidentally copied a `profile-meta.json` from one profile into another, causing both profiles to claim the same id. The rendering fix (using `profilesData.activeProfileId` as truth rather than `profile.isActive`) was already in place and correct.

**UI audit findings (first-time user walk-through):**
- "Save Live Config" was unclear — renamed to "Save Catalog"
- Topbar Import/Export were ambiguous vs module import/export on Profiles page — renamed to "Import Catalog" / "Export Catalog"
- "Integrations" nav group had only one item (Streamer.bot) — group removed, Streamer.bot promoted to direct nav item
- "Pull Lab" (nav) didn't match "Redeem Simulator" (view heading) — nav now says "Simulator"
- "Brand kicker" was internal jargon — renamed to "Eyebrow label" with a descriptive tooltip

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Version 0.5.0 → 0.5.0.1 |
| `tools/runtime/Program.cs` | Health endpoint version → "0.5.0.1" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.5.0.1" |
| `tools/runtime/CircuitService.Modules.cs` | circuitosVersion → "0.5.0.1" |
| `tools/admin/index.html` | Save Catalog / Import Catalog / Export Catalog labels; Integrations group removed; Simulator nav label; Eyebrow label field (×2: wizard + branding view) |
| `tools/admin/app.js` | markDirty/markClean → "Save Catalog"; viewTitles simulator → "Simulator" |
| `README.md` | Version → 0.5.0.1; added 0.5 features to feature list; marked 0.5 roadmap section complete |
| `HANDOFF.md` | Added Current State block; this session log entry |
| `docs/patch-notes/v0.5.0.1.md` | Created Discord-ready patch notes |

**Next steps:**
- Distribute `dist/CircuitOS-Update-0.5.0.1.zip`
- Move to **0.6 — Item Variants**

---

### 2026-06-22 — Claude (claude-sonnet-4-6)

**Goal:** Initial project review + v0.3.6 bug-fix release.

**Changes made:**

| File | Change |
|------|--------|
| `README.md` | Bumped version to 0.3.6; fixed version.json inaccuracy in versioning section; removed fixed roadmap bullets; kept CSV import bullet |
| `tools/runtime/CircuitOS.Runtime.csproj` | Version 0.3.5 → 0.3.6 (Version, FileVersion, AssemblyVersion) |
| `tools/runtime/Program.cs` | Health endpoint version string 0.3.5 → 0.3.6 |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion "1.1.1" → "0.3.6" (was hardcoded wrong, now matches app version) |
| `streamerbot-actions/StreamerbotReedeem.txt` | Fixed boost label showing on non-boosted collection pulls — now clears `activeBoostName` if rolled collection has no multiplier entry |
| `tools/package/Build-CircuitOSPackage.ps1` | `streamerbotIntegrationVersion` was hardcoded `"1.1.1"` in version.json manifest; now uses `$releaseVersion` from the EXE |
| `docs/patch-notes/v0.3.6.md` | Created Discord-ready patch notes |
| `HANDOFF.md` | Created this file |

**Bug fixes included in 0.3.6:**
1. Featured boost name was appended to ALL pull messages while boost was active, even for collections that weren't boosted. Now only shows when the rolled collection has an explicit multiplier.
2. Streamer.bot tab showed integration version "1.1.1" instead of the actual app version. Now reads "0.3.6".
3. Version.json reference in README was inaccurate (no such file exists). Corrected to name the actual source files.

| `tools/admin/index.html` | Added Import Items button to Events toolbar; added full `eventImportModal` with name, weight, salvage, start/end datetime-local fields |
| `tools/admin/app.js` | Added `eventImportPreview` state var; added `renderEventImportDestinationFields`, `populateEventImportTargets`, `buildEventImportPreview`, `renderEventImportPreview`, `openEventImport`, `closeEventImport`, `resetEventImport`, `applyEventImport`; wired all listeners and Escape handler |

**Next steps:**
- Distribute `dist/CircuitOS-Update-0.3.6.zip` to testers
- Move to **0.4 — Lower-Third Editor** (background/image, text/color/position/duration controls, live preview via existing OBS overlay state)

### 2026-06-22 — Claude (claude-sonnet-4-6) — Session 2

**Goal:** Implement 0.4 — Lower-Third Editor.

**Changes made:**

| File | Change |
|------|--------|
| `README.md` | Bumped version to 0.4.0 |
| `tools/runtime/CircuitOS.Runtime.csproj` | Version 0.3.6 → 0.4.0 |
| `tools/runtime/Program.cs` | Added `OverlayPath` to `RuntimeOptions`; added overlay path discovery (`DataPath/overlay` first, then repo path); added `/overlay-config.json` and `/overlay/{file}` HTTP routes; added `SendOverlayConfigFileAsync` and `SendOverlayFileAsync` helpers; updated version string to 0.4.0 |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.4.0" |
| `tools/admin/index.html` | Added "Overlay Editor" nav button; added `overlayView` section with iframe preview, layout/colors/timing/content editor panels |
| `tools/admin/app.js` | Added "overlay" to `viewTitles`; added `overlayConfig` and `overlayDirty` state vars; added `overlay-config` to `loadConfiguration` Promise.all; added `overlayField`, `overlayCheckbox`, `renderOverlayEditor`, `scaleOverlayPreview`, `saveOverlayConfig` functions; wired save/refresh button listeners and window resize handler |
| `tools/admin/styles.css` | Added overlay editor layout, preview wrap (iframe scale), field grid, color/range input rules |
| `docs/patch-notes/v0.4.0.md` | Created Discord-ready patch notes |
| `HANDOFF.md` | Updated version, phase, API list, remaining work |

**Architecture notes:**
- Overlay static files live in `DataPath/overlay/` (copied there by the package script from `overlays/lower-quarter/`)
- `overlay-state.json` (written by the Streamer.bot action) lives in `DataPath/overlay/overlay-state.json`
- The admin panel iframe at `http://127.0.0.1:8787/overlay/index.html?preview=1` works because the new runtime routes serve all overlay assets
- `scaleOverlayPreview()` scales the 1920px iframe down using CSS transform to fit the preview panel width
- The editor re-renders fields each time the overlay view is activated (simple, no stale state)

**Next steps:**
- Build and package: `dotnet publish` with correct flags, copy EXE, run `Build-CircuitOSPackage.ps1`
- Move to **0.5 — Profiles and Modules**

### 2026-06-22 — Claude (claude-sonnet-4-6) — Session 3

**Goal:** 0.4.1 polish — overlay editor fixes, configurable cooldown, background image, sidebar reorganization.

**Changes made:**

| File | Change |
|------|--------|
| `README.md` | Version → 0.4.1 |
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.4.1 |
| `tools/runtime/Program.cs` | Health endpoint version → "0.4.1" |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.4.1"; added `redeemCooldownSeconds` to `DefaultProfile()` and `NormalizeProfile()`; added cooldown injection via regex in `GenerateActionSource()` for `StreamerbotReedeem.txt` |
| `streamerbot-actions/StreamerbotReedeem.txt` | Added per-viewer 2-minute cooldown with Twitch refund on early re-redeem; moved viewerId/viewerName reads before lock; `const int CooldownSeconds = 120;` is now regex-replaceable |
| `data/system-profile.template.json` | Added `"redeemCooldownSeconds": 120` |
| `overlays/lower-quarter/overlay.js` | Disabled 500 ms poll in preview mode; added `backgroundImage` to `normalizeOverlayConfig` and `applyOverlayConfig` |
| `tools/admin/index.html` | Reorganized sidebar into nav groups (Collections, Viewers, Settings, Integrations, Tools); added `profileCooldown` number input to Branding |
| `tools/admin/app.js` | Added `redeemCooldownSeconds` to `defaultSystemProfile`; updated `updateProfileFromInputs`, `applySystemProfile`, `switchView` (auto-opens parent group); fixed iframe reload in `saveOverlayConfig` and `refreshOverlayPreviewButton` with timestamp cache-buster; added `backgroundImage` text field to `renderOverlayEditor` appearance section; added `profileCooldown` to input listener loop |
| `tools/package/Build-CircuitOSPackage.ps1` | Updated validation assertion from `profileSettingsNav` → `settingsNav` to match new sidebar structure |
| `docs/patch-notes/v0.4.1.md` | Created Discord-ready patch notes |
| `HANDOFF.md` | This entry |

**Next steps:**
- Distribute `dist/CircuitOS-Update-0.4.1.zip`
- Move to **0.5 — Profiles and Modules**

### 2026-06-22 — Claude (claude-sonnet-4-6) — Session 4

**Goal:** 0.4.2 — Fix overlay editor (editing, continuous refresh, background image, nav reorganization).

**Root cause identified:** Overlay statics (`overlay.js`) lived in `Data\overlay\` which the update package never replaces. Users on 0.4.0 still had the old overlay.js that polls every 500ms even in preview mode, had no `backgroundImage` support, and captured color values unreliably.

**Changes made:**

| File | Change |
|------|--------|
| `README.md` | Version → 0.4.2 |
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.4.2 |
| `tools/runtime/Program.cs` | Reordered overlayPath discovery: `Overlay/` (install root) now checked before `DataPath/overlay/`; `SendOverlayFileAsync` now accepts `dataPath` param and always serves `overlay-state.json` from `DataPath/overlay/` regardless of statics location; version → 0.4.2 |
| `tools/runtime/CircuitService.Core.cs` | integrationVersion → "0.4.2" |
| `tools/package/Build-CircuitOSPackage.ps1` | Overlay statics now copied to `Overlay\` folder (not `Data\overlay\`); `Overlay\` added to both full and update packages |
| `tools/package/package-files/UPDATE README.txt` | Added `Overlay\` to list of update contents |
| `tools/admin/app.js` | Added `backgroundImage: ""` to JS fallback overlayConfig; switched color/text input handling from `change` to `input` for immediate capture |
| `tools/admin/index.html` | Moved Overlay Editor button into settingsNav group; moved Settings group to bottom of sidebar (below Tools) |
| `docs/patch-notes/v0.4.2.md` | Created Discord-ready patch notes |
| `HANDOFF.md` | This entry |

**Architecture change — overlay statics location:**
- **Before**: `DataPath/overlay/overlay.js` (updated only by fresh install, never by update package)
- **After**: `InstallDir/Overlay/overlay.js` (in update package, takes priority over DataPath)
- `overlay-state.json` still served from `DataPath/overlay/` (written there by Streamer.bot action)
- Legacy installs (no `Overlay/` folder) still fall back to `DataPath/overlay/` until they update

**Next steps:**
- Distribute `dist/CircuitOS-Update-0.4.2.zip` — users MUST copy `Overlay\` folder too
- Move to **0.5 — Profiles and Modules**

### 2026-06-22 — Claude (claude-sonnet-4-6) — Sessions 5–7

**Goal:** 0.4.3–0.4.5 — Background image on overlay (three attempts), configurable text labels, image upload, preview iframe height.

**Root cause of background never showing (found in 0.4.5):** `html, body { background: transparent }` is a CSS shorthand that resets `background-image: none`, overriding any `--bg-image` variable set on body. Previous attempts (0.4.3/0.4.4) also placed the image inside the `.tracker` panel background stack, hidden behind near-opaque (`0.98`) gradients.

**Changes made:**

| File | Change |
|------|--------|
| `README.md` | Version → 0.4.5 |
| `tools/runtime/CircuitOS.Runtime.csproj` | Version → 0.4.5 |
| `tools/runtime/Program.cs` | Added `POST /api/overlay-image` and `GET /overlay-bg` routes; added `ReadRawBodyAsync` (10 MB limit); added `SendOverlayBackgroundAsync`; version → 0.4.5 |
| `tools/runtime/CircuitService.Core.cs` | Added `SaveOverlayBackground(byte[], string)` — validates MIME, deletes old `bg.*`, saves as `DataPath/overlay/bg.{ext}`, returns `{ ok, url, filename }`; integrationVersion → "0.4.5" |
| `overlays/lower-quarter/styles.css` | Split `html, body` rule: `html` keeps `background: transparent`; `body` gets explicit `background-color: transparent` + `background-image: var(--bg-image, none)` + cover/center; removed `--bg-image` from `.tracker` background stack (was hidden by 0.98-opacity gradients); added `--bg-image: none` to `:root` |
| `overlays/lower-quarter/overlay.js` | Added `labels` to `defaultOverlayConfig` and `normalizeOverlayConfig`; `applyOverlayConfig` sets `--bg-image` CSS variable on `:root` and writes labels to DOM elements; `renderState` uses config labels for status badge text; preview mode polling disabled |
| `data/overlay-config.template.json` | Added `labels` object with 6 default strings |
| `tools/admin/app.js` | Added `buildBgImageField` (Upload Image button → POST `/api/overlay-image` → stores `/overlay-bg?t=…`); added Labels section with 6 text fields; `profileCooldown` wired to input listener; refresh button uses timestamp cache-buster |
| `tools/admin/index.html` | Overlay Editor moved into Settings nav group; Settings group moved to bottom of sidebar; Labels panel added to overlay editor |
| `tools/admin/styles.css` | Preview iframe height 300 → 500px |
| `tools/package/Build-CircuitOSPackage.ps1` | Hash checksum failures on locked EXE made non-fatal (returns "LOCKED" string instead of crashing) |

**Architecture — background image:**
- Upload: `POST /api/overlay-image` receives raw bytes → saved as `DataPath/overlay/bg.{ext}`
- Serve: `GET /overlay-bg` looks for `bg.{png,jpg,gif,webp}` in `DataPath/overlay/`
- Config stores: `/overlay-bg?t=<timestamp>` as `backgroundImage` URL
- CSS: `body { background-image: var(--bg-image, none) }` — body fills the full OBS canvas; tracker panel sits on top

**Next steps:**
- Distribute `dist/CircuitOS-Update-0.4.5.zip`
- Move to **0.5 — Profiles and Modules**

### 2026-06-27 — Codex — 0.7 Twitch Settings UI closeout slice

**Goal:** Start finishing the 0.7 launch punch list from the active `C:\Dev\CircuitStreamSystem` repo copy, focusing on Twitch settings/status UX without packaging a release yet.

**Changes made:**

| File | Change |
|------|--------|
| `tools/admin/index.html` | Added a dedicated `twitchView` and sidebar nav entry before Streamer.bot. |
| `tools/admin/app.js` | Added `twitch` view title and `renderTwitchSettings()`; the page reflects current session mode, Twitch login, token freshness, live profiles, and reward-name readiness. Login/logout reuse existing `/api/twitch/login` and `/api/twitch/logout`. |
| `tools/admin/styles.css` | Added Twitch settings layout, Twitch-purple login treatment, readiness/reward rows, and responsive stacking. |
| `UI.md` | Marked the Twitch settings/login UI ask as initial-pass done and called out reward persistence as the next step. |
| `docs/patch-notes/0.7-dev-progress.md` | Recorded the Twitch Settings page and narrowed remaining Twitch UI work to reward-id persistence, live sync controls, and scope/config guidance. |

**Validation:** `node --check tools/admin/app.js` passed using the bundled Codex Node runtime.

**Still ahead for 0.7:** persist reward-id ↔ profile mapping, turn the Twitch page into real reward selection/sync controls, add scope/re-login guidance, then verify in cloud/Twitch mode with live credentials before packaging.

### 2026-06-28 — Codex — 0.7 Twitch reward management: create/sync + delete cleanup

**Goal:** Continue the 0.7 native Twitch path while preserving the product decision that CircuitOS stays local-first by default, with cloud optional and Twitch capabilities available from the local desktop bridge.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/TwitchRuntime.cs` | Added reusable `SyncRewardForProfile` and `DeleteRewardForProfile` helpers. Sync validates live profile + redemption name, persists reward id/title/cost to `twitch-rewards.json`; delete calls the provider and clears the stored `rewards.channelPoints` mapping. |
| `tools/runtime/TwitchHelix.cs` | Added `DeleteReward(rewardId)` for app-manageable channel-point rewards. |
| `tools/runtime/Program.cs` | Added `/api/twitch/reward-sync` and `/api/twitch/reward-delete` local runtime endpoints. Both use cached Twitch login, work in local or cloud-backed mode, and return refreshed profiles. |
| `tools/admin/app.js` | Wired Twitch Settings `Create/Sync` to the sync endpoint and enabled guarded `Delete` for already-synced rewards. `Edit` remains staged. |
| `tools/runtime.tests/Program.cs` | Expanded Twitch reward smoke coverage to verify persistence, profile summary exposure, delete provider call, and local mapping cleanup. |
| `README.md`, `docs/patch-notes/0.7-dev-progress.md`, `docs/0.7-cloud-foundation.md`, `HANDOFF.md` | Updated 0.7 status around local-first/cloud-optional Twitch capabilities and reward-management progress. |

**Validation:**

- `node --check tools/admin/app.js` passed.
- `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release` passed with 0 warnings/errors.
- `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions` passed, including Twitch reward delete cleanup.

**Still ahead for 0.7:** Twitch reward edit/cost controls, scope/re-login guidance in the Twitch page, live verification with the user's Twitch credentials after restarting the dev runtime, then continue the UI launch punch list and packaging path.

### 2026-06-28 — Codex — 0.7 Twitch reward dropdown / attach existing rewards

**Goal:** Let users reuse a Twitch channel-point reward they already created instead of forcing CircuitOS to create a new one.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/TwitchHelix.cs` | Added `ListRewards()` for current channel rewards and extended `CustomReward` with `Manageable` so the UI can distinguish CircuitOS-manageable rewards from attach-only rewards. |
| `tools/runtime/TwitchRuntime.cs` | Added `AttachRewardForProfile`; stored reward mappings are now reused by the listener instead of auto-creating a new reward on restart. Stored mappings include `manageable`. Delete refuses attach-only rewards before calling Twitch. |
| `tools/runtime/Program.cs` | Added `GET /api/twitch/rewards`; `/api/twitch/reward-sync` now accepts an optional `rewardId` to attach an existing reward. |
| `tools/admin/app.js` | Twitch Settings now loads current Twitch rewards, shows them in a dropdown per live profile, and sends the selected reward id during sync. Non-manageable rewards are labelled attach-only and cannot be deleted from CircuitOS. |
| `tools/admin/styles.css` | Added compact reward-cell/select styling. |
| `tools/runtime.tests/Program.cs` | Smoke coverage now verifies attach-existing reward mapping, listener routing from stored mapping, profile summary exposure, and delete guard for attach-only rewards. |
| `README.md`, `docs/patch-notes/0.7-dev-progress.md`, `HANDOFF.md` | Updated 0.7 status and continuity notes. |

**Validation:**

- `node --check tools/admin/app.js` passed.
- `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release` passed with 0 warnings/errors.
- `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions` passed, including attach-existing reward coverage.

**Still ahead for 0.7:** live UI verification with the user's Twitch account after restarting the dev runtime, then reward edit/cost controls and clearer scope/re-login guidance.

### 2026-06-28 — Codex — 0.7 Twitch reward edit/cost controls

**Goal:** Finish the first functional reward-management loop by making the Twitch Settings `Edit` action update managed channel-point reward title/cost instead of leaving it staged.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/TwitchHelix.cs` | Added `UpdateReward(rewardId, title, cost, prompt)` for Twitch-manageable rewards. |
| `tools/runtime/TwitchRuntime.cs` | Added `UpdateRewardForProfile`; validates stored reward ownership, updates Twitch, persists title/cost/managed state, and updates the profile `redemptionName` so CircuitOS and Twitch do not drift. |
| `tools/runtime/Program.cs` | Added `/api/twitch/reward-update` endpoint. |
| `tools/admin/app.js` | Enabled `Edit` for managed synced rewards. It prompts for title and cost, posts to the runtime endpoint, refreshes profile/reward state, and keeps attach-only rewards protected. |
| `tools/runtime.tests/Program.cs` | Smoke coverage verifies managed reward edit provider call, stored title/cost update, and profile redemption-name sync. |
| `README.md`, `docs/patch-notes/0.7-dev-progress.md`, `HANDOFF.md` | Updated 0.7 status and continuity notes. |

**Validation:**

- `node --check tools/admin/app.js` passed.
- `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release` passed with 0 warnings/errors.
- `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions` passed, including reward edit/attach/delete coverage.

**Still ahead for 0.7:** restart and live-test the Twitch Settings reward list/attach/edit/delete loop with the Twitch account, then continue UI launch polish and packaging prep.


### 2026-06-28 — Codex — 0.7 Twitch permissions guidance

**Goal:** Make the Twitch Settings page self-explanatory when tokens are expired or permissions are stale after native Twitch feature updates.

**Changes made:**

| File | Change |
|------|--------|
| `tools/admin/app.js` | Added a Twitch permissions card listing reward management, redemption intake, and chat reply permissions. The card shows a refresh/login action and highlights expired tokens. Added an attach-only explanation card so users know why some existing rewards cannot be edited/deleted by CircuitOS. |
| `tools/admin/styles.css` | Added compact scope-list styling and warning border treatment. |
| `README.md`, `docs/patch-notes/0.7-dev-progress.md`, `HANDOFF.md` | Updated 0.7 status and continuity notes. |

**Validation:**

- `node --check tools/admin/app.js` passed.
- `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release` passed with 0 warnings/errors.
- `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions` passed, including reward edit/attach/delete coverage.

**Still ahead for 0.7:** restart and live-test the Twitch Settings reward list/attach/edit/delete loop with the Twitch account, then continue UI launch polish and packaging prep.



### 2026-06-28 — Codex — Known bug fix: first-run draft command collisions

**Goal:** Fix the bug shown in the user screenshot: creating a blank/new profile from first-run failed because default command names collided with existing live profiles, so the profile/catalog did not save cleanly.

**Root cause:** `CompleteFirstRun()` validated normally, then called `SaveSystemProfile()`. `SaveSystemProfile()` correctly blocks command collisions when the active profile is live, but first-run is initializing an editing draft. That made draft creation inherit the go-live collision guard too early.

**Changes made:**

| File | Change |
|------|--------|
| `tools/runtime/CircuitService.Core.cs` | `CompleteFirstRun()` now saves the profile directly with the same atomic/backup path after configuration saves, bypassing live command-collision checks for draft initialization. Activation still enforces collisions. |
| `tools/runtime.tests/Program.cs` | Added regression coverage: a draft profile can first-run with commands already used by another live profile, remains inactive, and is still blocked when activated until commands are renamed. |
| `Known Bugs.txt` | Marked the new-profile/first-run collision bug fixed in source. |

**Validation:**

- `node --check tools/admin/app.js` passed.
- `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release` passed with 0 warnings/errors.
- `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions` passed, including the new first-run draft collision regression.

**Still open from `Known Bugs.txt`:** stable 0.6.0.8 native multi-profile limitation, duplicate variant labels such as `shiny shiny`, and stable 0.6.0.8 duplicate-protection behavior.

### 2026-06-28 — Codex — Known bug fix: duplicate-looking variant labels
- Fixed the "shiny shiny" class of variant display bug in source 0.7.
- `PullEngine.RollVariants` now trims labels and tracks seen labels case-insensitively; the paste-ready Streamer.bot redeem source now mirrors that behavior.
- Added a smoke-test regression that forces `SHINY`, ` shiny `, and `LARGE` at 100% chance and confirms the display stays `SHINY LARGE ...`, not `SHINY SHINY ...`.
- Validation passed: `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release`, `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions`, and `node --check tools/admin/app.js`.
### 2026-06-28 — Codex — Known bug hardening: native multi-profile routing
- Addressed the source-side cause of the native multi-profile pain point. The dispatcher already supported reward-id/profile-id and command-word routing; the missing pieces were stale listener lifecycle and collision guards around Twitch rewards.
- Added live-profile validation for duplicate `redemptionName` values. Two live profiles can no longer share the same channel-point reward title, because Twitch reward sync can collapse same-title rewards onto one reward id.
- Added Twitch reward-id safety in `TwitchRuntime`: sync/attach now rejects a reward id already attached to another live profile, and reward-map construction skips/logs duplicate ids instead of silently letting the last profile win.
- The running native Twitch listener now refreshes after Twitch login/logout, profile live-state changes, and reward sync/edit/delete, so it rebuilds reward-id -> profile routing without requiring an app restart.
- Smoke tests now cover command collisions, redemption-title collisions, duplicate reward-id attach blocking, and multi-profile runtime dispatch with unique commands/reward title.
- Validation passed: `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release`, `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions`, and `node --check tools/admin/app.js`.
- Still needs live Twitch verification with two live profiles and two distinct channel-point rewards before the Known Bugs note can be marked fully verified.
### 2026-06-28 — Codex — Attach-only Twitch reward fulfillment fix
- User live-tested native multi-profile redemptions with two live rewards: `Catch a Pokemon` routed to profile `default`; `Circuit Component` routed to `circuit-components` and fulfilled successfully.
- The remaining failure was not profile routing. It was Twitch fulfillment for an attach-only reward: Twitch returned 403 because the reward was not created by this Twitch Client-ID.
- Fixed `TwitchRuntime` so the live EventSub route map carries `Manageable`. Managed rewards still call `UpdateRedemptionStatus`; attach-only rewards record the pull and send chat, but skip Twitch fulfillment/cancel and log `RECORDED (attach-only reward; Twitch fulfillment skipped)`.
- Added smoke coverage that stored attach-only rewards preserve `Manageable=false` in the native route map.
- Validation passed: `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release`, `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions`, and `node --check tools/admin/app.js`.
- Next live check: rebuild/run dev build, redeem `Catch a Pokemon` again, expect no 403 and a `RECORDED (attach-only reward...)` log plus normal chat output.
### 2026-06-28 — Codex — Sidebar theme coverage source fix
- User confirmed the attach-only Twitch reward retest worked, so the native multi-profile known bug is now fixed/live-verified in source 0.7.
- Patched Appearance theme application so sidebar/topbar chrome receives derived variables from the selected profile colors: `--accent`, `--red-border`, `--sidebar-bg`, `--sidebar-card`, `--sidebar-card-hover`, and `--chrome-bg`.
- Updated sidebar/topbar CSS to use those variables instead of hard-coded dark/red rgba values.
- Updated `Known Bugs.txt`, `UI.md`, and `docs/patch-notes/0.7-dev-progress.md` to reflect the verified Twitch fix and the sidebar theme source fix.
- Validation passed: `node --check tools/admin/app.js`, `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release`, and `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions`.
- Still needs a quick visual check in the running admin app: change Appearance colors and confirm the sidebar/nav/footer/topbar follow the theme.
### 2026-06-28 — Codex — Overview Pull Rates source fix
- User visually confirmed the sidebar/topbar theme fix looked better; `UI.md` global theme item is now marked verified.
- Patched the Overview Pull Rates panel so the panel itself is no longer a `data-jump-view="ratelab"` clickable card. The `Tune in Rate Lab` button remains the navigation control and was corrected after user feedback.
- Hardened the global `data-jump-view` click handler to ignore clicks that begin inside form controls/buttons/links, reducing accidental navigation from embedded controls.
- Updated Overview range sliders to drive a simple raw `--fill` percentage. Removed the thumb-width compensation after visual testing showed it could overshoot past the handle; CSS keeps the themed border/thumb.
- During validation, repaired accidental JavaScript newline literal splits in `showNotice`, wizard item parsing, patch-note generation, and CSV row parsing; `node --check tools/admin/app.js` is clean.
- Validation passed: `node --check tools/admin/app.js`, `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release`, and `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions`.
- Still needs a quick visual check in the running admin app: drag Pull Rates on Overview and confirm the fill/handle stay together; click Tune in Rate Lab and confirm it navigates; check Action Center spacing.
