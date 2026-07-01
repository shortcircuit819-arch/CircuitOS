# CircuitOS

CircuitOS is a configurable Twitch collection-game platform with **native Twitch
integration — no code to paste**. Circuit Components is the included starter
profile, while the editor supports custom games, terminology, collections,
themes, messages, events, and currencies. Streamer.bot is supported as an
optional alternative.

Current application version: **0.7.1**

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
- Guided first-run setup, then one-click Twitch connect to go live
- **Native Twitch integration (zero-config):** one-click Twitch login with no
  developer account or config files; CircuitOS creates and manages the
  channel-point reward and handles redemptions, chat commands, and pull
  announcements directly via EventSub — no code to paste
- Optional cloud data backend from the Settings page (bring-your-own Appwrite),
  with local storage the default
- Per-pull-state overlay colors (rare, complete, duplicate) and an in-app
  command tester for previewing chat output
- Bulk names/CSV importer for permanent and event collections with automatic IDs and preview validation
- Collapsible, searchable editor designed for large catalogs
- OBS lower-quarter display for successful pulls
- Self-contained Windows x64 application with data-free update packages
- Persistent CircuitOS attribution and official application branding
- Multiple independent game profiles — each with its own catalog, inventory, branding, and overlay config
- Switch active profiles from the admin panel without mixing data
- Export any active profile as a portable `.circuitmodule` bundle; import modules as new profiles
- Streamer.bot supported as an optional alternative (paste-ready C# actions still generated)

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

### 0.7 - Native Twitch + Cloud Foundation *(shipping — current milestone, 0.7.0.2)*

*Native Twitch integration, plus the foundation for cloud sync. Local storage remains the default.*

**Shipped (0.7.0.x):** native Twitch is the zero-config one-stop shop — **one-click Twitch login**
(device flow, no developer account or config files), CircuitOS creates and manages the channel-point
reward, and **redemptions, chat commands, and pull announcements run directly through EventSub** with
no code to paste. A **Settings page** adds an optional cloud data backend (bring-your-own Appwrite,
keyed to your Twitch id) with a safe fallback to local. Multiple simultaneously-live profiles,
per-pull-state overlay colors, shared pull/redemption/command engines, and reward
create/attach/sync/edit/delete all landed. See `docs/patch-notes/v0.7.0.2.md`.

- Native Twitch: zero-config login, reward management, EventSub redemptions, chat commands, and chat
  announcements — no code to paste ✓
- Optional cloud data backend (bring-your-own Appwrite) from Settings; local is the default ✓
- Streamer.bot remains supported as an *optional* alternative (MixItUp planned for 0.8) ✓
- `IDataStore` abstraction — data access is interface-driven, so the cloud path is a swap not a rewrite ✓

**Still ahead / deferred to a later phase:** a self-updating installer (Velopack + GitHub Releases,
see `docs/updater-velopack-plan.md`), and a true *hosted* cloud — admin panel from any browser,
cloud-served overlay, one backend for all users. Hosted cloud is a security/infrastructure decision,
analyzed in `docs/feature-requests-analysis.md`.

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
- `tools/runtime/`: .NET application source
- `tools/package/`: repeatable release, update, and signing scripts
- `dist/CircuitOS-Release/`: inspectable fresh-install package
- `dist/CircuitOS-Windows-x64.zip`: send-ready fresh-install archive
- `dist/CircuitOS-Update-<version>.zip`: data-free update archive
- `overlays/lower-quarter/`: OBS lower-quarter source
- `docs/`: setup, operation, safety, and feature documentation

For a safe manual UI and patch-fix workflow, see
`docs/maintainer-quick-fixes.md`.

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






