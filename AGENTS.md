# Agent Instructions

CircuitOS is a configurable Twitch collection-game platform with native Twitch
integration — no code to paste. Circuit Components (electronics-themed) is the
included default profile. This file is a quick orientation for AI agents; the
detailed, continuously updated source of truth is **`HANDOFF.md`** — read it first.

## Project Layout

- Repo root: `C:\Dev\CircuitStreamSystem`
- Live data: `C:\Users\nicho\Documents\CircuitOS\Data` (profiles under
  `Data\profiles\<id>`; active profile `circuit-components`)
- `tools/runtime/` — .NET 9 WinForms + WebView2 app (HTTP server on
  `127.0.0.1:8787`)
- `tools/admin/` — vanilla-JS admin panel (no framework)
- `docs/` — user and maintainer docs, including `docs/patch-notes/`

## Current State

- Released application version: **0.7.2** (native Twitch; Streamer.bot integration retired)
- Current source milestone: **0.7 — Native Twitch + Cloud Foundation** (shipped)
- Next milestone: **0.8 — Design & Identity**

See `README.md` for the feature list and full roadmap, and `docs/versioning.md`
for the versioning scheme.

## Coding Preferences

- Avoid overengineering — prefer simple, reliable code.
- Never overwrite `inventory.json` without creating a timestamped backup first;
  keep inventory writes locked, validated, and atomic.
- Keep the administration API bound to `127.0.0.1`.

## Release Discipline

Every release updates the application version in four files (see
`HANDOFF.md` → Version String Locations), ships a `docs/patch-notes/` entry, and
adds a `HANDOFF.md` session-log entry. Update packages stay data-free.
