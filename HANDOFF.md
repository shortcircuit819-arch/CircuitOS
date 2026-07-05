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
| Current version | **0.7.3.1** (shipped — native Twitch is the single supported path; Streamer.bot retired in 0.7.2. Collection packs + import de-dupe in 0.7.3; share-all-collections in 0.7.3.1. Optional cloud, per-state overlay images, backup retention). Local mode is the default and unchanged. |
| Phase | **0.7 — Native Twitch + Cloud Foundation — shipped (0.7.2 retired Streamer.bot).** Zero-config Twitch login (device flow, no dev account), CircuitOS-managed channel-point reward, native EventSub redemptions + chat commands + pull announcements. Settings page with an optional cloud data backend (bring-your-own Appwrite, safe fallback to local). Multiple live profiles, per-state overlay colors, shared PullEngine/RedemptionEngine/CommandEngine (smoke-tested), reliability/security hardening. Still ahead: Velopack + GitHub installer/updater (gated on creating the repo — `docs/updater-velopack-plan.md`), and a true *hosted* cloud (security/infra decision — `docs/feature-requests-analysis.md`). Deferred features: bot chat account, cross-profile currency (shops/2.0), per-state overlay images. |
| Repo root | `C:\Dev\CircuitStreamSystem` |
| Live data path | `C:\Users\nicho\Documents\CircuitOS\Data` (profiles under `Data\profiles\<id>`; active profile `circuit-components`) |

---

## Architecture at a Glance

```
CircuitStreamSystem/
├── tools/runtime/          .NET 9 Windows Forms app (HTTP server + WebView2 UI)
│   ├── Program.cs          HttpListener on 127.0.0.1:8787, request routing
│   ├── CircuitService.Core.cs   Config, validation, backup, overlay state
│   ├── CircuitService.AnalyticsRoles.cs
│   ├── CircuitService.Backups.cs
│   ├── CircuitService.Overlay.cs
│   ├── CircuitService.Profiles.cs
│   ├── CircuitService.Modules.cs
│   ├── IDataStore.cs, LocalFileDataStore.cs, AppwriteDataStore.cs
│   ├── PullEngine.cs, RedemptionEngine.cs, CommandEngine.cs
│   ├── TwitchAuth.cs, TwitchHelix.cs, TwitchEventSub.cs, TwitchRuntime.cs
│   ├── CircuitWindow.cs    Windows Forms shell (WebView2)
│   └── CircuitOS.Runtime.csproj
│
├── tools/admin/            Browser frontend (vanilla JS, no framework)
│   ├── index.html          UI shell: first-run wizard, editor, analytics
│   ├── app.js              ~3,800 lines — all rendering, API calls, state
│   ├── styles.css
│   └── runtime/CircuitOS.exe   Published binary (copy here after dotnet publish)
│
├── data/                   Starter/dev JSON data (not the live data folder)
├── docs/                   User and maintainer documentation
│   └── patch-notes/        Discord-ready release notes (one file per version)
├── dist/                   Built release packages
├── AGENTS.md               Original (outdated) agent instructions
└── HANDOFF.md              ← this file
```

**Key API endpoints (all local, 127.0.0.1:8787):**
- `GET /api/health` → version string, data path, mode, Twitch session info
- `GET /api/config` → components catalog + boost config
- `GET /api/profile` → branding, commands, colors, messages
- `GET /api/analytics` → inventory stats
- `GET /api/backups` → backup history
- `GET /api/profiles` → profile list, editing profile, live profile set
- `GET /api/overlay-config` → overlay config JSON (falls back to template)
- `POST /api/twitch/login` / `POST /api/twitch/logout` → desktop Twitch OAuth session controls
- `GET /api/twitch/rewards` → list current Twitch channel-point rewards for attach/reuse
- `POST /api/twitch/reward-sync` → create/update or attach and persist a live profile channel-point reward
- `POST /api/twitch/reward-delete` → delete a synced CircuitOS-managed reward and clear profile mapping
- `POST /api/twitch/reward-update` → update managed reward title/cost and sync the profile redemption name
- `POST /api/save` → save config changes
- `POST /api/overlay-config` → save overlay config
- `POST /api/profiles` → create/switch/rename/delete/activate/deactivate profiles
- `POST /api/runtime/action` → native runtime dispatch for redeem/command actions
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

All four must match when cutting a release:

| File | Location | Field |
|------|----------|-------|
| `tools/runtime/CircuitOS.Runtime.csproj` | `<Version>`, `<FileVersion>`, `<AssemblyVersion>` | Assembly metadata |
| `tools/runtime/Program.cs` | `/api/health` response | Runtime version shown in UI footer |
| `tools/runtime/CircuitService.Modules.cs` | `circuitosVersion` in module manifest | Version stamped into exported `.circuitmodule` files |
| `README.md` | "Current application version" line | Documentation |

(0.7.2 removed the fifth location — `integrationVersion` in `CircuitService.Core.cs` — along with the
Streamer.bot integration it versioned.)

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
- `dist/CircuitOS-Update-{version}.zip` — data-free update package

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
| Streamer.bot C# actions | Native Twitch EventSub (desktop bridge uses WebSocket; Streamer.bot becomes optional) |
| Admin panel via localhost | Admin panel via browser, any device |
| OBS overlay at `localhost:8787` | OBS overlay at cloud URL |
| No auth | Twitch OAuth; desktop bridge currently uses direct Twitch OAuth |
| Local backups | Cloud-managed, per-streamer |

### Chosen cloud stack / auth direction

- **Appwrite** — backend (database, file storage, functions). Open source, self-hostable
  during development. MCP plugin already installed with 13 skills.
- **Direct Twitch OAuth for the desktop bridge** — already implemented with a loopback redirect and
  cached local tokens. Auth0 may be revisited for hosted multi-user deployment, but it is not the
  current desktop bridge path.
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
| Released version | **0.7.1** (native Twitch, zero-config login, optional cloud, per-state overlay images, backup retention, full new-user UX pass). Tags `v0.7.0.1`, `v0.7.0.2`, `v0.7.1`. |
| In development | **0.7 continued** — next up: Velopack + GitHub installer/updater (gated on the repo). Hosted cloud + deferred features tracked in `docs/`. Local mode remains the default. |
| Active profile data path | `C:\Users\nicho\Documents\CircuitOS\Data\profiles\circuit-components` |
| Data root | `C:\Users\nicho\Documents\CircuitOS\Data` (holds `appwrite.local.json`, `twitch.local.json`, `twitch-tokens.local.json`) |

### 0.7 development status (read first for a cold start)

**Current focus:** work from the older `UI.md` list was completed, then the user manually updated
`UI.md` with a new 0.7 launch punch list. Treat `UI.md` as active product direction now: sidebar
theme coverage, Overview layout/rate editing, Game Profile save flow, overlay state customization,
commands layout, clearer Backups UX, and Twitch settings/login treatment.

**Verification status (2026-06-27):** local Release build passes with 0 warnings/errors, and the
smoke harness passes against `data` + `streamerbot-actions`. This verifies first-run safety, generated
Streamer.bot structure, active profile collision guards, runtime dispatch, pull/redemption/command
engines, and Appwrite/Twitch config loader behavior. Live cloud/Twitch checks still require the user's
credentials/session.

**What's built and verified live (all behind `--cloud`; local mode 100% unchanged):**
- Data layer swapped to Appwrite: app reads/writes catalog/profile/boost/inventory from `profile_data`
- Profile management + a rolling backup recovery point (both inside `profile_data`)
- Twitch OAuth login/logout in-app (footer session panel) + CLI `--twitch-login`; tenant = Twitch user id
- Native Twitch desktop bridge slices: Helix reward creation, EventSub WebSocket redemption intake,
  fulfillment/cancel path, chat announcements, chat commands, and listener auto-start
- Shared `PullEngine`, `RedemptionEngine`, and `CommandEngine`; revived smoke harness

**Run cloud mode (from the build output — pass --ui/--actions since there's no `App` folder there):**
```
dotnet "tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll" --cloud \
  --data "<DataRoot>" --ui "tools/admin" --actions "streamerbot-actions"
```
**Diagnostics (most open a dialog unless headless):** `--check-appwrite`, `--appwrite-roundtrip`,
`--push-to-appwrite`, `--appwrite-profiles`, `--appwrite-backups`, `--twitch-login`,
`--twitch-reward`, `--twitch-listen`. Local app: drop `--cloud`.

**New 0.7 source files:** `tools/runtime/{IDataStore.cs (ILocalDataStore split), LocalFileDataStore.cs,
AppwriteDataStore.cs, AppwriteOptions.cs, PullEngine.cs, RedemptionEngine.cs, CommandEngine.cs,
TwitchOptions.cs, TwitchAuth.cs, TwitchHelix.cs, TwitchEventSub.cs, TwitchRuntime.cs}`,
`tools/runtime/CircuitOS.Runtime.csproj` (+Appwrite 5.1.0). `CircuitService` now takes `IDataStore`.

**Config files (gitignored, in Data root; user holds the secrets — assistant must NOT read them):**
`appwrite.local.json` {endpoint, projectId, apiKey, databaseId, collectionId};
`twitch.local.json` {clientId, clientSecret, redirectUri=http://localhost:8765}; `twitch-tokens.local.json` (cached).
Appwrite: nyc region, project `6a3b1af3002de5ef906b`, db `6a3b1b19000359f605af`, table `profile_data`
(cols userId/profileId/dataKey/json + unique index). Twitch user: `shortcircuit_tv` (id `103925885`).

**Setup docs:** `docs/0.7-cloud-foundation.md`, `0.7-appwrite-dev-setup.md`, `0.7-twitch-auth-setup.md`.

**Dev UI Bench — RETIRED 2026-07-01.** The proposal-only `tools/dev-ui-bench/` visual editor (built
2026-06-27) was removed: the direct-edit + live-preview workflow superseded it and it had drifted from
the current UI. The three 06-27 build entries remain in the session log below as history; the tool is gone.

**Remaining 0.7:** live-verify cloud/Twitch with the user's credentials as needed; complete the active
`UI.md` launch punch list; add Twitch settings/status UX; persist reward-id ↔ profile mapping; decide
hosted auth/deployment shape; add cloud overlay/background storage; fold `--cloud` into config; cut the
0.7 release.

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

### 2026-07-04 — Claude (claude-opus-4-8) — Import validation + collection-packs guide (no version bump)

- **Validate catalogs on import.** `ImportModule` and `ImportCollectionPack` now run the imported catalog
  through `ValidateConfiguration` (with the module's boost, or a default) *before* creating the profile,
  so a corrupt or hand-edited `.circuitmodule` / `.circuitcollection` is rejected with clear errors and
  leaves nothing behind. A genuine CircuitOS export already passed this validator on save, so legit files
  round-trip unchanged. `TestCollectionPacks` gains a negative case (invalid pack → rejected, no profile).
- **Collection-packs user guide.** Added `docs/collection-packs.md` (pack vs module, share one / share
  all, what travels vs stays yours, events never share, import + de-dupe) and shipped it in the packaged
  Documentation/ via `Build-CircuitOSPackage.ps1` guideFiles.

No version bump (batched under 0.7.3.1); no UI change.

### 2026-07-04 — Claude (claude-opus-4-8) — Profile-meta safety net (no version bump)

Robustness fix for the "my profiles vanished" class of scare. `LocalFileDataStore.ListProfiles` used to
silently skip any profile folder whose `profile-meta.json` was missing or unparseable — so a profile with
intact catalog/inventory could disappear from the UI. Now if the meta can't be read but the folder holds
real profile data (`components.json` / `system-profile.json` / `inventory.json`, via new
`LooksLikeProfile`), the profile is still listed under its folder name (id = name = folder) so the user
can see / switch to / rename it — and renaming rewrites a clean meta. Empty folders stay ignored. New
smoke test `TestProfileMetaSafetyNet` covers missing meta, corrupt meta, and the empty-folder negative
case. No version bump (batched under 0.7.3.1); no UI change.

### 2026-07-04 — Claude (claude-opus-4-8) — Polish & cleanup (no version bump)

Housekeeping batched under 0.7.3.1 (no version change):
- **Retired the legacy PowerShell admin.** Deleted `tools/admin/CircuitAdmin.ps1` and its two broken
  `.cmd` wrappers (`tools/start-circuitos.cmd`, `tools/start-admin-portable.cmd` — they invoked
  `%~dp0CircuitAdmin.ps1` = `tools/CircuitAdmin.ps1`, which never existed). The .NET runtime fully
  supersedes it. De-referenced it in `dotnet-runtime.md`, `configuration-editor.md`, and
  `installation-and-updates.md`, and rewrote `maintainer-quick-fixes.md` → "Run the UI Locally" to
  launch the .NET app headless (`dotnet run … --headless --data … --ui tools\admin --port 8810`).
- **Fixed the Pull Rates slider fill drift** (Known Bugs). First tried correcting the fill-gradient
  calc for the thumb's border-box width — that still drifted (predicting Chromium's native range-thumb
  geometry is unreliable; commit 674dfeb). Real fix (70a9162): replaced the gradient with the thumb's
  own **box-shadow** clipped by `overflow:hidden`, so the fill is tied to the handle by construction and
  can't drift at any position. Verified visually in preview at 60% and max — the handle sits exactly at
  the fill's leading edge. Known Bugs open list is now empty.
- **UX.md:** reviewed — every Tier 1–3 item is done; the one `[~]` (ALL-CAPS kickers) is a deliberate
  keep. No changes needed.

No C# changed (no rebuild/republish needed).

### 2026-07-04 — Claude (claude-opus-4-8) — Share-all collection packs; cut 0.7.3.1

Iteration on 0.7.3 collection packs: share the WHOLE set of permanent collections as one pack.
- `CircuitService.Modules.cs`: `ExportCollectionPack` now takes a specific key OR `""`/`"*"` = all
  permanent collections; the pack carries a `collections{}` map (one or many) instead of a single
  `collection`. Event collections never travel — share-all skips `type==event`, single-share of an
  event errors. `ImportCollectionPack` reads the `collections{}` map (backward-compatible with the
  first single-collection shape). Manifest gains `collectionCount`.
- UI: **Share All** button in the Collections toolbar (`shareCollection("*")`); the per-collection
  Share button now only renders on permanent collections; import prompt says "N collections" for
  multi-collection packs.
- Test: `TestCollectionPacks` seeds 2 permanent + 1 event collection and asserts share-all bundles both
  permanent collections, excludes the event, and the round-trip carries them; single share still = 1.

**Version → 0.7.3.1** (four-part iteration on the collection-packs sub-feature).
`docs/patch-notes/v0.7.3.1.md` added.

**Validation (all green):** runtime + tests build 0/0; smoke tests pass incl. multi-collection
share-all + event exclusion; admin UI verified in preview — Share All button present, export/import
round-trip, single Share only on permanent cards, no console errors.

### 2026-07-04 — Claude (claude-opus-4-8) — Collection packs + import de-dupe; cut 0.7.3

**Collection packs (`.circuitcollection`)** — share ONE collection as a themeable mini-game.
- `CircuitService.Modules.cs`: `ExportCollectionPack(collectionKey)` bundles one collection + the
  profile's flavor (terminology/commands/messages/tuning) but strips `colors`/`brandKicker`/`adminName`.
  `ImportCollectionPack(pack, name?)` creates a NEW profile from the pack that adopts the importer's
  own colors/brand + overlay config; single-collection catalog; deduped, editable name.
- `POST /api/collection-pack/export` (body `{collectionKey}`) and `/import` (body `{pack, name}`) in Program.cs.
- UI: per-collection **Share** button in `buildCollectionCard` → downloads `.circuitcollection`.
  `importModule` now auto-detects `manifest.format === "circuitcollection"` and routes to
  `importCollectionPack` with an editable-name `prompt` (pre-filled with the sharer's game name). Import
  file input accepts `.circuitcollection`; button relabelled "Import Module / Pack".

**Import name de-dupe** — `UniqueProfileName` (Profiles.cs) appends " (2)", " (3)" … on name collision;
wired into both `ImportModule` and `ImportCollectionPack`. Kills the duplicate-twin footgun.

**Delete hint** — the active profile card shows *"Switch to another profile to delete this one"* (it
intentionally has no Delete button). Tiny `.pc-delete-hint` style added.

**README fix** — stripped a corrupted UTF-16 tail (13 NUL bytes) that had been committed with 0.7.2
(a stray `Out-File`-style append from an earlier session).

**Version → 0.7.3** in all four locations (csproj, `/api/health`, `Modules.cs` `circuitosVersion`,
README). New smoke test `TestCollectionPacks` — and note the tests csproj now also links
`CircuitService.Modules.cs` (it didn't before, which is why the pack methods weren't visible at first).
`docs/patch-notes/v0.7.3.md` added.

**Validation (all green):** runtime + tests build 0/0; smoke tests pass incl. the new pack round-trip +
de-dupe; admin UI verified live in preview — export strips theme, import builds a themed
single-collection profile ("Basic Pack"), duplicate import de-dupes to "Basic Pack (2)", Share button +
delete hint render, no console errors.

**Design decisions (for reference):** terminology travels with the pack (it's the collection's flavor);
only the theme (colors/brand/overlay) is the importer's. A pack is a new profile, not a merge. Event
collections currently carry as-is (importer can edit). Online pack *discovery/marketplace* remains
cloud-gated (roadmap 1.6+); file-based sharing is the near-term piece that shipped here.

### 2026-07-02 — Claude (claude-opus-4-8) — Retired Streamer.bot; cut 0.7.2

**Full removal of the Streamer.bot integration** (user: "nobody uses it" — both testers are on the
native Twitch login). Native Twitch has handled redemptions, chat commands, reward management, and the
overlay since 0.7.0, so the SB path was pure maintenance tax (a second copy of the pull/variant/tier
logic) and muddied the "no code to paste" story.

**Removed:**
- `streamerbot-actions/` (all templates) and `tools/package/package-files/STREAMERBOT ACTIONS.txt`.
- `GetStreamerBotSetup()` + `GenerateActionSource()` + `EscapeCSharp()` + `integrationVersion` from
  `CircuitService.Core.cs`; the `/api/setup` endpoint and `--actions`/action-folder resolution from
  `Program.cs` (the `CircuitService` ctor no longer takes `actionPath`; `RuntimeOptions.ActionPath` gone).
- Admin UI (`app.js` + `index.html`): the Streamer.bot Setup `<section>`, the nav button,
  `generateStreamerBotSetup` / `renderStreamerBotSetup` / `copyGeneratedCode`, `setupBundle`, all call
  sites, the first-run "Prefer Streamer.bot?" notices, and the now-dead `.setup-*` CSS in `styles.css`.
- Smoke tests: the generated-C# assertions + the orphaned `GetBraceDepth` helper; the test now takes
  one arg (`<source-data-path>`), no action path.
- Installer: `Build-CircuitOSPackage.ps1` no longer bundles the Streamerbot Actions folder or the
  `streamerbotIntegrationVersion` manifest field, and the redeem-C# release validation is dropped.
  `START HERE.txt` / `UPDATE README.txt` / `OBS SETUP.txt` rewritten to native Twitch. `--actions`
  dropped from `start-circuitos.vbs` and `start-admin.vbs`.

**Reframed, not removed:** the shared engines are the native implementation now — PullEngine /
RedemptionEngine / CommandEngine / IDataStore / Core.cs header comments no longer describe themselves as
"ported from / mirroring the Streamer.bot action."

**Docs:** split `distribution-and-streamerbot-setup.md` → `installation-and-updates.md` (SB half
dropped, added a native "Go Live On Twitch" section; README link updated). De-SB'd README
(intro/features/versioning/roadmap/important-files), the command/feature guides (catalog-commands,
collection-command, leaderboard, salvage, featured-stream-boosts, obs-lower-quarter), configuration-editor,
versioning, dotnet-runtime, maintainer-quick-fixes, AGENTS.md, and the open Known Bug. **Left as
historical:** all `docs/patch-notes/*`, `HANDOFF-archive.md`, `UX.md` punch-list, `0.7-cloud-foundation.md`
(design-era doc), and the 0.6 roadmap line — rewriting those would falsify the record.

**Deliberately left (flagged for a follow-up):** `tools/admin/CircuitAdmin.ps1` — the legacy PowerShell
emergency fallback (not shipped, not built; launched only by the `.cmd` wrappers) still carries its own
copy of the SB generator. Frozen dead code; candidate for a separate "retire the legacy PS admin" task.

**Version → 0.7.2** in the (now **four**, not five) locations: `csproj`, `Program.cs` `/api/health`,
`CircuitService.Modules.cs` `circuitosVersion`, `README.md`. `integrationVersion` in Core.cs is gone
with the SB feature it versioned. `docs/patch-notes/v0.7.2.md` added.

**Validation (all green):** `dotnet build` of the runtime + smoke-tests projects (0 warnings, 0 errors);
smoke tests pass (`-- data`, single arg) covering first-run, active-profile collisions, Twitch reward
persistence, runtime dispatch, pull/redemption/command engines, Appwrite/Twitch loaders, backup
retention; admin UI loaded headless in preview — no console errors, `app.js` executes, version footer
reads **0.7.2**, no Streamer.bot nav/section, "Twitch" nav present. **No git commit/tag yet** (awaiting user).

### 2026-07-01 — Claude (claude-opus-4-8) — Roadmap re-worked (README, no code)

Workshopped the forward roadmap with the user and rewrote the README 0.8→2.0 section. Key changes:
- **0.8 = Design & Identity** (design-token layer + circuit-tech re-skin + Design Mode). Design-first
  was the user's explicit call.
- **0.9 = Distribution & Release Candidate** (Velopack installer + auto-updater + signing, folded into
  RC hardening). Old "0.8 = MixItUp" dropped — native Twitch made more-bot-support non-core.
- **1.0 = Signed Stable Release.**
- **1.x band:** 1.1 hosted cloud → 1.2 analytics (standalone) → 1.3 achievements (standalone) → 1.4
  online viewer profiles → 1.5 Twitch Extension (panel + video overlay) → 1.6 module sharing → 1.7
  module marketplace → 1.8 viewer trading/gifting → 1.9 shops readiness.
- **Key insight captured:** the Twitch Extension REQUIRES hosted cloud (viewers can't reach a local PC),
  so 1.1 hosted cloud + 1.5 extension are a linked "CircuitOS on Twitch" pair. Hosted-cloud cost/process
  discussed (self-host VPS ~€5/mo or Appwrite Cloud ~$15-50/mo; real cost is ops + the auth rework —
  Twitch-login → per-user session + row permissions, off the master key).
- Fixed the 0.7 section to say shipped 0.7.1 (was 0.7.0.2).

### 2026-07-01 — Claude (claude-opus-4-8) — Per-state overlay images + cut 0.7.1

**Per-state overlay images:** streamers can upload a background image/GIF per pull state
(rare/complete/duplicate), falling back to the global background. `SaveBackground` gained a `slot`
(global = `bg.<ext>`, states = `bg-<slot>.<ext>`), `/api/overlay-image?state=` routes it, serving is
generalized by exact stem. `overlay.js` `applyStateColors → applyStateOverrides` now applies color set
+ background per state and resets to global on normal (also fixes a latent per-state-color "sticking"
bug). Admin State Overrides panel gains a per-state background upload. Verified in preview.

**Cut 0.7.1** (bumps the third version part — sub-feature milestone): rolls up everything since
0.7.0.2 (per-state overlay images + backup retention on top of the cloud Settings page and new-user
UX pass). Version → 0.7.1 in all 5 locations; `docs/patch-notes/v0.7.1.md` added; EXE + dist rebuilt;
tag `v0.7.1`. No code reverted.

### 2026-07-01 — Claude (claude-opus-4-8) — Backend: config backup retention (no release)

Config saves dropped a timestamped backup every time and never cleaned up, so `config-backups/`
grew unbounded. Added a retention policy:
- `LocalFileDataStore.PruneBackups(keep)` trims each managed backup type (components / featured-boost /
  discord-role-awards / system-profile) to the N most recent by filename timestamp; only touches
  recognized managed config backups, never inventory. `WriteAtomic` calls it after creating a backup,
  reading the count from the `backupRetention` app setting (default `DefaultBackupRetention = 30`,
  0 = keep all).
- `GET /api/settings` returns `backupRetention`; `POST /api/settings/backup-retention` sets it
  (validated 0..5000). Uses the generalized `AppSettings` KV store, so setting it preserves the cloud
  choice (verified live).
- Smoke test `TestBackupRetention` (fresh temp store): 7 backups accumulate → PruneBackups(3) keeps 3
  → PruneBackups(0) keeps all. Linked `AppSettings.cs` into the test csproj. Build 0/0, suite green.

Enforced automatically with the default — no UI needed; a Settings toggle can wire the endpoint later.

### 2026-07-01 — Claude (claude-opus-4-8) — Housekeeping + retired the Dev UI Bench (no release)

Docs/repo cleanup, no code behavior change.
- **README** refreshed to shipped reality (native-Twitch-first framing, 0.7 features listed, roadmap =
  0.7.0.2 shipped with hosted phase deferred).
- **HANDOFF** pre-0.7 session log moved to `HANDOFF-archive.md` (~2422 → ~1500 lines); top identity +
  Current State refreshed to 0.7.0.2.
- **Known Bugs.txt** trimmed to the two open items; shipped fixes point to patch notes.
- Removed a stray empty `backups/` folder.
- **Retired the Dev UI Bench** (`tools/dev-ui-bench/` + `docs/dev-ui-bench.md` deleted, README/HANDOFF
  references scrubbed) — superseded by the direct-edit + live-preview workflow and drifting from the UI.
  The user chose to retire it. The 06-27 build entries remain below, banner-annotated as history.

### 2026-07-01 — Claude (claude-opus-4-8) — Background scaffolding + roadmap de-risking (no release)

Autonomous, low-risk-only pass while the user was at work. No behavior changes on critical paths, no
version bump.

- **AppSettings generalized** to a small key/value store (`GetString/GetBool/GetInt/Set`) so future
  Settings options (backup retention, start-with-Windows, update channel) get a home without new
  files. Cloud behavior unchanged; `Set` now preserves other keys. Build 0/0; settings round-trip
  verified in preview.
- **Repo-push prep:** added `.gitattributes` (normalizes line endings — kills the CRLF churn) and
  extended `.gitignore` (`circuitos-settings.json`, future `twitch-bot-tokens.local.json`).
- **Design capture:** `docs/feature-requests-analysis.md` §4 "Hosted cloud" — why you can't ship a
  master key, the two correct auth designs (user-session + row permissions, or a function layer), and
  the cost/uptime/privacy hats. `docs/updater-velopack-plan.md` — the full actionable Velopack +
  GitHub Releases integration checklist (Step 0 repo → app code → vpk build → signing), so it's fast
  once the repo exists.

**Next when the user's ready:** create the GitHub repo (gates the Velopack updater), then execute
`docs/updater-velopack-plan.md`. Hosted cloud is its own future milestone (§4).

### 2026-07-01 — Claude (claude-opus-4-8) — Cut 0.7.0.2 (cloud Settings page + new-user UX pass)

Release cut bundling everything since 0.7.0.1. **No code reverted; version bumped 0.7.0.1 → 0.7.0.2.**

**Settings page (new):** app-level Settings view with a Local/Cloud data-backend choice, an Appwrite
connection form (write-only API key, Test connection), an About panel (version / storage / data
folder + Open-folder), and a Preferences panel (relocated the Hide-System-Check toggle). Backend:
`AppSettings` (persists the backend choice), `AppwriteOptions.Save`/`RedactedStatus`, graceful cloud
startup (reads the saved choice, connectivity-probes, falls back to local with `/api/health.cloudError`
if it can't start), and `/api/settings*` endpoints. Cloud is now reachable without the `--cloud` flag —
framed as advanced/self-host (bring-your-own Appwrite). Hosted-cloud-for-everyone remains a future
infra decision.

**UX pass (UX.md tiers 1–3, all done):** de-Streamer.bot'd the new-user flow (first-run → Twitch,
Streamer.bot marked optional, native "go live locally" copy corrected), plain-language wizard/help
(field examples, redemption-name/singular-plural explained, boost/events/search de-jargoned), and
consistency/polish (naming, confirm-button match, onboarding-framed Twitch permissions card, health
table no longer truncates).

**Also landed earlier this session (already in 0.7.0.1 or on main):** the zero-config native-listener
hotfix, command tester, inline device login. **Deferred (in `docs/feature-requests-analysis.md`):** bot
chat account, cross-profile currency (shops/2.0), per-state overlay images, and the **Velopack + GitHub
Releases installer/updater** (chosen direction; gated on creating the GitHub repo — that's the next
build once the repo exists).

Version → 0.7.0.2 in all 5 locations; `docs/patch-notes/v0.7.0.2.md` added; EXE + dist rebuilt; tag
`v0.7.0.2`.

### 2026-06-29 — Claude (claude-opus-4-8) — Renumber 0.7.1 → 0.7.0.1 (restore four-part scheme)

Renumber only — **no code reverted**, all 0.7 fixes stay. The three-part `0.7.0` / `0.7.1` deviated
from the project's four-part scheme (`0.6.0.8`) and over-claimed / burned runway; neither was
distributed (dev builds only). Now a single **0.7.0.1** for the whole 0.7 line to date.
- Version strings set to `0.7.0.1` in all 5 locations (csproj ×3, `/api/health`, integrationVersion,
  circuitosVersion, README).
- Consolidated `v0.7.0.md` + `v0.7.1.md` → one `docs/patch-notes/v0.7.0.1.md`; deleted the two.
- Deleted tags `v0.7.0` and `v0.7.1`; created `v0.7.0.1`. EXE + dist rebuilt at 0.7.0.1.
- Going forward: fixes accumulate under 0.7.0.1; next bump is 0.7.0.2 etc. (see the versioning-cadence
  preference — bump only on a release, not per fix).

### 2026-06-29 — Claude (claude-opus-4-8) — HOTFIX: zero-config login killed the native listener

**User report:** on 0.7.1, logged in + refreshed, but commands, redemptions, AND overlay all dead.

**Root cause:** `TwitchRuntime.TryStart` still called `TwitchOptions.TryLoad(dataRoot)`, which returns
**null when there's no `twitch.local.json`** — i.e. the zero-config case the device-flow login created.
So `TryStart` returned null and the **EventSub listener never started**. That one socket powers chat
commands, redemption intake, and (via redemptions → overlay-state.json) the overlay, so all three died
together. When I moved the login + reward endpoints to `Resolve`, I missed the listener itself.

**Fix:** `TwitchRuntime.TryStart` now uses `TwitchOptions.Resolve(dataRoot)` (bundled client id when no
file), wrapped in try/catch → null only if genuinely unconfigured; still returns null when not logged
in. Also moved the `--twitch-reward` diagnostic off `TryLoad`. Build 0/0, smoke green. No version bump
(per the runway preference) — rebuilt the 0.7.1 binary in place.

**Still likely needed by the user (app-switch fallout, not a code bug):** their existing channel-point
reward was created by the OLD Twitch app; the NEW bundled app didn't create it. Commands work
immediately after this fix; for redemptions, re-sync/create the reward under the new app from the
Twitch page so it's manageable/fulfillable. Confirm the profile is **Live** (redemptions only route to
live profiles).

### 2026-06-29 — Claude (claude-opus-4-8) — Autonomous: command tester, inline login polish, feature writeups

Ran unsupervised (maintainer at work, no approvals). Banked each piece as its own commit; skipped
anything needing a product decision and wrote those up instead.

- **Command tester** (`e6cd7aa`) — "Test a command" box on the Game Profile page runs any chat command
  through `/api/runtime/action` against the editing profile's saved data as a **sandbox viewer**
  (`__command_test__`), so no live data changes. Frontend-only (endpoint already existed). Verified in
  preview: known words return replies, unknown words error cleanly, no console errors. Addresses the
  "test feature on commands" request.
- **Inline Twitch login polish** (`e8c78df`) — split the device flow into `TwitchAuth.RequestDeviceCode`
  + `PollDeviceToken` (LoginDeviceFlow composes them) and added `/api/twitch/login/start` +
  `/api/twitch/login/poll`. The host opens the pre-filled activate page; the panel shows the user code
  and polls instead of holding one blocking request. **Additive + safe:** the old blocking
  `/api/twitch/login` is untouched and the frontend falls back to it when `start` returns
  `inline=false` (self-host w/ secret), so login can't break. Verified live: `/start` returns a real
  code, `/poll` returns pending/expired correctly. Only the human-authorize step is unverified (reuses
  the proven `PollDeviceToken`). **Worth a real login test when you're back.**
- **Feature writeups** — `docs/feature-requests-analysis.md`: account linking (recommend Helix
  resolve-at-add, `altUserId→mainUserId`; need decision: per-profile vs global) and cross-profile
  currencies (3 interpretations — need you to pick intent). Not implemented; both need your call.

### 2026-06-29 — Claude (claude-opus-4-8) — Twitch login: Device Code Flow (zero-config distribution) — IN PROGRESS

**Problem (user-reported):** when anyone other than the dev tries to log in, they hit
"twitch.local.json was not found." Root cause: the login uses the **authorization-code grant**,
which needs a **client secret**, and the design required every streamer to register their own
Twitch app and supply clientId+secret in `twitch.local.json`. That breaks the zero-config vision,
and you can't ship your own secret (a client secret in a distributed desktop app is extractable).

**Decision (with user): full zero-config via Twitch Device Code Flow.** CircuitOS registers ONE
Twitch app (Client Type = **Public**), bundles only its **clientId** (public by design), and each
streamer logs in via the device flow (enter a code at twitch.tv/activate) — no secret, no per-user
Twitch app, no `twitch.local.json` required.

**Done this session (additive, compiles 0/0, smoke green — NOT yet shipped, tag NOT moved):**
- `TwitchOptions`: added `DefaultClientId` (bundled, **currently empty — must be filled**), `HasSecret`,
  and `Resolve(dataRoot)` which never returns null (file wins; else bundled clientId; secret optional).
  Left `TryLoad` intact so legacy paths + tests are unaffected.
- `TwitchAuth.LoginDeviceFlow(opts, dataRoot, onPrompt, cancel)` — full device flow: request
  device/user code → `onPrompt` shows where to enter it → poll token endpoint (handles
  `authorization_pending`) → fetch identity → save (encrypted). Added `DeviceCodePrompt` record and
  `PostFormRaw`. `Refresh` now omits the secret when absent (public-client tokens refresh secret-less).
- `Program.cs --twitch-login` now uses `Resolve` and picks device flow when there's no secret (prints
  the code in headless, MessageBox + opens browser otherwise). The legacy loopback flow still runs
  when a secret IS present (self-host).

**DONE (second pass):**
1. ✅ `TwitchOptions.DefaultClientId` set to the CircuitOS Public app id `rs7hti26ty98in6ltdjd8rb980wjjb`.
   Validated live against `https://id.twitch.tv/oauth2/device` — returns a device/user code, confirming
   the app is Public + device-grant enabled. Twitch's `verification_uri` even pre-fills the code
   (`twitch.tv/activate?device-code=XXXX`).
2. ✅ In-app `/api/twitch/login` now uses `Resolve` + device flow when no secret: a desktop dialog
   (`ShowDeviceCodePrompt`) opens the pre-filled activate URL and shows the code, then the request
   blocks on the poll and returns on success — no frontend change needed (reuses the existing button).
   Headless mode logs the code to the console. Legacy loopback flow still used when a secret is present.
3. ✅ Reward endpoints (`ListTwitchRewards`/Sync/Update/Delete) switched to `Resolve`.

**SHIPPED as 0.7.1.** User live-tested the in-app login (device flow) successfully. Removed the
blocking MessageBox — `ShowDeviceCodePrompt` now opens Twitch's pre-filled activate URL directly and
polls in the background, so the flow is just Log in → Authorize → done. Kept the device flow (not
implicit) so sessions keep their refresh token and survive long streams — decided with the user.
Version bumped 0.7.0 → 0.7.1 (all 5 locations), `docs/0.7-twitch-auth-setup.md` rewritten for the
zero-config model, `docs/patch-notes/v0.7.1.md` added, EXE + dist rebuilt, tag `v0.7.1` created.

**Optional polish (not required):** show the code inline on the Twitch admin page with async polling
instead of opening the external browser; a nicer "waiting for authorization…" state on the login button.

---

### 2026-06-29 — Claude (claude-opus-4-8) — 0.7 review + reliability/security hardening

**Goal:** Full review of the 0.7 source after cutting the release, then fix what the review surfaced.
Folded into 0.7.0 (the tag was local-only, never distributed), so the EXE/dist were rebuilt.

**P0 found and fixed — native redemptions didn't drive the OBS overlay.** `overlay-state.json` was
only ever written by `StreamerbotReedeem.txt`; the native `DispatchRuntimeAction` redeem path wrote
inventory + chat but never overlay state. A fully-native streamer (the whole 0.7 pitch) got a dead
lower-third. `CircuitService.DispatchRuntimeAction` now writes `overlay-state.json` (byte-compatible
shape) on every native pull, via a new `ILocalDataStore.WriteOverlayState`. In cloud mode the host
passes the local store explicitly (new optional `CircuitService` ctor param) so it still works.

**Other fixes (all in `tools/runtime/`):**
- **Native `!salvage` never persisted** — the command branch mutated inventory in memory only.
  Now persists when `SalvageResult.Mutated`.
- **Inventory writes weren't atomic** — `LocalFileDataStore.WriteProfileData` did a raw
  `File.WriteAllText`. Now atomic (tmp → re-parse validate → `File.Move`/`File.Replace`) with a
  rolling `.bak` for inventory. New `WriteOverlayState` is atomic too (no backup — display data).
- **EventSub had no keepalive timeout** — a half-dead socket blocked `ReceiveAsync` forever and
  redemptions silently stopped. Reads now time out at `keepalive_timeout_seconds + 5s` grace and
  force a reconnect. `keepalive_timeout_seconds` is read from `session_welcome`.
- **No redemption dedup** — Twitch can replay; now de-duped by `metadata.message_id` (bounded set).
- **Chat commands had no throttle** — added a per-viewer 3s cooldown (recorded only when we actually
  reply, so non-commands don't burn it). Protects Twitch's ~20-msg/30s send limit.
- **Tokens stored in plaintext** — `TwitchTokens` now DPAPI-encrypts access/refresh at rest
  (CurrentUser scope, app entropy). Legacy plaintext files still load (`"protected"` flag) and
  re-save encrypted, so no forced re-login. Added `System.Security.Cryptography.ProtectedData 9.0.0`
  to the runtime + smoke-test csproj.
- **No Host-header validation** — added a loopback-only `IsAllowedHost` allowlist on the local API
  (DNS-rebinding defense-in-depth).

**Tests:** extended `TestRuntimeDispatch` to assert (1) `overlay-state.json` is written with the
right viewer/part/version, and (2) native salvage persists the consumed duplicates. Full smoke suite
green, runtime builds 0/0.

**Still open (deferred, low-risk):** Overview slider fill drift (visual); per-profile overlay URLs
(overlay statics still publish only to the active profile — a live-but-not-editing profile's native
pull writes its overlay state, but its statics aren't served yet); porting the dup-protection fix into
`StreamerbotReedeem.txt` if Streamer.bot stays a supported path.

**Per-profile reward cost (also fixed this session):** added a `redemptionCost` profile field
(default 100, validated 1..1,000,000) mirroring the `redeemCooldownSeconds` pattern across
`DefaultProfile`/`NormalizeProfile`, the template, and the Game Profile UI (`#profileRedemptionCost`).
`TwitchRuntime.SyncRewardForProfile` now reads it instead of the hardcoded 100. Verified end-to-end in
the preview (edit → save → `/api/profile` returns the new cost).

---

### 2026-06-29 — Claude (claude-sonnet-4-6) — C4: per-state overlay color overrides

**Goal:** Complete the last remaining item on the 0.7 UI.md punch list — overlay editor state customization.
The Overlay Editor previously applied a single global color set to all pull states; the Rare/Complete/Duplicate
preview tabs switched the dummy state but did not expose any per-state styling controls.

**Changes:**

| File | Change |
|------|--------|
| `overlays/lower-quarter/overlay.js` | Added `stateColors` to `defaultOverlayConfig` and `normalizeOverlayConfig()`. Extracted `applyColorSet(root, accentColor, labelColor, barColor)` helper. Added `applyStateColors(stateName, config)` — picks from `config.stateColors[state]`, falls back to global appearance colors if the override field is empty. Called from `renderState()` after state class assignment. `normalizeStateColor()` validates each override field. |
| `tools/admin/app.js` | Added `activeOverlayPreviewState = "normal"` variable. Added `renderStateColorFields()` — shows instructional note when Normal; renders 3 color pickers (Accent, Label, Bar Fill) pre-populated from the state override or global fallback when Rare/Complete/Duplicate is active. Called from `renderOverlayEditor()` and the `[data-preview-state]` click handler. |
| `tools/admin/index.html` | Added "State Overrides" panel (`STATE COLORS` kicker) with `#overlayStateColorsNote` and `#overlayStateColorsFields`. |
| `UI.md` | Marked C4 Done. All 0.7 punch list items are now Done/Verified. |

**Verified (preview server):** State Overrides panel visible below Appearance with instructional note on
Normal. Clicking Rare renders three color pickers pre-filled with global defaults. No console errors.

**UI.md punch list is COMPLETE.** Next step: cut the 0.7 release or live-test Twitch reward flow.

---

### 2026-06-27 — Codex — Verification + documentation realignment

**Goal:** Verify the current `C:\Dev\CircuitStreamSystem` source and clean up stale docs/notes before
new work. User clarified that `UI.md` was manually updated with new asks, so any older "UI.md complete"
notes refer to the previous list, not the current one.

**Verification:**
- `dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release` passed with 0 warnings/errors.
- `dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data streamerbot-actions`
  passed. Coverage includes first-run safety, generated Streamer.bot structure, active-profile collision
  guards, runtime dispatch, pull/redemption/command engines, and Appwrite/Twitch config loaders.

**Docs cleaned:**
- `AGENTS.md` — current version/status updated from stale 0.6.0.6 text to 0.6.0.8 shipped + 0.7 unreleased.
- `README.md` — 0.7 progress updated to reflect direct Twitch OAuth, native desktop bridge slices, and the
  current UI/verification focus.
- `UI.md` — normalized into the active 0.7 launch punch list.
- `docs/patch-notes/0.7-dev-progress.md` — updated with native Twitch bridge progress, verification results,
  and current remaining work.
- `docs/0.7-cloud-foundation.md` — added a supersession note: desktop bridge uses direct Twitch OAuth +
  EventSub WebSocket today; hosted Auth0/webhook design is future deployment territory, not current prerequisite.
- `docs/0.7-twitch-auth-setup.md` — added chat scopes and re-login note.
- `docs/0.7-appwrite-dev-setup.md` — clarified that Auth0 is not required for the current desktop bridge.
- `HANDOFF.md` — current-state summary updated so the top of the file matches later session entries/source.

**Next best step:** work through `UI.md` in small verified slices, starting with global/sidebar theme coverage
and the Overview card/rate-editing issues because they are visible, low-risk, and directly affect first-run trust.

---

> **NOTE: The Dev UI Bench described in the next three 06-27 entries was RETIRED and deleted on
> 2026-07-01** (superseded by direct editing + live preview). Kept here as history only.

### 2026-06-27 — Codex — CircuitOS UI Bench dev tool scaffold

**Goal:** Create a dev-only standalone UI planning tool so the user can stay productive during
Claude/Codex usage limits by designing UI changes and exporting wiring tickets. This is not a
user-facing feature and does not edit user data or production source.

**Added:**
- `docs/dev-ui-bench.md` — purpose, non-goals, safe boundaries, workflow, and ticket format.
- `tools/dev-ui-bench/README.md` — quick local usage and boundaries.
- `tools/dev-ui-bench/index.html` — static browser shell.
- `tools/dev-ui-bench/styles.css` — CircuitOS-like mock UI styling.
- `tools/dev-ui-bench/app.js` — screen selector, component palette, property editor, localStorage draft
  save, live mock preview, copy/download Markdown wiring ticket.

**Validation:** `node --check tools/dev-ui-bench/app.js` passed using the bundled Codex Node runtime.

**Boundary:** Proposal-only. No CircuitOS APIs, no Appwrite/Twitch/Streamer.bot calls, no production
source mutation, no profile/inventory data access.

**Next best step:** use UI Bench to create a ticket for the first `UI.md` item, then wire that item
in the real app as a small verified slice.

---

### 2026-06-27 — Codex — UI Bench style import

**Goal:** Let the dev-only UI Bench import the current CircuitOS look so mockups can be edited against
the real theme instead of only the default bench palette.

**Changes made:**
- Added Style Import controls to `tools/dev-ui-bench/index.html`: CSS file import, paste box, reset,
  status line, and theme variable editor.
- Added browser-only CSS variable parsing/editing in `tools/dev-ui-bench/app.js`; known `:root`
  variables repaint the preview live, save to localStorage, and export into the Markdown wiring ticket.
- Added compact sidebar styling in `tools/dev-ui-bench/styles.css`.
- Updated `docs/dev-ui-bench.md` and `tools/dev-ui-bench/README.md` with the safe import workflow.

**Boundary:** still proposal-only. Import reads a user-selected/pasted CSS blob in the browser; it does
not write back to `tools/admin/styles.css`, user data, secrets, Twitch, Appwrite, or runtime APIs.

**Validation:** JavaScript syntax passed with bundled Node.

---

### 2026-06-27 — Codex — UI Bench visual canvas import

**Goal:** Make UI Bench usable as an actual visual editor, not only a component-list planner: import the
current app/welcome screen, click elements directly, resize/hide/reorder proposal blocks, and export a
wiring ticket.

**Changes made:**
- Added Current Layout Import controls to `tools/dev-ui-bench/index.html`: `index.html` file import,
  paste fallback, blank mockup reset, and status output.
- Added screen-to-view mapping in `tools/dev-ui-bench/app.js` for current admin screens plus first-run
  welcome wizard steps.
- Added browser-only visual canvas rendering from imported HTML; selected static panels, toolbars,
  buttons, fields, and toggles can be clicked directly.
- Imported full `styles.css` is stored and injected into an isolated canvas instead of only parsing color
  variables, so the editor preview can match the current app more closely.
- Overview runtime containers such as Pull Rates, Collection Health, Event Timeline, Economy Pulse, and
  Top Collectors are hydrated with fake rows when the imported static HTML is empty.
- Added proposal controls for label edits, hidden state, size presets, move up/down, and drag reorder.
- Exported wiring tickets now include `Layout source`, source selector, hidden state, and canvas size.
- Updated `docs/dev-ui-bench.md` and `tools/dev-ui-bench/README.md` with the layout-import workflow.

**Boundary:** still proposal-only. The importer reads user-selected/pasted HTML in the browser and
stores drafts in localStorage. It does not execute `tools/admin/app.js`, call APIs, mutate production
source, or touch user/profile data. Runtime-generated controls may still need to be added manually.

**Validation:** JavaScript syntax passed with bundled Node; browser smoke verified isolated full-CSS canvas
rendering, fake Overview runtime rows, full-width canvas behavior, and zero console warnings/errors.

---

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

### 2026-06-24 — Claude (claude-opus-4-8) — Native Twitch: cooldown + dup-protection now reflect live

User report: profile settings (cooldown etc.) weren't taking effect on native redemptions. The redeem
dispatch read the profile fresh each time, but **never used `redeemCooldownSeconds`** and read dup-protection
from the *request* (always absent → 0). Fixed:
- **Cooldown enforced**: per-viewer in-memory cooldown (`_lastRedeem`, keyed `profileId:viewerId`) from
  `redeemCooldownSeconds`; within the window the redeem returns **429** and `TwitchRuntime` cancels (refunds the
  points) and posts the cooldown message to chat. Recorded only after a successful pull. Resets on restart.
- **Dup protection** now read from the profile (`redeemDupProtectionTurns`), not the request.
Both reflect on the next redemption (the dispatch re-reads the profile each time). Smoke green.

**Note:** the reward **title/cost** still don't live-update — `EnsureReward` runs at startup, so renaming
`redemptionName` or changing cost needs an app restart (and cost is still the 100 placeholder). Reward
re-sync on save + a profile cost field are the follow-ups.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Phase 4 native Twitch: pull announcements in chat

The redeem dispatch now also returns formatted **announcement lines** (centralized, reusable), and the Twitch
path sends them to chat after fulfilling. Verified via smoke (`TestRuntimeDispatch` runs a redeem → the
formatter is exercised; full suite green).

- `CircuitService.BuildRedeemAnnouncements(messages, RedemptionResult, viewerName)` formats the same set the
  Streamer.bot action emits — `redeemSuccess` (always), `rarePull` (if rareLabel), `triplePull` (streak == 3),
  `collectionComplete` (newly completed), `variantPull` (if variants + template non-blank). Blank templates are
  skipped. Helpers `FormatTemplate` + `OneInOdds`. The redeem `ServiceResult` now carries `["messages"]` (like
  commands do).
- `TwitchRuntime.HandleRedemption` sends each announcement via `SendChatMessage` after FULFILLED (try/catch,
  bails quietly if chat scope isn't granted — same `--twitch-login` re-consent as chat commands).

So a redemption now: routes to the live profile → records the pull → fulfils → **announces in chat**. Needs the
chat-scope re-login (shared with slice 3). **Still ahead:** Twitch status in the admin UI; persist reward↔profile
map; per-profile reward cost. Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Phase 4 native Twitch — slice 3: chat commands

Built (compiles clean; deploy blocked at commit time only because the user's dev build was running and locked
the DLL — close it + rebuild to deploy). **Requires a one-time re-login** for the new scopes.

- **Scopes** (`TwitchAuth`): added `user:read:chat` + `user:write:chat`. ⚠️ Existing tokens lack these → the
  chat subscription fails gracefully (logged) until the user re-runs `--twitch-login` to re-consent.
- `TwitchHelix.SendChatMessage(text)` — POST `/helix/chat/messages` as the broadcaster.
- `TwitchEventSub` — now also subscribes to `channel.chat.message` (when an `onChat` handler is supplied) and
  routes notifications by `subscription_type`; added the `ChatMessage` record.
- `TwitchRuntime.HandleChat` — a `!`-prefixed chat message → `DispatchRuntimeAction(command)` (resolves the
  live profile that owns the word, returns reply lines in `Body["messages"]`) → `SendChatMessage` each line.
  Non-commands / unowned words are ignored silently. Wired into `TwitchRuntime.TryStart` (so the running app
  gets chat too).

**To use:** close the running app → `dotnet build …` → `--twitch-login` (re-consent) → relaunch the dev build →
type e.g. `!components` in chat → bot replies. Redemptions keep working without the re-login.

**Still ahead:** pull announcements in chat (the redeem path returns structured data, not a formatted message —
would format `redeemSuccess`/`rarePull`/etc. and send); Twitch status in the admin UI; persist reward↔profile
map; per-profile cost. Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Phase 4 native Twitch: folded the listener into the running app

`TwitchRuntime.TryStart(store, service, dataRoot, log, cancel)` (new) encapsulates the listen flow (ensure
rewards → map reward→profile → EventSub → on redemption dispatch + fulfil). The **running app now auto-starts
it on a background task** (`Program.cs`, right after the server task, cancelled on exit) — no separate console
needed. It returns null / no-ops cleanly when Twitch isn't configured or no profile is live. The
`--twitch-listen` diagnostic was slimmed to call the same `TryStart` (DRY). Builds clean; smoke green.

**Dev build:** run the full windowed app and native Twitch comes along —
`dotnet …\bin\Release\net9.0-windows\CircuitOS.dll --data <DataRoot> --ui tools\admin --actions
streamerbot-actions --overlay overlays\lower-quarter`. Redemptions update inventory (visible in the panel) and
fulfil on Twitch. (Twitch log lines go to the console if launched from one; the panel's inventory is the
windowed-mode proof.) Created a double-click launcher at `Documents\CircuitOS\run-circuitos-dev.cmd` (not in repo).

**Still ahead:** surface Twitch status in the UI/health; persist reward↔profile map; per-profile cost; slice 3
chat commands (needs re-login for chat scopes). Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Phase 4 native Twitch — slice 2: EventSub WebSocket (redemptions live)

**✅ LIVE-VERIFIED on @shortcircuit_tv (2026-06-24).** `--twitch-reward` created the "Circuit Component" reward
on-channel; `--twitch-listen` connected, a real redemption routed to the `circuit-components` profile, the pull
was recorded + inventory saved, and the redemption was FULFILLED. The full native path
(login → reward → EventSub WebSocket → dispatch → fulfill) works in the desktop app, no hosting.

- `TwitchEventSub` (new) — connects to `wss://eventsub.wss.twitch.tv/ws`; on `session_welcome` creates the
  `channel.channel_points_custom_reward_redemption.add` subscription (websocket transport, no public endpoint);
  parses `notification` events → `RedemptionEvent`; handles `session_reconnect` + reconnect-with-backoff;
  keepalive is a no-op.
- `TwitchHelix.CreateEventSubSubscription` added.
- `--twitch-listen` run mode (`Program.cs`): ensures each **live** profile's reward exists, maps
  reward id → profile id, opens the socket, and on each redemption: `DispatchRuntimeAction(redeem)` →
  `UpdateRedemptionStatus(FULFILLED)` on success / `CANCELED` (refund) on failure. Console mode, Ctrl+C to stop.

**Live test (user):** `--twitch-login` (once) → take a profile live in the admin UI → run `--twitch-listen`
from a terminal → redeem the channel-point reward on Twitch → expect "FULFILLED" + inventory updated, and the
points refunded if the pull fails.

**Still ahead:** fold `--twitch-listen` into the running app (background task on launch when profiles are live)
instead of a separate console mode; persist the reward id ↔ profile map; per-profile cost (currently 100
placeholder); keepalive-timeout reconnect; **slice 3 = chat commands** (`channel.chat.message` + Helix
send-chat → needs `user:read:chat`/`user:write:chat` scopes → re-login). Edge: two live profiles with the same
`redemptionName` collide on one reward (no guard yet). Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Phase 4 native Twitch — slice 1: token refresh + Helix reward

**Started the native zero-config Twitch path.** Decisions (with user): **EventSub over WebSocket** (not
webhooks) and **redemptions first** (uses existing scopes — no re-login).

**⭐ Roadmap-reshaping decision — EventSub WebSocket:** the app connects *outbound* to
`wss://eventsub.wss.twitch.tv/ws`, gets a session id, and binds subscriptions to it — **no public endpoint /
no hosting required.** This means the native Twitch path **runs in the desktop app today**; it does NOT depend
on the hosted Phase 5 (the old `0.7-cloud-foundation.md` design assumed webhooks → a public Function URL).
Supersedes that transport choice for the desktop build.

**Slice 1 (built; live-verify is the user's — needs their Twitch token; Helix isn't in the smoke harness):**
- `TwitchAuth.Refresh(opts, current, dataRoot)` — exchanges the stored refresh token for a fresh access token
  and re-saves. (No refresh existed; a 4h token would have died mid-stream.)
- `TwitchSession` (new, `TwitchHelix.cs`) — holds tokens, auto-refreshes ~5 min before expiry, persists.
  Shared by Helix + (coming) the EventSub socket.
- `TwitchHelix` (new) — authed Helix wrapper (Bearer + Client-Id, refresh-once-on-401): `EnsureReward`
  (idempotent create/update of the channel-point reward), `ListManageableRewards`, `UpdateRedemptionStatus`
  (FULFILLED/CANCELED).
- `--twitch-reward` diagnostic (`Program.cs`) — creates/updates the reward titled from the active profile's
  `redemptionName` (cost placeholder 100). **Test:** run it → the reward should appear in your channel's
  Channel Points.

**Next — slice 2 (the intake):** `TwitchEventSub` WebSocket client — connect, handle welcome/keepalive/reconnect,
create the `channel.channel_points_custom_reward_redemption.add` subscription (transport=websocket), and on a
redemption: map reward id → live profile → `DispatchRuntimeAction(redeem)` → `UpdateRedemptionStatus(FULFILLED)`.
Expose as `--twitch-listen`, then fold into the running app. Then slice 3: chat commands (needs added scopes →
re-login). Reward id ↔ profile mapping persistence still TODO. Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Item C frontend: active-profiles admin UI working

The active-profiles admin UI (built mostly by the Codex session) is now **functional** — verified live with
two profiles live at once. Fixed two bugs in it:
- `renderViewOnDemand("profiles")` only called `renderProfiles()`, never `renderProfilesSummary()`, so the
  **"what's live" banner never rendered** (it's only re-called from `loadProfiles`, which skips it unless you're
  already on the view — chicken-and-egg). Now calls both.
- The per-card **"Switch" button was created but never appended** (`actions.append(switchBtn)` was missing), so
  non-editing profiles had no Switch action. Fixed.

**What works now (Profiles view):** a summary banner ("N live" + which profile you're editing + which are live);
per-profile cards with EDITING / LIVE badges, status ("Editing + live" / "Editing only" / "Live now" / "Ready to
go live"), and **Go Live / Stop Live** (→ `activate`/`deactivate` ops), Switch, Rename, Delete. Activation
collisions surface via the top notice (the service-level guard is smoke-tested). Verified: created a 2nd profile,
took it live → "2 live", both cards correct.

**Deferred (the one remaining item C piece): per-profile overlay URLs.** Can't be added cleanly yet because the
runtime only **publishes overlay statics into the *active* profile's folder** at startup (`Program.cs` →
`PublishOverlayStatics(overlayDataPath = active profile)`). A live-but-not-editing profile's `overlay/index.html`
doesn't exist, so a per-profile overlay path would 404 in OBS. **To do it right:** publish overlay statics to
**every live** profile's folder on startup (iterate `ListProfiles().Where(IsLive)`), then show/copy each profile's
path on its card. Pairs naturally with the Phase-5 hosted overlay work. Unreleased; no version bump.

---

### 2026-06-24 — Claude (claude-opus-4-8) — Sole driver; landed runtime dispatch (item C groundwork)

User asked me to take over as the single driver (parallel Codex sessions stopped) after concurrent edits to
the same files caused commit collisions. **Lesson reinforced: one agent at a time on shared files.**

**Landed the runtime command/redeem dispatcher** (developed by the Codex session; I fixed its failing test and
committed it green). `CircuitService.DispatchRuntimeAction(request)` is the native entry point that routes an
incoming **command** or **redeem** to the **matching live profile** and runs it through the shared engines:
- `ResolveRuntimeProfileId` picks the target profile — explicit `profileId`, else the live profile whose
  `commands` own the incoming command word, else the first live profile. This is the multi-active-profile
  routing (each live game owns its command words / reward).
- Command → `CommandEngine`; redeem → `RedemptionEngine` (well, the pull path) → writes **profile-scoped**
  inventory via `WriteProfileData`/`ImportProfileData`. Returns `{profileId, profileName, ...}`.
- `Program.cs` exposes it on the local HTTP API. New smoke test `TestRuntimeDispatch` exercises both paths.
- **Bug I fixed:** the test asserted the returned `profileId == "second"` (a stale literal) after randomizing
  the profile id to `"dispatch-<guid>"` — changed to compare against the actual `profileId`. Dispatch code
  itself was correct. **Full smoke suite green.**

This is the runtime half of **item C / Phase 4 native routing**: redemptions + chat commands now resolve to
the right active profile and call the shared engines. Still ahead: the **admin UI** for active profiles
(toggles, live-vs-editing, per-profile overlay URLs) and wiring the native Twitch EventSub intake to
`DispatchRuntimeAction`. Unreleased; no version bump.

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


---

## Older sessions

Session-log entries before the current 0.7 milestone (0.4–0.6.0.8 and early groundwork) have
been moved to **HANDOFF-archive.md** to keep this file focused. See it for the full history.
