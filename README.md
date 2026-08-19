# CircuitOS

CircuitOS is a configurable Twitch collection-game platform with **native Twitch
integration — no code to paste**. Circuit Components is the included starter
profile, while the editor supports custom games, terminology, collections,
themes, messages, events, and currencies.

Current application version: **1.0.1**

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
- Share a single collection as a `.circuitcollection` pack — it imports as a new profile that adopts your own theme

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
- **1.0** is reserved for the stable public release after feature freeze,
  migration testing, installer/update testing, and release-candidate validation.
- **2.0** is reserved for the Shop and Currency Workshop architecture.

Application version and data-schema version are tracked separately. A UI release
does not automatically change the data version. The application version string
lives in four places that must be updated together when cutting a release:
`CircuitOS.Runtime.csproj` (`<Version>`/`<FileVersion>`/`<AssemblyVersion>`),
`Program.cs` (`/api/health`), `CircuitService.Modules.cs` (`circuitosVersion`),
and this `README.md`.

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

### 0.7 - Native Twitch + Cloud Foundation *(shipped — 0.7.3)*

*Native Twitch integration, plus the foundation for cloud sync. Local storage remains the default.*

**Shipped (0.7.0.x):** native Twitch is the zero-config one-stop shop — **one-click Twitch login**
(device flow, no developer account or config files), CircuitOS creates and manages the channel-point
reward, and **redemptions, chat commands, and pull announcements run directly through EventSub** with
no code to paste. A **Settings page** adds an optional cloud data backend (bring-your-own Appwrite,
keyed to your Twitch id) with a safe fallback to local. Multiple simultaneously-live profiles,
per-pull-state overlay colors and images, backup retention, shared pull/redemption/command engines,
and reward create/attach/sync/edit/delete all landed. Streamer.bot was retired in 0.7.2 (native is
the only supported path), and 0.7.3 added shareable collection packs (`.circuitcollection`) plus
import name de-duplication. See `docs/patch-notes/v0.7.1.md`, `v0.7.2.md`, and `v0.7.3.md`.

- Native Twitch: zero-config login, reward management, EventSub redemptions, chat commands, and chat
  announcements — no code to paste ✓
- Optional cloud data backend (bring-your-own Appwrite) from Settings; local is the default ✓
- Streamer.bot integration **retired in 0.7.2** — native Twitch is the single supported path ✓
- `IDataStore` abstraction — data access is interface-driven, so the cloud path is a swap not a rewrite ✓

1.0 shipped as the first public GitHub Release, which brought the auto-update feed into existence. After
1.0: hosted cloud and the "CircuitOS on Twitch" extension in the 1.x line (see below).

### 0.8 - Design & Identity ✓ *(shipped 0.8.1)*

*Make CircuitOS look unmistakably its own — intentional, not generic.*

- A design-token layer and a full re-skin of the admin panel to the CircuitOS visual language:
  hairline structure over cards, crisp geometry (no pills), a soft accent glow as the only soft
  element, and hue-independent bones so any streamer's color theme still looks composed ✓
- **Design Mode** — an in-app overrides layer (roundness + per-token colors + reset), applied over the
  theme at runtime so the base skin is never edited and nothing drifts ✓
- Contrast-aware theming that stays readable across any accent/base combination ✓
- Six curated base themes including a light theme; the streamer picks a theme + one contrast-safe accent ✓
- Also in 0.8.1: an optional bot chat account, per-profile OBS overlays, and a CSRF fix on the local API ✓

### 0.9 - Distribution & Release Candidate ✓ *(shipped 0.9.0)*

*Make it genuinely shippable to non-technical streamers, then prove it's stable.*

- Velopack installer + GitHub auto-updater — replaces "extract a ZIP" with next-next-finish and in-app
  self-updates (Settings → About) ✓
- One-command release pipeline (`Build-CircuitOSVelopack.ps1`: publish → pack → optionally sign → upload) ✓
- Code signing wired and proven end-to-end — **parked, not a 1.0 gate**; a thumbprint swap the day a
  certificate exists (see `docs/release-signing.md`)

### 1.0 - Stable Public Release ✓ *(shipped 2026-08-19)*

1.0 shipped **unsigned** as CircuitOS's first public GitHub Release — which brought the auto-update feed
into existence (the updater reads GitHub Releases, and until v1.0.0 there was nothing to read). Everything
built across 0.4–0.9 is now a stable, installable release.

Unsigned installs trip Windows SmartScreen ("unknown publisher"). 1.0 mitigates that with transparency,
not suppression: `docs/installation-and-updates.md` documents the exact **More info → Run anyway** click
path, and the release publishes a SHA-256 checksum so the download can be verified. The signing pipeline
stays in the repo — a thumbprint swap the day a certificate exists — as a parked, optional step.
See `docs/release-signing.md`.

Remaining validation: the live install → update round-trip against the now-published feed, and the stable
compatibility promise for saved data and supported integrations.

### 1.x - Growth toward the economy

*Post-1.0 feature releases. The "online/social" features (1.4–1.7) build on the 1.1 hosted backend;
1.2 and 1.3 are standalone and can ship independently. Treat the ordering as direction, not a contract.*

- **1.1 — Hosted cloud** — a secure multi-tenant backend (Twitch login → per-user session + row-level
  permissions; never a shipped master key) so a viewer's collection can follow them with zero setup.
  The foundation for everything "online" below. Bring-your-own Appwrite and local mode remain supported.
- **1.2 — Streamer analytics** — insights on pulls, viewer engagement, and economy health. Standalone;
  no hosting required.
- **1.3 — Achievements & badges** — viewer collection milestones that later surface on their profile.
  Standalone.
- **1.4 — Online viewer profiles** — a viewer's inventory and collection progress, hosted and viewable.
  Needs 1.1.
- **1.5 — Twitch Extension** — CircuitOS on the channel page for every viewer, as a **panel** (deep:
  full leaderboard, inventory browser, progress) and a **video overlay** (glanceable: progress and live
  pull moments). Needs 1.1 + 1.4, plus Twitch's extension review.
- **1.6 — Module sharing** — publish a game/catalog online, shareable by link (builds on the existing
  `.circuitmodule` export). Needs 1.1.
- **1.7 — Module marketplace** — browse and download others' shared modules, with discovery. Builds on 1.6.
- **1.8 — Viewer trading & gifting** — let viewers exchange items between inventories; a strong
  engagement driver and the most direct on-ramp to the 2.0 economy.
- **1.9 — Shops readiness** — currency plumbing and economy scaffolding to bridge into 2.0.

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
- Publish a SHA-256 checksum with every public release so downloads can be verified; sign and timestamp
  releases if and when a signing certificate is available.

See `docs/installation-and-updates.md` for installation and update
instructions, and `docs/release-signing.md` for the signing workflow.

## License

CircuitOS is open source under the [MIT License](LICENSE).

## Code Signing

CircuitOS 1.0 ships **unsigned**, so a fresh install shows Windows SmartScreen's "unknown publisher"
warning. This is expected: `docs/installation-and-updates.md` documents the **More info → Run anyway**
click path, and every release publishes a SHA-256 checksum so the downloaded file can be verified against
the build. Never disable antivirus to work around the warning.

The signing pipeline is built and proven end-to-end and stays in the repo; the day a code-signing
certificate exists, signing is a thumbprint swap with no code changes. See `docs/release-signing.md`.

## Privacy Policy

CircuitOS collects no telemetry and never transfers data to the developer. The application only
makes the network connections the streamer sets up themselves:

- **Twitch** — after an explicit one-click login, CircuitOS talks to the Twitch API on the
  streamer's behalf (channel-point rewards, chat commands, pull announcements). OAuth tokens are
  stored encrypted on the local machine and can be disconnected at any time.
- **Optional self-hosted cloud backend** — only if the streamer configures their own Appwrite
  instance in Settings does profile data sync to that user-controlled server. Local storage is
  the default.
- **Update checks** — the built-in updater reads this repository's public GitHub Releases feed.

Everything else — catalogs, viewer inventories, branding, settings — stays on the local machine.
