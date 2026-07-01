# CircuitOS

CircuitOS is a configurable Twitch collection-game platform powered by
Streamer.bot. Circuit Components is the included starter profile, while the
editor and generated actions support custom games, terminology, collections,
themes, messages, events, and currencies.

Current application version: **0.7.0.2**

## Current Features

- Weighted Twitch channel-point item redemption
- Backup-safe viewer inventory with locking and atomic replacement
- Inventory, missing-item, duplicate, collection, balance, salvage, and
  leaderboard chat commands
- One-time collection completion tracking
- Configurable permanent and date-aware event collections
- Featured-stream collection boosts
- Rare-pull and three-identical-pulls odds messages
- Per-collection item variants (shiny, foil, large, etc.) with independent
  roll odds, variant-aware chat messages, overlay tags, and simulation
- Per-collection rarity tiers with independent pull weights, tier-weighted
  pull logic, Rate Lab tier breakdown, and bulk tier assignment
- Bulk names/CSV importer with optional tier column
- Duplicate salvage economy and read-only economy analytics
- Searchable viewer inspector and Discord role-award queue
- Pull simulator and Discord-ready patch-note generation
- Configuration backup, validation, comparison, download, and recovery center
- Privacy-safe diagnostics report for tester and support workflows
- Editable branding, terminology, colors, commands, and chat messages
- Guided first-run setup with generated paste-ready Streamer.bot C# actions
- Bulk names/CSV importer for permanent and event collections with automatic IDs and preview validation
- Collapsible, searchable editor designed for large catalogs
- OBS lower-quarter display for successful pulls
- Self-contained Windows x64 application with data-free update packages
- Persistent CircuitOS attribution and official application branding
- Multiple independent game profiles — each with its own catalog, inventory, branding, and overlay config
- Switch active profiles from the admin panel without mixing data
- Auto-migration: existing data moves into the default profile folder on first 0.5 launch
- Export any active profile as a portable `.circuitmodule` bundle; import modules as new profiles
- `IDataStore` abstraction layer — data access is interface-driven, making the 0.7 cloud migration a swap rather than a rewrite

## Versioning Policy

CircuitOS uses intentional pre-release versioning:

- **0.3.x** releases contain UI improvements, performance work, bug fixes,
  documentation, and packaging changes. They do not intentionally change the
  saved-data contract or introduce a major new workflow.
- **0.5, 0.6, 0.7 and later milestones** each represent a major feature area.
  Work within a milestone uses a four-part version: **0.5.1**, **0.5.1.1**,
  **0.5.1.2**, etc. The third part is a sub-feature; the fourth is an iteration
  within that sub-feature. The milestone number (second part) only advances when
  the current milestone is fully satisfactory — not on a fixed schedule.
- **1.0** is reserved for the signed, stable public release after feature freeze,
  migration testing, installer/update testing, and release-candidate validation.
- **2.0** is reserved for the Shop and Currency Workshop architecture.

Application version, data-schema version, and Streamer.bot integration version
are tracked separately. A UI release does not automatically change the data or
integration version. The application version string lives in five places that
must be updated together when cutting a release: `CircuitOS.Runtime.csproj`
(`<Version>`/`<FileVersion>`/`<AssemblyVersion>`), `Program.cs` (`/api/health`),
`CircuitService.Core.cs` (`integrationVersion`), `CircuitService.Modules.cs`
(`circuitosVersion`), and this `README.md`.

## Roadmap

### 0.3.x - Stabilization

- Continue tester-driven UI, accessibility, performance, and bug fixes
- Keep large catalogs responsive and reduce setup friction
- Harden packaging, recovery, diagnostics, and update documentation

### 0.4 - Lower-Third Editor *(complete as of 0.4.6)*

- Panel background image with configurable opacity
- Customizable text labels and badge copy
- Accent color, position, sizing, and animation controls
- Live 1920×1080 preview in the admin panel
- Image upload and persistent overlay configuration

### 0.5 - Profiles and Modules *(complete as of 0.5.0.1)*

- Multiple independent collection games from one installation ✓
- Each profile has its own catalog, viewer inventory, branding, and overlay settings ✓
- Switch active profiles from the admin panel ✓
- Migration path: existing data auto-moves into the default profile folder on first launch ✓
- Export any active profile as a portable `.circuitmodule` bundle; import as new profiles ✓
- Profile create, rename, and delete ✓
- `IDataStore` abstraction layer — all data access routed through a swappable interface ✓

### 0.6 - Item Variants and Rarity Tiers *(complete as of 0.6.0.6)*

- Per-collection weighted variants such as shiny, foil, large, small, or
  alternate art — up to two variant tags per pull, independent rolls ✓
- Variant-aware odds, optional variant chat message, overlay tags, and
  simulation ✓
- Per-collection rarity tiers with independent pull weights; items assigned to
  a tier; tier-weighted pull logic in the Streamer.bot action ✓
- Rate Lab tier breakdown panel and tier-aware pull simulation ✓
- Bulk tier assignment (assign-all / assign-unassigned) and CSV tier column ✓
- Backward-compatible catalog rules — `variants` and `tiers` are optional and
  absent collections behave exactly as before ✓

### 0.7 - Cloud Platform + Twitch Integration *(in progress — unreleased desktop bridge)*

*CircuitOS transitions from a local Windows app to a hosted web platform in this release.*

**Progress (unreleased; default behavior remains local and unchanged):** the source now has a
desktop-on-cloud bridge behind a `--cloud` switch. Appwrite stores catalog/profile/boost/inventory
data, cloud profile management works, and cloud saves create a recovery point. Direct Twitch OAuth
login/logout works in-app and keys cloud data to the streamer's Twitch user id. Native Twitch work has also moved into the admin bridge: the Twitch Settings page can create/sync channel-point rewards, list and attach existing Twitch rewards, edit managed reward title/cost, persist reward ids per live profile, delete synced CircuitOS-managed rewards, run EventSub WebSocket redemption intake, send chat announcements, and use shared pull/redemption/command engines covered by smoke tests. The current pre-release focus is live Twitch verification, UI polish from `UI.md`, hosted admin/overlay planning, and folding cloud mode into a shipped 0.7 release. See `docs/0.7-cloud-foundation.md` and `docs/patch-notes/0.7-dev-progress.md`.

- Data migrates from local JSON files to Appwrite (database now; file storage still planned for
  cloud overlay/background assets)
- Streamers log in with Twitch OAuth; the desktop bridge uses direct Twitch OAuth today, while the
  future hosted version may revisit Auth0 or another account layer
- **Native Twitch is the one-stop shop: zero-config, no code to paste.** CircuitOS
  creates and manages the channel-point reward via the Twitch API and handles
  redemptions through EventSub. The desktop bridge currently uses EventSub WebSocket and admin-driven reward list/attach/sync/edit/delete with in-page permission guidance; hosted webhook/function architecture remains a later deployment option.
- Streamer.bot remains supported as an *optional* alternative (MixItUp planned for
  0.8); they forward to the same shared pull logic and are never required
- Admin panel accessible from any browser on any device
- OBS overlay served from a cloud URL

### 0.8 - Additional Bot Support

- MixItUp Bot integration alongside Streamer.bot and native Twitch EventSub
- Shared platform-neutral collection rules where practical

### 0.9 - Release Candidate

- Feature freeze and migration coverage
- Fresh-install and update-matrix testing
- Onboarding, diagnostics, recovery, and documentation review
- Signing and public-release preparation

### 1.0 - Signed Stable Release

- Trusted signed executable and release artifacts
- Stable compatibility promises for saved data and supported integrations
- Public installation, update, recovery, and support workflow

### 2.0 - Shop and Currency Workshop

- Configurable currencies and earning rules
- Shops, purchases, perks, and inventory-backed rewards
- Optional gambling and other currency sinks
- Discord roles, titles, and additional fulfillment workflows
- Economy controls, audit history, and abuse safeguards

## Important Files

- `data/components.json`: master collection catalog
- `data/inventory.json`: viewer inventory save file
- `data/system-profile.template.json`: portable branding profile template
- `streamerbot-actions/`: paste-ready Streamer.bot C# source
- `tools/admin/`: administration interface and local runtime
- `tools/dev-ui-bench/`: dev-only UI planning workbench for exporting wiring tickets
- `tools/runtime/`: .NET application source
- `tools/package/`: repeatable release, update, and signing scripts
- `dist/CircuitOS-Release/`: inspectable fresh-install package
- `dist/CircuitOS-Windows-x64.zip`: send-ready fresh-install archive
- `dist/CircuitOS-Update-<version>.zip`: data-free update archive
- `overlays/lower-quarter/`: OBS lower-quarter source
- `docs/`: setup, operation, safety, and feature documentation

For a safe manual UI and patch-fix workflow, see
`docs/maintainer-quick-fixes.md`. For the dev-only UI planning workbench, see
`docs/dev-ui-bench.md`.

## Safety Principles

- Never overwrite `inventory.json` without first creating a backup.
- Keep inventory writes locked, validated, and atomic.
- Keep the administration API bound to `127.0.0.1`.
- Validate catalog IDs, weights, event windows, salvage values, and boost
  references before saving.
- Keep update packages free of user data.
- Do not advise users to disable antivirus protection.
- Sign public 1.0+ releases and timestamp the signature.

See `docs/distribution-and-streamerbot-setup.md` for installation and update
instructions, and `docs/release-signing.md` for the signing workflow.






