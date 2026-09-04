# Release history

These historical milestones describe the project when each shipped. Use the [README](../README.md) for current behavior.



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


## Current release

[1.0.2 patch notes](patch-notes/1.0.2.md) · [All releases](https://github.com/shortcircuit819-arch/CircuitOS/releases)
