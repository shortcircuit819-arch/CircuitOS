# CircuitOS maintainer guide

For small UI and documentation fixes. Verify your branch before editing: 1.1 achievements and 1.2 Economy Assistant are separate development lines. Read the local AGENTS.md and HANDOFF.md when available, then [CONTRIBUTING.md](../CONTRIBUTING.md).

## Files and scope

- `tools/admin/index.html`, `styles.css`, `app.js`: vanilla frontend.
- `tools/runtime`: .NET Windows runtime, local API and game engines.
- `overlays/lower-quarter`: OBS reveal.
- `tools/package/Build-CircuitOSVelopack.ps1`: current installer/update packaging.

Text, spacing and isolated UI fixes may be small patches. Inventory writes, saved-data schemas, odds, rewards, auth, migrations and restores require a focused implementation and safety review. Keep backup-before-write, locking, atomicity and input validation intact.

## Run against disposable data

From the selected checkout, create a fresh fixture from the bundled starter files. Do not reuse an existing directory or copy live viewer data:

```powershell
$fixtureRoot = Join-Path $env:TEMP ('CircuitOS-manual-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
Copy-Item data/components.json,data/featured-boost.json,data/inventory.json -Destination $fixtureRoot
Copy-Item data/system-profile.template.json -Destination (Join-Path $fixtureRoot 'system-profile.json')
Copy-Item data/overlay-config.template.json -Destination (Join-Path $fixtureRoot 'overlay-config.json')
dotnet run --project tools/runtime/CircuitOS.Runtime.csproj -c Debug -- `
  --headless --data $fixtureRoot --ui tools/admin --overlay overlays/lower-quarter --port 8810
```

Open `http://127.0.0.1:8810/`. Stop the process when done; remove only the exact fixture directory you created after verifying its resolved path. The installed app's live data must never be used for manual tests.

## Verify the change

Run the relevant commands in CONTRIBUTING.md. For UI edits, check the affected screen and neighboring navigation at normal and narrow widths, keyboard behavior and browser errors. In the 1.2 line, verify catalog autosave and Undo; other editors retain explicit saves. Do not assume the controls on different development branches match.

The core integration flow is redemption -> pull -> inventory -> chat/overlay. Synthetic checks do not prove actual Twitch or OBS integration; state the distinction in the result. Do not put viewer identities, credentials or local personal paths into public screenshots.

## Prepare a release only when authorized

Follow [versioning](versioning.md) and [release signing](release-signing.md). Synchronize the runtime project version, health version, module-export version and README, and add actual patch notes. Documentation-only work does not need a version bump.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/package/Build-CircuitOSVelopack.ps1
```

Without `-Upload` this builds locally. Without a configured certificate/signing template the output is unsigned. Do not publish until the owner approves the candidate. Validate installer, portable ZIP, update feed and SHA-256 checksums; packages must contain no user saves or credentials. Current distribution is the Velopack installer/portable package, not the legacy single-executable ZIP workflow.

Keep a frozen candidate outside rolling `dist/` output when testing staggered releases. Deleted local packaging history may need to be restored from verified released packages before building deltas; do not assume an old delta base remains on disk.
