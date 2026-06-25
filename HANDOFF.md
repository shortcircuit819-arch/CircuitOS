# CircuitOS — AI Handoff Log

This file is the source of truth for AI-to-AI continuity between Claude and
ChatGPT Codex. Update the **Current State** and **Session Log** sections at
the end of every working session before stopping.

---

## Project Identity

| Field | Value |
|-------|-------|
| Project | CircuitOS — configurable Twitch collection-game platform |
| Default game | Circuit Components (electronics-themed) |
| Current version | **0.6.0.8** (installed/packaged); 0.7 Phase 1 refactor in source, not yet packaged |
| Phase | 0.6 complete and validated on stream. **0.7 Phase 2 + 2b COMPLETE for the desktop-on-cloud bridge** (all verified live via `--cloud`): app reads/writes game data from Appwrite, profile management works, every cloud save snapshots a recovery point. Done: host-agnostic `CircuitService`; `AppwriteDataStore` (core + profiles + backups, all in `profile_data`); `PullEngine` (tested); revived smoke harness; config loader + diagnostics. Deferred to Phase 5: overlay-background Storage bucket. Next: **Phase 3 — Twitch OAuth via Auth0** (real tenant id; account-gated), then Phase 4 (EventSub + `PullEngine` = native zero-config Twitch). No release cut (default-local preserved; installed 0.6.0.8 untouched). |
| Repo root | `C:\Dev\CircuitStreamSystem` |
| Live data path | `C:\Users\nicho\Documents\CircuitOS\Data` (profiles under `Data\profiles\<id>`; active profile `circuit-components`) |

---

## Architecture at a Glance

```
CircuitStreamSystem/
├── tools/runtime/          .NET 9 Windows Forms app (HTTP server + WebView2 UI)
│   ├── Program.cs          HttpListener on 127.0.0.1:8787, request routing
│   ├── CircuitService.Core.cs   Config, validation, backup, Streamer.bot generation
│   ├── CircuitService.AnalyticsRoles.cs
│   ├── CircuitService.Backups.cs
│   ├── CircuitService.Overlay.cs
│   ├── CircuitWindow.cs    Windows Forms shell (WebView2)
│   └── CircuitOS.Runtime.csproj
│
├── tools/admin/            Browser frontend (vanilla JS, no framework)
│   ├── index.html          UI shell: first-run wizard, editor, analytics
│   ├── app.js              ~3,800 lines — all rendering, API calls, state
│   ├── styles.css
│   └── runtime/CircuitOS.exe   Published binary (copy here after dotnet publish)
│
├── streamerbot-actions/    Paste-ready C# for Streamer.bot (plain .txt files)
│   ├── StreamerbotReedeem.txt       Main pull + inventory write
│   ├── StreamerbotCatalogCommands.txt
│   ├── StreamerbotCollection.txt
│   └── StreamerbotSalvage.txt
│
├── data/                   Starter/dev JSON data (not the live data folder)
├── docs/                   User and maintainer documentation
│   └── patch-notes/        Discord-ready release notes (one file per version)
├── dist/                   Built release packages
├── AGENTS.md               Original (outdated) agent instructions
└── HANDOFF.md              ← this file
```

**Key API endpoints (all local, 127.0.0.1:8787):**
- `GET /api/health` → version string, data path
- `GET /api/config` → components catalog + boost config
- `GET /api/profile` → branding, commands, colors, messages
- `GET /api/analytics` → inventory stats
- `GET /api/backups` → backup history
- `GET /api/overlay-config` → overlay config JSON (falls back to template)
- `POST /api/save` → save config changes
- `POST /api/setup` → generate Streamer.bot C# actions
- `POST /api/overlay-config` → save overlay config
- `GET /overlay-config.json` → raw overlay config file (used by overlay.js)
- `GET /overlay/{index.html|styles.css|overlay.js|overlay-state.json}` → overlay static/state files

**Data files (live folder, not repo):**
- `components.json` — collection catalog (weights, parts, salvage values)
- `inventory.json` — viewer inventories (locked with `inventory.lock`)
- `featured-boost.json` — featured stream weight multipliers
- `system-profile.json` — branding, terminology, message templates, colors
- `discord-role-awards.json` — completion queue for Discord roles

---

## Version String Locations

All five must match when cutting a release:

| File | Location | Field |
|------|----------|-------|
| `tools/runtime/CircuitOS.Runtime.csproj` | `<Version>`, `<FileVersion>`, `<AssemblyVersion>` | Assembly metadata |
| `tools/runtime/Program.cs` | `/api/health` response | Runtime version shown in UI footer |
| `tools/runtime/CircuitService.Core.cs` | `integrationVersion` in `/api/setup` response | Integration version shown on Streamer.bot tab |
| `tools/runtime/CircuitService.Modules.cs` | `circuitosVersion` in module manifest | Version stamped into exported `.circuitmodule` files |
| `README.md` | "Current application version" line | Documentation |

The `integrationVersion` in `CircuitService.Core.cs` was historically a separate
version (was `"1.1.1"` while the app was `"0.3.5"`). As of 0.3.6 it is kept in
sync with the app version.

---

## Build & Release Workflow

```powershell
# 1. Publish the .NET runtime
dotnet publish tools\runtime\CircuitOS.Runtime.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o tools\runtime\publish

# 2. Copy EXE to the admin folder
Copy-Item tools\runtime\publish\CircuitOS.exe tools\admin\runtime\CircuitOS.exe -Force

# 3. Run the packaging script
powershell -ExecutionPolicy Bypass -File tools\package\Build-CircuitOSPackage.ps1
```

The packaging script creates:
- `dist/CircuitOS-Windows-x64.zip` — fresh install
- `dist/CircuitOS-Update-{prev} to {new}/` — data-free update package

---

## Coding Conventions

- Viewer inventory is **never** overwritten without a timestamped backup first
- Atomic writes: write to `.tmp` → validate → `File.Replace` (not direct overwrite)
- `inventory.lock` file-lock prevents concurrent Streamer.bot + admin writes
- All API responses include `"ok": true/false`
- Streamer.bot `.txt` templates use `folderPath` replacement at generation time
- Message templates use `{placeholder}` syntax validated server-side
- Collection keys: lowercase alphanumeric + underscores only (`^[a-z0-9][a-z0-9_]*$`)
- Component IDs: same pattern, must be globally unique across all collections

---

## Tool Capabilities (Added 2026-06-22)

The AI assistant now has access to browser and desktop control plugins. These change the
development workflow significantly — use them to verify UI changes before reporting them
done rather than waiting for the user to test and report back.

| Tool | What it does | When to use |
|------|-------------|-------------|
| **Claude in Chrome** (`mcp__Claude_in_Chrome__*`) | Navigate the running admin panel, take screenshots, click buttons, fill fields, read the DOM | After every admin UI change. Take a screenshot to verify the rendered result before packaging. |
| **Computer use** (`mcp__computer-use__*`) | Control the Windows desktop — start/stop CircuitOS.exe, verify files on disk, screenshot the native app | Full-stack verification: file layout after profile switch, Streamer.bot action path injection, overlay rendering in the real app |
| **Preview** (`mcp__Claude_Preview__*`) | Render HTML files directly without the full server | Isolated overlay and CSS testing — verify `styles.css` changes render correctly before building |
| **Visualize** (`mcp__visualize__*`) | Generate architecture diagrams and UI mockups inline | At the START of each feature before writing code. Get design sign-off, then implement. |
| **Session management** (`mcp__ccd_session__*`) | mark_chapter, spawn_task, dismiss_task | Already in use. Mark chapters at phase boundaries. Spawn tasks for out-of-scope issues caught during work. |

**Key workflow change:** Before these tools existed, every visual iteration required build → user installs → user looks → user reports → repeat. That loop is why 0.4 took 6 patch releases and the overlay is still not fully resolved. Going forward:
1. Make change
2. Verify with Chrome/Preview/Computer-use immediately
3. Package only when verified

---

## Known Remaining Work

### 0.4 Overlay — mostly resolved through 0.5.0.6–0.5.0.8

Most of the original overlay gaps were closed during the 0.5 sprint:

- **Preview accuracy** — RESOLVED in 0.5.0.6. The editor now has a Normal/Rare/Complete/
  Duplicate state picker and renders a dummy tracker permanently in preview mode (no longer
  depends on a live `overlay-state.json`).
- **Background image not showing** — RESOLVED in 0.5.0.8. Root cause was the `html, body`
  background shorthand resetting `background-image`, plus a `/overlay-bg` URL that didn't
  resolve in file:// mode. Now stored as a relative `bg.png` filename.
- **OBS path mismatch** — RESOLVED in 0.5.0.7. Overlay statics are published to
  `DataPath/profiles/<id>/overlay/` alongside the state file, and the editor surfaces the
  exact Local-file path with a Copy button.

Remaining minor UX nits (optional, low priority):

- **Panel overlay darkness clarity** — the Opacity slider (0.98 default = near-opaque, lower =
  image shows through) is not obviously tied to the uploaded panel image. A clearer label
  would help, but the live preview now demonstrates the relationship.
- **Body vs panel labelling** — the "background image" label could state more explicitly that
  it fills the OBS canvas behind the tracker card, not the card itself.

OBS Browser Source: Local file mode → `DataPath/profiles/<id>/overlay/index.html`
(CircuitOS publishes overlay statics here on startup; Streamer.bot writes state to the same folder.)

The overlay.js reads config from `../overlay-config.json` (= `DataPath/profiles/<id>/overlay-config.json`)
and state from `overlay-state.json` (= `DataPath/profiles/<id>/overlay/overlay-state.json`).
HTTP mode (`http://127.0.0.1:8787/overlay/index.html`) also works when CircuitOS is running.

---

## Strategic Direction — Cloud Migration at 0.7

**Decided 2026-06-22.** CircuitOS will migrate from a local Windows app to a cloud/web
platform at version 0.7, coinciding with Twitch integration. This is Option 2 of three
considered paths: build locally through 0.6, but design 0.5 with a data abstraction layer
so the 0.7 cloud switch is surgical rather than a rewrite.

### What changes at 0.7

| Now (local) | 0.7+ (cloud) |
|-------------|--------------|
| .NET Windows Forms + HttpListener | Hosted web backend |
| JSON files on disk | Appwrite (DB, auth, file storage) |
| Streamer.bot C# actions | Twitch EventSub webhooks (Streamer.bot becomes optional) |
| Admin panel via localhost | Admin panel via browser, any device |
| OBS overlay at `localhost:8787` | OBS overlay at cloud URL |
| No auth | Twitch OAuth via Auth0 |
| Local backups | Cloud-managed, per-streamer |

### Chosen cloud stack (planned)

- **Appwrite** — backend (database, file storage, functions). Open source, self-hostable
  during development. MCP plugin already installed with 13 skills.
- **Auth0** — Twitch OAuth for streamer authentication. MCP plugin already installed with
  44 skills.
- **Discord** — patch note posting, role award notifications. MCP plugin already installed
  with 2 skills.

### The abstraction layer requirement (critical for 0.5)

Every data access in 0.5 must go through an `IDataStore` interface, not hardcoded file paths.
The 0.5 implementation is `LocalFileDataStore` (wraps current JSON file logic). At 0.7 we
add `AppwriteDataStore` and swap it in via dependency injection. If 0.5 skips the interface
and uses paths directly, 0.7 becomes a rewrite instead of a swap.

```csharp
// The interface — 0.5 defines it, 0.7 gets a second implementation
public interface IDataStore
{
    Task<string?> ReadFileAsync(string name);
    Task WriteFileAsync(string name, string content);
    Task<bool> ExistsAsync(string name);
    Task DeleteAsync(string name);
    Task<Stream?> ReadBinaryAsync(string name);
    Task WriteBinaryAsync(string name, Stream content, string contentType);
    string ProfileId { get; }
}

// 0.5: local file implementation
public class LocalFileDataStore : IDataStore { /* wraps DataPath/profiles/{id}/ */ }

// 0.7: cloud implementation
public class AppwriteDataStore : IDataStore { /* wraps Appwrite SDK */ }
```

All `CircuitService.*` classes receive `IDataStore` via constructor. `CircuitService.Core.cs`
stops calling `File.*` directly and calls `_store.ReadFileAsync("components.json")` etc.

---

## 0.5 Plan — Profiles and Modules

### What it means

Currently CircuitOS manages exactly one game: one catalog, one inventory, one set of branding
and settings. 0.5 adds the ability to run multiple independent games from one installation —
switch between them without mixing data, and move collection catalogs between games as portable
modules. It also lays the `IDataStore` abstraction that makes 0.7 possible without a rewrite.

**Profile**: a complete, isolated game instance.
- Its own catalog, inventory, branding, featured boost, overlay config, and backups.
- Its own Streamer.bot actions (generated with the correct data path for that profile).
- Represented locally as a sub-folder; at 0.7 becomes a row in Appwrite.

**Module**: a portable collection catalog — just the collections and their parts, no inventory.
- Can be exported from any profile and imported into any other.
- The collections/events editor is the natural source of modules.

### Proposed local data layout (0.5)

```
DataPath/                              ← e.g. C:\CircuitOS\Data
├── profiles/
│   ├── circuit-components/            ← migrated from old DataPath root
│   │   ├── components.json
│   │   ├── inventory.json
│   │   ├── system-profile.json
│   │   ├── featured-boost.json
│   │   ├── discord-role-awards.json
│   │   ├── overlay/
│   │   │   ├── overlay-state.json
│   │   │   ├── overlay-config.json
│   │   │   └── bg.*
│   │   └── backups/
│   └── pokemon/                       ← second profile (example)
│       └── ...
└── active-profile.txt                 ← name of the currently active profile folder
```

`LocalFileDataStore` is initialized with the active profile folder path. All service classes
hold an `IDataStore` reference — they never construct file paths themselves.

### Migration on first 0.5 launch

If `DataPath/profiles/` does not exist:
1. Create `DataPath/profiles/circuit-components/`
2. Move existing data files into it
3. Write `active-profile.txt` = `circuit-components`
4. Prompt user to regenerate Streamer.bot actions with the new path

Automatic and reversible — original files renamed, not deleted, until the user confirms.

### Key risks and decisions

| Risk | Decision |
|------|----------|
| Streamer.bot actions are path-hardcoded | Each profile generates its own actions. Setup tab shows active profile path. User re-pastes on profile switch. |
| Switching profiles mid-stream | Safe — bot keeps hitting old path. Admin panel shows a warning banner. |
| Profile name collisions | Profile IDs enforce collection key rules (`^[a-z0-9][a-z0-9_]*$`). |
| Backups per profile | Live inside the profile folder — portable with the data. |
| `IDataStore` scope | One store instance per active profile. Switching profiles swaps the instance. |

### Development sequence

**Phase 0 — Design (before any code)**
Use `Visualize` to produce:
- `IDataStore` interface and data flow diagram
- UI mockup of the profile switcher (header dropdown + management panel)
Get approval on both before writing a line of 0.5 code.

**Phase 1 — Abstraction layer**
- Define `IDataStore` interface in `tools/runtime/`
- Implement `LocalFileDataStore` wrapping current file logic
- Refactor all `CircuitService.*` classes to use `IDataStore` (no direct `File.*` calls)
- No visible change to the user — behavior identical, architecture ready for 0.7

**Phase 2 — Profiles data layer**
- Add profile management to `CircuitService.Core.cs` (`ListProfiles`, `CreateProfile`, `SwitchProfile`, `DeleteProfile`)
- Add migration logic (runs once on startup if `profiles/` missing)
- New API routes: `GET /api/profiles`, `POST /api/profiles/switch`, `POST /api/profiles/create`
- Verify with **Computer use**: folder structure, switch, file locations

**Phase 3 — Admin UI**
- Profile switcher in admin panel header
- Profile management panel (list, create, rename, delete, duplicate)
- Setup tab warning when Streamer.bot path is stale
- Verify with **Claude in Chrome**: screenshot every state, test all flows

**Phase 4 — Modules**
- Export profile catalog as `.circuitmodule` (zip with JSON + metadata)
- Import and merge into active profile
- Verify with **Chrome**: full import flow

### What NOT to build in 0.5

- Cloud sync or remote profiles (that's 0.7)
- `AppwriteDataStore` implementation (define the interface, leave the impl for 0.7)
- Automatic Streamer.bot update (no write API exists)
- Auth or multi-user (single streamer until 0.7)

---

## Current State

| Field | Value |
|-------|-------|
| Released version | **0.6.0.8** (shipped, validated on stream; installed app is this) |
| In development | **0.7 — Cloud Platform + Twitch** (Phases 1–3 done, UNRELEASED; default-local preserved, no version bump, EXE not repackaged) |
| Active profile data path | `C:\Users\nicho\Documents\CircuitOS\Data\profiles\circuit-components` |
| Data root | `C:\Users\nicho\Documents\CircuitOS\Data` (holds `appwrite.local.json`, `twitch.local.json`, `twitch-tokens.local.json`) |

### 0.7 development status (read first for a cold start)

**Current focus:** the recent work stream has been the admin UI polish backlog from `UI.md` — primarily the Overview / Configure / Collections / Messages experience in `tools/admin`, verified live in-browser. The cloud/runtime pieces are still present and validated, but the active iteration has been UI polish rather than backend expansion.

**⚠️ FIRST TASK: live-verify the row-addressing fix.** The bug (re-push "verified 0") is **fixed in
source** (2026-06-24, see Session Log) but not yet re-verified against live cloud — run
`--push-to-appwrite` (expect 6/6) → `--cloud`. Then optionally a `local-dev → Twitch-id` migration so
login/logout doesn't strand data, and clean up any pre-fix `local-dev` rows in the console.

**What's built and verified live (all behind `--cloud`; local mode 100% unchanged):**
- Data layer swapped to Appwrite: app reads/writes catalog/profile/boost/inventory from `profile_data`
- Profile management + a rolling backup recovery point (both inside `profile_data`)
- Twitch OAuth login/logout in-app (footer session panel) + CLI `--twitch-login`; tenant = Twitch user id
- Shared `PullEngine` (the roll logic for Phase 4), revived smoke harness

**Run cloud mode (from the build output — pass --ui/--actions since there's no `App` folder there):**
```
dotnet "tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll" --cloud \
  --data "<DataRoot>" --ui "tools/admin" --actions "streamerbot-actions"
```
**Diagnostics (all open a dialog):** `--check-appwrite`, `--appwrite-roundtrip`, `--push-to-appwrite`,
`--appwrite-profiles`, `--appwrite-backups`, `--twitch-login`. Local app: drop `--cloud`.

**New 0.7 source files:** `tools/runtime/{IDataStore.cs (ILocalDataStore split), LocalFileDataStore.cs,
AppwriteDataStore.cs, AppwriteOptions.cs, PullEngine.cs, RedemptionEngine.cs, CommandEngine.cs, TwitchOptions.cs, TwitchAuth.cs}`,
`tools/runtime/CircuitOS.Runtime.csproj` (+Appwrite 5.1.0). `CircuitService` now takes `IDataStore`.

**Config files (gitignored, in Data root; user holds the secrets — assistant must NOT read them):**
`appwrite.local.json` {endpoint, projectId, apiKey, databaseId, collectionId};
`twitch.local.json` {clientId, clientSecret, redirectUri=http://localhost:8765}; `twitch-tokens.local.json` (cached).
Appwrite: nyc region, project `6a3b1af3002de5ef906b`, db `6a3b1b19000359f605af`, table `profile_data`
(cols userId/profileId/dataKey/json + unique index). Twitch user: `shortcircuit_tv` (id `103925885`).

**Setup docs:** `docs/0.7-cloud-foundation.md`, `0.7-appwrite-dev-setup.md`, `0.7-twitch-auth-setup.md`.

**Remaining 0.7:** live-verify the row-addressing fix → Phase 4 (EventSub function + reward creation via the shared engines,
the native zero-config path) → Phase 5 (hosted admin + cloud overlay + Storage bucket + Auth0). Then
fold `--cloud` into config and cut the 0.7 release.

### 0.6 (released)
0.6 — Item Variants + Tiers — feature complete and validated on stream (0.6.0.8). Variants, rarity tiers,
tier-weighted rolling, Rate Lab breakdown, simulation, bulk tier assignment, CSV tier import, plus the
0.6.0.6/0.6.0.7/0.6.0.8 fixes. See the 0.6.0.x session-log entries.

**Version string locations (all must match):**
- `tools/runtime/CircuitOS.Runtime.csproj` → `<Version>`, `<FileVersion>`, `<AssemblyVersion>`
- `tools/runtime/Program.cs` → `/api/health` response
- `tools/runtime/CircuitService.Core.cs` → `integrationVersion` in `/api/setup` response
- `tools/runtime/CircuitService.Modules.cs` → `circuitosVersion` in module manifest
- `README.md` → "Current application version"

**Profile data layout (as of 0.5):**
```
DataPath/
├── profiles/
│   ├── default/          ← migrated from old root on first 0.5 launch
│   │   ├── components.json
│   │   ├── inventory.json
│   │   ├── system-profile.json
│   │   ├── featured-boost.json
│   │   ├── discord-role-awards.json
│   │   ├── overlay-config.json
│   │   ├── overlay/
│   │   │   ├── overlay-state.json
│   │   │   └── bg.*
│   │   ├── config-backups/
│   │   └── profile-meta.json   ← {id, name, createdAt}
│   └── <other-profile>/
└── active-profile              ← plain text file containing active profile id
```

**New API endpoints added in 0.5:**
- `GET /api/profiles` → list profiles + activeProfileId
- `POST /api/profiles` → profile operations (create / switch / rename / delete)
- `GET /api/modules/export` → export active profile as `.circuitmodule` JSON
- `POST /api/modules/import` → import `.circuitmodule`, creates new profile

**Key files added in 0.5:**
- `tools/runtime/IDataStore.cs` — data access interface
- `tools/runtime/LocalFileDataStore.cs` — file-system implementation
- `tools/runtime/CircuitService.Profiles.cs` — profile CRUD
- `tools/runtime/CircuitService.Modules.cs` — module export/import

---

## Session Log

### FIXED 2026-06-24 — AppwriteDataStore row addressing desync (was "verified 0")

The row-addressing bug below is **fixed in source** (built clean; live re-verify is the user's step —
needs cloud credentials). Kept here for history.

**Was:** `--push-to-appwrite` over a non-empty table reported "Pushed 6, **verified 0**", then `--cloud`
said "Catalog not found." Cause: `AppwriteDataStore` addressed each row by a SHA-256-derived id
(`RowId(userId, profileId, key)`). When a stored row's real `$id` no longer matched the recomputed hash
(tenant swap, or an earlier hash formula), `UpsertRow(newId,…)` resolved the `unique_profile_key` conflict
against the OLD-id row and updated it, so the follow-up `GetRow(newId)` 404'd → verify failed.

**Fix applied:** `TryGetRow` now resolves rows via `ListRows` on the unique index
`(userId, profileId, dataKey)` — **confirmed in the console as `unique_profile_key`, all three columns** —
and returns the real `$id`. `UpsertJson` `UpdateRow`s that `$id` when present, else `CreateRow(ID.Unique())`.
The derived `RowId` method and the `System.Security.Cryptography` import are removed. Also: `--appwrite-profiles`
and `--appwrite-backups` (Program.cs) hardcoded the `local-dev` tenant — switched to `ResolveTenant(dataRoot)`
so all four cloud diagnostics + `--cloud` agree on the active tenant. No schema change.

**Verify (user, live):** `--push-to-appwrite` (expect 6/6) → `--cloud`. If old `local-dev` rows linger from
before the fix, they're harmless; delete them in the console for tidiness. Local files remain the source of truth.

---

### 2026-06-24 — User (Codex, outside this session) — 0.7 cloud: tenant migration, client-side filtering, auto port

Backend changes the user developed outside this Claude session (recorded here for continuity; committed
cleanly while untangling a mixed commit):

- **`AppwriteDataStore.MigrateRowsToTenant`** — moves `local-dev` rows to the real Twitch-id tenant; run on
  `--cloud` startup (`Program.cs`). This is the deferred `local-dev → Twitch-id` migration.
- **Row lookups now filter client-side.** `FindRow`/`TryGetRow`/`AllRowsForTenant` do `ListRows(Query.Limit(1000))`
  then `.Where(...)` in memory instead of server-side `Query.Equal(...)`. Reason (per their code comment): the
  user's Appwrite Cloud Tables endpoint **rejects the query-string filter form** in this environment. ⚠️ This
  supersedes the server-side-query approach described in the earlier "fix Appwrite row-addressing desync" entry —
  the unique-index lookup is the same idea, just done client-side. (Scales to ≤1000 rows; revisit if it grows.)
- **`Program.cs ResolvePort`** — picks the first free loopback port from the preferred one (fixes the
  port-in-use issue); headless mode logs the bound URL.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Admin UI cleanup pass 3 (UI.md complete)

Finished the Overview interactivity — **`UI.md` is now fully done** (All / Overview / Configure / Collections).
Verified live in-browser.

- **Inline pull-rate tuning on the Overview:** each Pull Rates row now has an editable weight input
  (reuses `.weight-input`), wired to `col.value.weight` + `markDirty` + a new `refreshOverviewRates()` that
  updates bars/percentages **in place** (no row rebuild, so focus is kept while typing). `.rate-row` grid got a
  weight column. Shows active collections only (0-weight rows drop out on full re-render, same as the chart).
- **Hide the System Check card:** a "Hide" button in the card header (`#hideSystemCheckButton`) persists to
  `localStorage["circuitos.hideSystemCheck"]`; a "Show System Check card" button (`#showSystemCheckButton`,
  below the dashboard) restores it. `applySystemCheckVisibility()` applies on load.

**Overview refinements (user feedback on pass 3):** the Pull Rates "bar" is now a **draggable range
slider** (`.rate-slider`, painted with a gradient fill up to the thumb), shared dynamic `sliderMax` with
headroom above the largest weight; row spacing increased. The six **stat cards are now clickable** (jump to
their section; listeners attached per-render since stats re-render). Fixed the **Hide button overflowing**
the narrow System Check card by letting `.panel-header` wrap (`flex-wrap`). **Follow-up (user):** removed the
redundant numeric weight box in **both** the Overview Pull Rates and the Rate Lab weight editor — the slider
bar is the sole control now (`buildWeightRow` + the overview row both render only `.rate-slider` + the %
label; `refreshWeightPercentages` no longer paints a separate mini-bar). Slim rectangular slider thumb
instead of the round one (user wasn't sold on the circle — may revisit). No way to type an exact weight now;
re-add a compact input if precision is needed.

**Verify-loop gotcha (note for next time):** the headless server **caches static files (index.html) at startup**,
so `index.html` edits need a **preview server restart** (stop+start), not just `location.reload()` — app.js/CSS
re-render on reload but the HTML structure won't update until restart.

**Next:** the **active-profiles UI (item C)** — surface the A+B backend (active toggles, live-vs-editing,
collision errors inline, per-profile overlay URLs, a "what's live" banner). Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Admin UI cleanup pass 2 (per UI.md)

Continued the `UI.md` backlog; all verified live in-browser via the headless + preview loop. Finishes
the **Collections** and **Configure** sections and adds Overview clickable cards.

- **Main Collections — delete + hidden IDs** (`buildCollectionCard` in app.js): permanent collections now
  have a **Delete** button (was event-only), guarded so you can't delete the last main collection; the
  collection **key chip + key edit field are hidden** (auto/stable, not user-facing); per-item **Component ID**
  field hidden too, widening Display name and leaving room for Tier. "Add Component" now generates a **unique**
  id (the id stays the inventory key; hiding it required this so adds can't collide). `.part-row`/`.part-row-tiered`
  columns updated.
- **Messages — less scrolling:** the template grid is now `auto-fit minmax(320px)` (3 columns on a normal
  window, was fixed 2), with tighter cards (padding + textarea min-height trimmed).
- **Overview — clickable cards:** the five dashboard panels (Pull Rates→Rate Lab, Collection Health→Main
  Collections, Event Timeline→Events, Economy Pulse→Economy, Viewer Activity→Inventory) are now whole-card
  clickable (`clickable-card` + `data-jump-view`, picked up by the existing startup jump handler) with a
  hover affordance. Inner jump buttons kept as explicit affordance.

**UI.md remaining (Overview only):** pull-rate weights tunable inline on the Overview (embed the Rate Lab
weight editor); a setting to hide the System Check card. Then the **active-profiles UI (item C)**.
Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Admin UI cleanup pass 1 (per UI.md)

**Goal:** Start the cross-app UI polish backlog the user added in `UI.md` (simpler wording, less
jargon, better Overview/Configure layout). All verified live in-browser.

**Live verify loop (reusable):** run the app **headless** (HTTP server, no WinForms window) against a
throwaway copy of `data/`, then drive it with the preview browser:
`dotnet tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll --headless --data <tmp> --ui tools/admin
--actions streamerbot-actions --overlay overlays/lower-quarter` → serves `127.0.0.1:8787`. The preview
tool needs `.claude/launch.json` at the **harness root** (the OneDrive path), config name `circuitos-admin`,
port 8787. `.claude/launch.json` is gitignored (machine-specific abs paths).

**Changes (all in `tools/admin/` — index.html, app.js, styles.css):**
- **Wording:** topbar `Import Catalog/Export Catalog/Refresh Live Data/Save Catalog` → `Import/Export/Refresh/Save`
  (the Save + Refresh labels are set dynamically in app.js — changed there too); `Save System Profile` → `Save Profile`;
  import-modal footers de-jargoned.
- **Bug fix:** the spurious red **"Message cannot be empty."** on every load — `validateMessageTemplate` ignored the
  `optional` flag, so the intentionally-blank `variantPull` message tripped it. Now respects `optional` (matches the
  server's `OptionalMessages`).
- **Overview reorg:** Action Center moved to the **top** (full-width "Needs Attention"); **Pull Rates + Collection
  Health** now side-by-side; **System Check** demoted to the lower dashboard row (`#systemCheckPanel` id added for a
  future hide-setting). All panel ids preserved so app.js wiring is intact.
- **"Main Collections":** the permanent-collections nav item + view title now read "Main Collections" (was the
  terminology-driven "Collections"/"Permanent Collections" — changed at app.js:293 and the getViewTitle special-case),
  removing the old "Collections › Collections" redundancy.
- **Configure → new "Appearance" page:** theme colors moved off Game Profile into `#appearanceView`
  (nav under Configure). The color grid (`#profileColors`) was relocated by id — its existing render/dirty/save wiring
  (saveSystemProfile) is unchanged; added `saveAppearanceButton` + dirty indicator. Game Profile live-preview lost the
  "Profile location" path (per UI.md) and "Admin name" → "Control panel nickname" (full Twitch-username wiring deferred
  to the Twitch phase).
- **Overlay editor:** was a single tall column (preview stacked above all settings → endless scroll). Now a balanced
  **2-column layout** (`.overlay-editor-col` flex columns, `min-width:0` so the 1920px iframe can't stretch a column):
  left = cropped preview + Browser Source + Position & Size + Colors; right = Timing + Content + Text — most of the
  editor fits without scrolling. The **preview is cropped to the bottom ~420px band** of the 1080 canvas
  (`scaleOverlayPreview`, `visibleBand` constant) so the lower-third fills the frame and is readable instead of a thin
  strip in a full 16:9 box. Collapses to 1-col under 980px. (Note: assumes a bottom-anchored overlay.)

**Decisions taken with the user:** theme colors → dedicated Appearance page; identity field relabel now / wire Twitch
later; one overlay source per profile (overlay UI still ahead).

**Still in UI.md (next):** Messages view scrolling; Overview clickable cards + inline-tunable rates + hide-System-Check
setting; Main Collections hide-ID + delete-collection. Then the **active-profiles UI (item C)** to surface the A+B backend.
Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — 0.7 multiple-active-profiles: foundation (A + B)

**Goal:** Start the user-requested shift from one-active-profile to **multiple simultaneously-active
profiles** (run two games at once; switch profiles only to *edit*). Decisions taken with the user:
explicit **active set** (separate from the editing selection); **hard-block** on command collisions;
**one overlay source per profile** (overlay UI deferred). This session built A (data model) + B (guard);
C (UI) and D (native live routing) are still ahead.

**A — active-set data model:** `active` flag added to each profile's meta = the live set; the existing
`active-profile` pointer keeps meaning the *editing* selection (`ActiveProfileId`). `ProfileInfo` now carries
`IsLive` alongside `IsActive`. New `IDataStore` members: `SetProfileActive(id, bool)` and `ReadProfileData(profileId, key)`
(cross-profile read, the counterpart of `ImportProfileData`). Implemented in both stores:
- `LocalFileDataStore`: meta `active` flag; new-profile default inactive; default/migrated/fresh profiles
  start live; **one-time `BackfillActiveFlags()`** stamps every profile explicitly on first run after upgrade
  (pre-feature installs → editing-current becomes the live one, matching old single-active behavior).
- `AppwriteDataStore`: same via the `__profile_meta__` row; `WriteProfileMeta` now preserves `active`/`createdAt`
  across rename; per-row fallback (live ⇐ `active` or `== editing profile`) for the dev bridge.

**B — command-collision guard:** new API ops `activate`/`deactivate` (via the existing `POST /api/profiles`).
Activation is **blocked** if the profile's command words collide with another *live* profile, and
`SaveSystemProfile` enforces the same when the edited profile is itself live (drafts save freely). Error reads
`"Command '!inventory' is already used by the active profile '<name>'. Rename it before saving."`
Lives in `CircuitService.Profiles.cs` (`CommandCollisions`, `IsProfileLive`), reading other live profiles'
commands via `ReadProfileData`.

**Verified:** runtime builds clean (Release, 0 warnings); smoke harness extended with
`TestActiveProfilesAndCollisions` — default live after first-run, new profiles inactive, activate/deactivate
flips `IsLive`, colliding-command activation blocked (profile stays inactive), unique-command activation
succeeds. New compile in `runtime.tests` csproj: added `CircuitService.Profiles.cs`. Backward-compatible,
**unreleased**, default-local untouched.

**Still ahead:** **C (UI)** — active toggle per profile, editing-vs-live distinction, inline collision errors,
per-profile overlay URLs, a "what's live" banner. **D (native)** — EventSub routes redemptions by reward-ID
and commands by word across the active set into the shared engines (Phase 4/5).

---

### 2026-06-24 — Claude (claude-opus-4-8) — 0.7 Phase 4 (step 1b): shared CommandEngine

**Goal:** Extend the shared-logic work to the chat commands (user asked: "shouldn't the commands be
shared too, not just the pull?"). They were right — only redemption was shared. Verified the generator
(`CircuitService.Core.cs:361,439-442`) emits exactly **4** actions, so the live command logic lives in
`StreamerbotCatalogCommands.txt` (inventory/missing/duplicates/balance/leaderboard), `StreamerbotCollection.txt`,
and `StreamerbotSalvage.txt`. `StreamerbotCheck/Missing/Dupes.txt` are **dead legacy** (hardcoded paths +
component IDs, not generated) — flagged for deletion via a background task, not ported.

**Built: `tools/runtime/CommandEngine.cs`** — ports those three actions to `System.Text.Json.Nodes` (the
actions hand-parse JSON to avoid Newtonsoft; the engine uses real parsing). Read commands return the chat
line(s) to send with the same ~440-char segmentation; salvage mutates inventory in place and reports
consumed/earned/balance + message. Methods: `Inventory`, `Missing`, `Duplicates`, `Balance`, `CollectionDetail`,
`Leaderboard`, `Salvage`. Configurable wording comes via a `CommandContext` (terminology + message templates)
the caller builds from the profile, so the engine is game-agnostic. Wallet currency stays under the fixed
`"scrap"` key (matches saved inventory); `CurrencyName` is display only. Legacy salvageValue fallbacks kept
for parity. One intentional improvement over the template: the leaderboard title uses `GameName` instead of
the hardcoded "Circuit Leaderboard".

**Verified:** runtime builds clean (Release, 0 warnings); smoke harness extended with `TestCommandEngine` —
inventory/missing/duplicates output, balance, collection detail (summary + owned/missing/dupes), leaderboard
ranking, and the salvage write (consumes one extra → +1 currency, balance 5→6, part reduced to 1) all pass.
New file `CommandEngine.cs`; test wired into `runtime.tests`.

**Shared-logic status:** the *whole* pull→apply→commands surface is now shared and tested
(`PullEngine` + `RedemptionEngine` + `CommandEngine`). The native EventSub path no longer has to
re-implement anything game-logic; it wires intake + chat-send to these. **Unreleased**, default-local untouched.
**Next (needs user/infra):** Helix reward create/update on login; Appwrite Function for EventSub redemptions
**and** a chat-message intake for commands; chat-send via Helix. Still pending: row-fix live verify.

---

### 2026-06-24 — Claude (claude-opus-4-8) — 0.7 Phase 4 (step 1): shared RedemptionEngine

**Goal:** Begin Phase 4 (native zero-config Twitch). Audit-first finding: the handoff framed
`PullEngine` as "built, just wire it in," but reading `StreamerbotReedeem.txt` end-to-end showed
`PullEngine.Roll` is only the **inner** roll (dup-protection → tier → variant) over an *already-chosen*
collection. Two pieces still lived ONLY in the Streamer.bot `.txt`: (1) collection selection (weighted
pick + featured-boost multipliers + event-window gating) and (2) the inventory read-modify-write
(owned counts, completion detection + seeding, pull-streak/triple, dup-protection counter). The native
EventSub path re-implementing those = the exact drift the shared engine was meant to prevent.

**Built (offline, no cloud/Twitch needed): `tools/runtime/RedemptionEngine.cs`** — ports those two
pieces from the `.txt` (Newtonsoft `JObject` → `System.Text.Json.Nodes`), wrapping `PullEngine.Roll`:
- `SelectCollection(collections, boost, now, rng)` → `CollectionSelection` (key, collection, displayName,
  probability, applied-boost name). Honors boost multipliers + event windows; boost label only applies if
  the *selected* collection had a multiplier. Throws `InvalidDataException` on bad config (mirrors the action).
- `ApplyRedemption(catalog, boost, inventory, viewerId, viewerName, now, rng, dupProtectionTurns=0)`
  → `RedemptionResult` (pull outcome, ownedAfter/total, quantity, isDuplicate, newlyCompleted,
  streak count + sequence probability, rareLabel). Mutates `inventory` in place.
- Output formatting (chat templates, overlay state) and cooldown intentionally stay caller-side — they
  differ per integration. Legacy Circuit-Components weight/rareLabel fallbacks kept byte-for-byte for parity.

**Verified:** runtime builds clean (Release, 0 warnings); smoke harness extended with `TestRedemptionEngine`
— collection weighting 89.9% vs 90 target, event gating in/out, and new/duplicate/completion/triple-streak
application all pass. New files: `RedemptionEngine.cs`; test wired into `runtime.tests` csproj + `Program.cs`.

**Unreleased**; default-local untouched. **Next (needs user/infra):** Phase 4 step 2 — Helix channel-point
reward create/update on login (cached token); step 3 — Appwrite Function behind the EventSub redemption
webhook calling `RedemptionEngine` + `AppwriteDataStore` (requires a publicly reachable endpoint, which is
really the Phase 5 hosting question). Still pending: the row-fix live verify (`--push-to-appwrite` → `--cloud`).

---

### 2026-06-24 — Claude (claude-opus-4-8) — 0.7: fix Appwrite row-addressing desync

**Goal:** Clear the flagged P0 — the re-push "verified 0" / "Catalog not found" desync — after auditing
the source against the handoff diagnosis.

**Audit confirmed the diagnosis and found two extra items:** (1) `--appwrite-profiles`/`--appwrite-backups`
hardcoded `"local-dev"` while `--cloud`/push used `ResolveTenant`, so post-login those diagnostics tested a
different tenant; (2) the fix's index query had to match the real constraint — user confirmed via console
screenshot that `unique_profile_key` covers `(userId, profileId, dataKey)`, all three columns.

**Changes:**
| File | Change |
|------|--------|
| `tools/runtime/AppwriteDataStore.cs` | `TryGetRow` → `ListRows` on the unique index, returns real `$id`. `UpsertJson` → `UpdateRow($id)` or `CreateRow(ID.Unique())`. `TryDelete`/round-trip delete resolve `$id` first. Removed `RowId(...)` and the crypto import. |
| `tools/runtime/Program.cs` | `--appwrite-profiles` (was line 574) and `--appwrite-backups` (was 633) now use `ResolveTenant(dataRoot)`. |

**Verification:** runtime builds clean (Release, 0 warnings); smoke harness passes (PullEngine distribution,
`AppwriteOptions`/`TwitchOptions` loaders, Streamer.bot generation). The cloud round-trip itself can't be
unit-tested (the smoke project doesn't link `AppwriteDataStore`, and live needs the user's credentials) —
**live re-verify is pending the user:** `--push-to-appwrite` (expect 6/6) → `--cloud`.

**Unreleased:** default-local preserved, no version bump, installed 0.6.0.8 untouched.

**Next:** user runs the live verify; then Phase 4 (EventSub + reward creation via `PullEngine`).

---

### 2026-06-24 — Claude (claude-opus-4-8) — 0.7 Phase 3: Twitch OAuth (verified live)

**Goal:** Replace the `local-dev` tenant with the real Twitch user id via OAuth. **Verified live** —
logged in as `shortcircuit_tv` (user id `103925885`); identity + tokens cached.

**Decision:** direct Twitch OAuth (no Auth0 in the desktop bridge — chosen by user). The streamer
registers their own Twitch app; client id/secret live in a local gitignored file. Auth0 deferred to
the hosted phase. The same Twitch app + scopes (`channel:read:redemptions`, `channel:manage:redemptions`)
feed Phase 4 (EventSub + reward management).

**Code added (compile-verified; OAuth flow verified live by the user):**

| File | Change |
|------|--------|
| `tools/runtime/TwitchOptions.cs` | NEW. Loads `twitch.local.json` (clientId/clientSecret/redirectUri, default `http://localhost:8765`); validation + secret-redacting `Describe()`. Unit-tested in smoke harness. |
| `tools/runtime/TwitchAuth.cs` | NEW. `TwitchAuth.Login` — desktop authorization-code flow: HttpListener loopback, browser launch, code→token exchange (`id.twitch.tv/oauth2/token`), identity from `helix/users`. `TwitchTokens` record save/load to `twitch-tokens.local.json` (gitignored, plaintext for dev — DPAPI is a hardening TODO). |
| `tools/runtime/Program.cs` | `--twitch-login` diagnostic; `ResolveTenant(dataRoot)` = cached Twitch user id ?? `local-dev`; push + `--cloud` now use `ResolveTenant`. |
| `.gitignore` | `twitch.local.json`, `twitch-tokens.local.json` |
| `docs/0.7-twitch-auth-setup.md` | NEW — Twitch app registration + config checklist. |

**Config (`twitch.local.json`, Data root, gitignored):** `{ clientId, clientSecret, redirectUri }`.
Redirect must match the Twitch-registered URL exactly (`http://localhost:8765`).

**Login indicator (UI):** `/api/health` now returns `mode` ("cloud"/"local") + `twitch`
({login, displayName, userId} | null), set from static session fields in `Program.cs`. The admin
panel renders a sidebar-footer badge via `renderSessionMode()`. Footer was decluttered (user request):
removed the raw `#dataPath` line; the badge now shows just "☁ @handle" (cloud, accent) or "Local data",
with full detail (identity + backend + data location) in the hover tooltip. (`#sessionMode` in
index.html, `.session-mode` in styles.css.)

**Session panel + safety (UI):** the footer badge is now a button that opens a session panel
(`renderSessionPanel` in app.js, `.session-panel` CSS) showing Twitch display name/login/user id,
token expiry (health.twitch now includes `expiresAt`), backend, and data location — plus a **Log out
of Twitch** button. Logout hits `POST /api/twitch/logout` (`Program.cs`, uses static `_dataRoot`) which
deletes `twitch-tokens.local.json` and clears `_sessionTwitch`. Privacy note states tokens are local-only.
**Login from UI too:** signed-out panel shows "Log in with Twitch" → `POST /api/twitch/login` runs the
interactive OAuth flow on the running app (blocks for consent on its own :8765 loopback), sets
`_sessionTwitch`, and the panel refreshes from health. So full login/logout is in-app (no CLI needed);
`--twitch-login` CLI still works. Note: logging in mid-session caches tokens; relaunch `--cloud` to re-key
the running store to the new tenant.

**Next:** user re-runs `--push-to-appwrite` (re-keys data from `local-dev` → Twitch id `103925885`)
then `--cloud` (reads under the Twitch id). Old `local-dev` rows are left orphaned in cloud (harmless;
optional cleanup later). Then Phase 4: EventSub function calling the shared `PullEngine`, using the
cached Twitch token for reward management + redemption subscriptions — the native zero-config path.

---

### 2026-06-23 — Claude (claude-opus-4-8) — 0.7 Phase 2b: cloud profile management (verified)

**Goal:** Start Phase 2b. Implemented tenant-scoped **profile management** in `AppwriteDataStore`
— the cleanest 2b piece since it needs no new Appwrite resource. **Verified live.**

**Design (within the existing `profile_data` table):** a profile "exists" once it has any row;
an optional `__profile_meta__` row (json = `{name, createdAt}`) carries its display name. All ops
scoped to `_userId`. `_profileId` is now mutable so `SwitchProfile` retargets the live instance.
Implemented `ListProfiles` (distinct profileIds from the tenant's rows, names from meta rows),
`CreateProfile`, `RenameProfile`, `SwitchProfile`, `DeleteProfile`, `ImportProfileData`. Added a
`RowId(profileId, key)` overload + `AllRowsForTenant()` (ListRows by `Query.Equal("userId", …)`).

**Verified:** `--appwrite-profiles` diagnostic (list → create → rename → delete, test profile
cleaned up) **passed live**; runtime + smoke build clean; local smoke tests pass.

**Cloud backups — DONE and verified.** Implemented a single rolling recovery point per managed
file (catalog/boost/roles/profile) WITHOUT a new table: snapshots live under a `#bak` profile
namespace inside `profile_data` (`BackupProfileId = _profileId + "#bak"`). `WriteAtomic` copies the
prior row there before upserting; `ListBackups`/`FindBackup`/`ReadBackupJson` read it; `ListProfiles`
skips `#`-namespaces; `DeleteProfile` also clears the `#bak` rows. Verified live via `--appwrite-backups`
(write v1 → overwrite v2 → prior captured → cleaned up). Full *timestamped history* (vs one rolling
point) is a later enhancement needing a dedicated table.

**Remaining Phase 2b — deferred:** Overlay-background **Storage bucket** (`SaveBackground`/`FindBackground`
→ Storage URL). Intentionally deferred to Phase 5: in the current desktop bridge the overlay is still
served from local files, so a cloud bucket only matters once the overlay itself is hosted.

**Phase 2b is functionally complete for the desktop-on-cloud bridge.** Next milestones: Phase 3
(Twitch OAuth via Auth0 → real tenant id replaces `local-dev`; account-gated), Phase 4 (EventSub
function calling the shared `PullEngine` — the zero-config native Twitch path), Phase 5 (hosted admin
panel + cloud overlay + the deferred Storage bucket).

---

### 2026-06-23 — Claude (claude-opus-4-8) — 0.7 MILESTONE: CircuitOS runs on Appwrite cloud data

**Goal:** Make the app actually read/write game data from Appwrite. **Achieved and verified live** —
the admin panel loaded the user's collections/profile/boost from the cloud and saves write back.
Proves the 0.5 thesis: swap the data layer, don't rewrite the app.

**Work this session (all default-local-preserving; no release; installed 0.6.0.8 untouched):**

1. **`AppwriteDataStore` core implemented** (`TablesDB` rows): one row per `(userId, profileId, dataKey)`,
   deterministic 32-hex row id (SHA-256 of the triple), JSON blob in the `json` column.
   `Exists`/`TryRead`/`ReadRequired`/`WriteAtomic`/`GetInfo` block on the async SDK. Verified by:
   - `--appwrite-roundtrip` (write→read→verify→delete) — **passed live**
   - `--push-to-appwrite` migration (local 6 files → cloud rows, read back) — **passed live** (6 rows)
2. **Host-agnostic `CircuitService`** — reverted the slice-1 `ILocalDataStore` typing back to `IDataStore`,
   capturing `_localStore = store as ILocalDataStore` for the few filesystem-bound spots
   (`DisplayDataPath`/`DisplayBackupPath` fall back to `appwrite://…` / "(cloud)" when null;
   Streamer.bot path injection and the overlay template degrade gracefully). Touched
   Core/Backups/Overlay/Profiles.
3. **Program.cs store selection** — `--cloud` flag picks `AppwriteDataStore` (tenant `local-dev`,
   profileId from the local store) vs `LocalFileDataStore`. The local store is ALWAYS created; the OBS
   overlay is still served from its local path (`overlayDataPath` threaded through `RunServerAsync`/
   `HandleRequestAsync`, replacing `service.Store.DataPath`).
4. **Phase-2b safe defaults** on `AppwriteDataStore` so the panel loads cleanly in cloud mode:
   `ListProfiles` → single synthetic profile, `ListBackups` → empty, `FindBackground` → null. Mutating
   actions (create/switch/delete profile, restore backup, save background) still throw clearly.

**Verified:** runtime + smoke harness build clean (0 warnings); smoke tests pass (local behavior intact);
headless local launch serves catalog as before; **`--cloud` launch loaded cloud data in the real UI**.

**Run cloud mode (from the build output, so pass --ui/--actions explicitly):**
`dotnet "tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll" --cloud --data "<DataRoot>" --ui "tools/admin" --actions "streamerbot-actions"`

**Known cloud-mode limits (Phase 2b):** backups view empty; profile create/switch/delete errors;
overlay still local-file; Streamer.bot path injection is a placeholder; each op is a network round-trip.

**Next:** Phase 2b (cloud backups, profile management as tenant rows, overlay-background Storage bucket),
then Phase 3 (Twitch OAuth via Auth0 → real tenant id replaces `local-dev`), then Phase 4 (EventSub
function calling the shared `PullEngine`). Eventually fold `--cloud` into config + cut a real release.

---

### 2026-06-23 — Claude (claude-opus-4-8) — 0.7 Phase 2 start: Appwrite config + verified live connection

**Goal:** Connect the runtime to the user's Appwrite Cloud backend. **Connection verified live.**

**Decisions/facts established:**
- **Appwrite Cloud** (region **nyc** → endpoint `https://nyc.cloud.appwrite.io/v1`). User can't run Docker, so Cloud is the dev backend.
- **Appwrite 1.8 uses TablesDB** (Tables/Rows/Columns = old Collections/Documents/Attributes). The SDK's `Databases.GetCollection` is deprecated; use `TablesDB` (`GetTable`/`GetRow`/`UpsertRow`/`ListRows`). The configured `collectionId` is the table id.
- The model-A table `profile_data` exists with 4 columns (userId/profileId/dataKey/json) + 1 unique index — confirmed by the live check.
- Appwrite .NET SDK is **v5.1.0**; `Client.SetEndpoint` (lowercase p), not `SetEndPoint`.

**Code added (all behavior-preserving for the local app; no release):**

| File | Change |
|------|--------|
| `tools/runtime/CircuitOS.Runtime.csproj` | Added `Appwrite` 5.1.0 package |
| `tools/runtime/AppwriteOptions.cs` | NEW. Config record + `TryLoad(dataRoot)`: reads `<dataRoot>/appwrite.local.json`, `CIRCUITOS_APPWRITE_*` env overrides, validation, key-redacting `Describe()`. Returns null → stay on local store. Unit-tested in the smoke harness. |
| `tools/runtime/Program.cs` | `--check-appwrite` diagnostic mode: loads config, `TablesDB.GetTable`, shows result in a dialog (or stdout when `--headless`). Registered `--check-appwrite` as a valueless flag in `RuntimeOptions.Parse` (it was eating the next arg). Error dialog now shows `ex.Type` + safe `Describe()`. |
| `tools/runtime.tests/*` | Added `AppwriteOptions` tests (file load, env override, env-only, defaults, validation, key redaction). |

**Config format (`appwrite.local.json`, gitignored, in the Data root):**
`{ endpoint, projectId, apiKey, databaseId, collectionId }`. Template committed as
`appwrite.local.example.json`. The user keeps the secret; assistant never reads it.

**Debugging the live connection (good error-type breadcrumbs):**
403 `general_resource_blocked` (projectId left as the `your-project-id` placeholder) →
404 `project_not_found` (used the project *name* `circuitos-dev`, not its ID) →
**200 connected** once the real project ID (`6a3b1af3002de5ef906b`) was set. The key (265 chars)
and scopes were fine throughout.

**How to run the check:**
`dotnet "tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll" --check-appwrite --data "<DataRoot>"`

**Next:** Implement `AppwriteDataStore` for real against `profile_data` using `TablesDB`
`GetRow`/`UpsertRow`/`ListRows` (one row per userId+profileId+dataKey, JSON blob in `json`),
blocking on the async SDK for the sync `IDataStore` (parallel-desktop scenario). Then a
round-trip parity test the USER runs (it writes a test row to their cloud — assistant won't
use their key). Then wire runtime store selection by config.

---

### 2026-06-23 — Claude (claude-opus-4-8) — 0.7: shared PullEngine + revived smoke harness

**Goal:** Keep moving 0.7 forward. Judged the remaining data-layer slices (async,
tenancy scoping) as *speculative until a cloud consumer exists* — refactoring the
interface further in a vacuum risks the wrong abstraction. Pivoted to the concrete,
non-speculative, #1-priority groundwork: extracting the shared pull logic. No version
bump / no package (groundwork + test infra; installed 0.6.0.8 EXE untouched).

**Did:**

| File | Change |
|------|--------|
| `tools/runtime/PullEngine.cs` | NEW. The single source of truth for item selection — dup protection → tier-weighted pick → variant roll — ported faithfully from `StreamerbotReedeem.txt`. Pure over the catalog JSON (`System.Text.Json.Nodes`); RNG injected for determinism. Returns `PullOutcome(PartId, PartName, DisplayPartName, VariantLabels, TierLabel, Probability)`. Not wired to a live path yet (the native EventSub function that calls it is Phase 4); it's the reference impl all integration paths will share. |
| `tools/runtime.tests/*` | REVIVED. The smoke harness had been broken since 0.5 — `Program.cs` called `new CircuitService(testPath, ...)` (string) but the constructor takes a store since the IDataStore refactor, and the csproj never included `IDataStore.cs`. Fixed: construct via `LocalFileDataStore`, account for the 0.5 `profiles/default/` layout in the path assertions + inventory-hash timing, and added the missing source files to the csproj. Added a `PullEngine` distribution test. |

**Why PullEngine over more data-layer refactoring:** the native Twitch path (the user's
explicit #1 — zero-config, no Streamer.bot code) needs this roll logic as real callable
.NET code, and it's concrete (porting, not inventing) and independently testable. The
async/tenancy interface changes are better driven by the actual AppwriteDataStore later.

**Minor intentional difference from the Streamer.bot action:** PullEngine uses ONE
injected RNG for both tier and variant rolls (the action used a second `Random` for
variants). Draws stay independent so the distribution is identical; one seed just makes
tests reproducible.

**Verified — `dotnet run` of the smoke harness against the repo `data/`:**
- PullEngine tier distribution **69.9 / 25.1 / 5.0%** vs the 70/25/5 weights ✓
- SHINY variant **25.0%** vs its 0.25 chance ✓
- Dup protection (only-unowned item always picked) + equal-odds fallback ✓
- First-run safety (inventory hash unchanged, profile written last, backups created) ✓
- Generated Streamer.bot C# structurally valid (4 actions, balanced braces) ✓
- Main runtime build clean (0 warnings) with the new file.

**Note for next session:** the smoke harness (`dotnet run --project tools/runtime.tests --
<data-path> <actions-path>`) works again — use it to guard future refactors, especially
the eventual async conversion.

**Also wrote `docs/0.7-appwrite-dev-setup.md`** — sets up the model-A `profile_data` collection
(attrs `userId`/`profileId`/`dataKey`/`json` + unique index on the three keys). **Decision: use
Appwrite Cloud, not self-hosted Docker** — the user's machine can't run Docker/virtualization,
and Cloud matches the hosted 0.7 end state. Doc is Cloud-first (sign up at cloud.appwrite.io,
endpoint `https://cloud.appwrite.io/v1`); Docker kept only as an optional appendix. Phase 2
(write the real `AppwriteDataStore`) is unblocked once the user creates the Cloud project +
collection and shares endpoint + project id (API key stays local, never in source).

---

### 2026-06-23 — Claude (claude-opus-4-8) — 0.7 Phase 1, slice 1 (DataPath/BackupPath split)

**Goal:** Start the `IDataStore` seam refactor that 0.7 sits on. Done as small,
behavior-preserving, build-and-run-verified slices (the user actively streams with this
tool, so no big risky surgery). No version bump / no package — internal groundwork,
behavior unchanged, installed 0.6.0.8 EXE untouched.

**Slice 1 — moved the clearest filesystem leak off the portable contract:**

| File | Change |
|------|--------|
| `tools/runtime/IDataStore.cs` | Removed `DataPath`/`BackupPath` from `IDataStore` (now the portable contract). Added `ILocalDataStore : IDataStore` carrying `DataPath`/`BackupPath` — implemented only by the local store. |
| `tools/runtime/LocalFileDataStore.cs` | Now implements `ILocalDataStore`. |
| `tools/runtime/CircuitService.Core.cs` | `_store`, ctor param, and `Store` property retyped `IDataStore` → `ILocalDataStore` (this service is the local host's service; it injects `DataPath` into generated Streamer.bot actions and serves the local overlay). No logic changes. |
| `tools/runtime/AppwriteDataStore.cs` | Dropped the `DataPath`/`BackupPath` stubs — the cloud store no longer has to fake filesystem paths (that's the point of the split). Header note updated. |

**Why this typing:** `CircuitService` stays the LOCAL host's service for now, so typing it to
`ILocalDataStore` keeps every `DataPath`/`BackupPath` use compiling with zero logic change. The
portable `IDataStore` is now fully cloud-implementable. Making `CircuitService` itself
host-agnostic (so the cloud function can reuse it) is a later slice — the Streamer.bot-injection
and overlay-serving bits that need `DataPath` are local-host concerns to extract then.

**Verified:** `dotnet build` clean (0 warnings). Ran the freshly-built DLL headless against a
throwaway copy of `data/` on a test port: `/api/health` → `ok:true` with `dataPath` correctly
resolved through the 0.5 migration to `profiles/default`; `/api/config` → full catalog loads
(basic/power collections + parts). Behavior identical to 0.6. Temp data + processes cleaned up.

**Remaining Phase 1 slices (still local, no accounts):**
1. `FindBackground` returns a local path → evolve toward a Storage URL/ref (consumed by overlay
   serving; signature change ripples to `Program.cs`).
2. Reshape profile management for `(userId, profileId)` tenant scoping + ownership.
3. Convert the data ops to async (`Task<...>`) and ripple through `CircuitService.*` callers —
   the biggest slice; do it deliberately and keep `LocalFileDataStore` green.
Then Phase 2: stand up Appwrite + flesh out `AppwriteDataStore` (model A: document-per-key).

---

### 2026-06-23 — Claude (claude-opus-4-8) — 0.7 groundwork (design, no release)

**Goal:** Begin laying groundwork for the 0.7 Cloud Platform + Twitch milestone.
Design only — no version bump, no package.

**Did:** Read the *real* data-access seam (`IDataStore.cs` + `LocalFileDataStore.cs`)
rather than the HANDOFF's idealized sketch, wrote
[`docs/0.7-cloud-foundation.md`](docs/0.7-cloud-foundation.md) — the milestone's
architectural starting point — and added a compiling scaffold
`tools/runtime/AppwriteDataStore.cs` (every member throws `NotReady()` with its
intended Appwrite mapping in a comment; not wired up; no Appwrite SDK dependency;
`dotnet build` green). **Decision: 0.7 keeps BOTH stores in parallel** — local app
stays on `LocalFileDataStore`, cloud uses `AppwriteDataStore`, chosen at runtime.

**Key finding — the `IDataStore` seam is NOT cloud-ready as-is.** Its members fall in
three buckets:
1. **Cloud-portable** — the core data ops (`TryRead`/`WriteAtomic`/backups/`SaveBackground`)
   map cleanly onto Appwrite.
2. **Filesystem-leaky** — `DataPath`/`BackupPath` expose raw paths, consumed for Streamer.bot
   path-injection (`Core.cs`) and local overlay serving (`Program.cs`); `SaveBackground`/
   `FindBackground` return file paths. None of these survive the cloud move — they should
   move OUT of `IDataStore` into a separate integration/overlay-URL seam.
3. **Multi-tenancy-reshaped** — the profile-switch methods assume one local operator toggling
   a global active profile; in the cloud the authenticated Twitch user is the tenant and every
   op must be scoped by `(userId, profileId)` with ownership enforcement.
   Plus: the interface is synchronous; a hosted backend wants async (`Task<...>`), which ripples
   through all `CircuitService.*` callers.

**Recommended Appwrite model:** start with "document-per-key" (one `profile_data` collection,
JSON blob per `(userId, profileId, dataKey)`) to honor "swap not rewrite," then evolve the
hot path (`inventory`) toward modeled collections for server-side writes/leaderboards.

**Reference for the EventSub port:** the pull/roll/tier/variant logic in
`StreamerbotReedeem.txt` is already pure logic over the catalog JSON, so it ports into an
Appwrite Function behind the Twitch webhook without the file I/O.

**Next coding step (Phase 1, fully local, no accounts needed):** refactor the seam — split the
filesystem-leaky + profile-management responsibilities out of `IDataStore`, make the data ops
async, keep `LocalFileDataStore` green so 0.6 behavior is unchanged. This is the make-or-break
step; everything cloud sits on it.

**Open decisions needing the user** (captured in the doc): Appwrite self-hosted vs Cloud;
creating Appwrite/Auth0/Twitch-dev accounts; data model A-vs-B per key; first-login migration;
whether the local Windows app stays supported in parallel or 0.7 is a hard cutover.

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
