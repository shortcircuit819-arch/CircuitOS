# Agent Instructions

CircuitOS is a configurable Twitch collection-game platform powered by
Streamer.bot. Circuit Components (electronics-themed) is the included default
profile. This file is a quick orientation for AI agents; the detailed,
continuously updated source of truth is **`HANDOFF.md`** — read it first.

## Project Layout

- Repo root: `C:\Dev\CircuitStreamSystem`
- Live data: `C:\Users\nicho\Documents\CircuitOS\Data` (profiles under
  `Data\profiles\<id>`; active profile `circuit-components`)
- `tools/runtime/` — .NET 9 WinForms + WebView2 app (HTTP server on
  `127.0.0.1:8787`)
- `tools/admin/` — vanilla-JS admin panel (no framework)
- `streamerbot-actions/` — paste-ready Streamer.bot C# source (`.txt`)
- `docs/` — user and maintainer docs, including `docs/patch-notes/`

## Current State

- Released application version: **0.6.0.8**
- Current source milestone: **0.7 — Cloud Platform + Twitch Integration** (unreleased)
- Current working focus: verify/polish the desktop-on-cloud bridge, native Twitch flow,
  and the manually updated `UI.md` launch punch list before packaging a 0.7 build.

See `README.md` for the feature list and full roadmap, and `docs/versioning.md`
for the versioning scheme.

## Coding Preferences

- Keep Streamer.bot C# code paste-ready and easy to troubleshoot.
- The maintained Redemption action uses `Newtonsoft.Json`; the command actions
  intentionally avoid it. Match the existing action's dependencies.
- Avoid overengineering — prefer simple, reliable code.
- Never overwrite `inventory.json` without creating a timestamped backup first;
  keep inventory writes locked, validated, and atomic.
- Keep the administration API bound to `127.0.0.1`.

## Release Discipline

Every release updates the application version in five files (see
`HANDOFF.md` → Version String Locations), ships a `docs/patch-notes/` entry, and
adds a `HANDOFF.md` session-log entry. Update packages stay data-free.
