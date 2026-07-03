# CircuitOS Installation and Updates

CircuitOS can run against an existing live data folder or as a portable starter
installation. Circuit Components is the included default game profile.

## Build The Recipient Package

Run from the project root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\package\Build-CircuitOSPackage.ps1
```

This creates:

- `dist\CircuitOS-Release`: inspectable recipient folder
- `dist\CircuitOS-Windows-x64.zip`: send-ready archive
- `dist\CircuitOS-Update-<version>.zip`: data-free update archive

The builder uses an explicit file list, creates a fresh empty inventory, omits
owner-specific paths, and places the OBS files directly under `Data\overlay`.
Recipients launch the package directly with its top-level `CircuitOS.exe`.

The builder reports the executable's Authenticode status. Public releases should
be signed with `tools\package\Sign-CircuitOSRelease.ps1` and a trusted
code-signing certificate before the ZIP is distributed. See
`docs\release-signing.md`.

For an existing installation, close CircuitOS and copy the contents of the
matching update ZIP into the installed CircuitOS folder. The update archive has
no `Data` directory, so catalog, profile, inventory, boosts, role state, and
backups are not replaced. `version.json` records the application and data-schema
versions.

The CircuitOS platform wordmark remains fixed in the administration sidebar and
local-engine status area. A system profile controls the separate Active Profile
identity, terminology, and theme, but cannot replace the CircuitOS attribution.

## Launch Modes

- `tools\admin\start-admin.vbs` keeps the existing Circuit Components live-data
  path used by the project owner.
- `tools\admin\start-circuitos.vbs` is retained for repository development.
- Distributed copies place `CircuitOS.exe` at the package root. It discovers
  the adjacent `App` and `Data` folders automatically.
- The `.cmd` launchers are retained as compatibility wrappers.

The portable starter inventory is empty. The electronics catalog and boost are
examples that can be replaced in the editor.

For release packaging, use `CircuitOS` as the ZIP root folder and keep the
`tools`, `data`, and selected `docs` directories in their existing relative
locations.

The published `CircuitOS.exe` is a self-contained Windows x64 application. A
recipient does not need PowerShell execution-policy changes, a .NET SDK, or a
separate .NET runtime. It uses the Microsoft Edge WebView2 Runtime included with
current Windows and Microsoft Edge installations. `CircuitAdmin.ps1` is kept in
the package only as an optional legacy fallback.

Self-contained does not mean trusted. An unsigned new executable can still
trigger reputation or antivirus warnings. Do not instruct recipients to disable
security software. Sign public releases and submit suspected false positives to
the security vendor.

## First Run

When `system-profile.json` is missing, the app opens the required first-run
wizard. Blank Collection is selected by default, while Circuit Components
remains available as an example preset. The wizard configures:

- Game, administrator, and redemption names
- Item, collection, and currency terminology
- Inventory, missing, duplicates, leaderboard, balance, collection, and salvage
  chat commands
- Background, panel, border, accent, text, and muted colors

Fresh release packages intentionally omit `Data\system-profile.json` so the
wizard runs once. Update packages contain no `Data` folder and therefore never
restart first-run setup for an existing installation.

Save System Profile to create the live profile. The app uses stable internal
catalog keys while presenting the configured terminology to administrators and
chat.

`data\system-profile.template.json` documents the version 1 profile schema.

## Go Live On Twitch

After saving the profile, open the **Twitch** section and connect your account.
The one-time login needs no developer account or config files. CircuitOS creates
and manages the channel-point reward, then handles redemptions, chat commands,
and pull announcements directly through EventSub — no code to paste.

## Verification

Run `!components`, `!scrap`, and one test redemption. Verify that
`inventory.json` and `overlay\overlay-state.json` update inside the data folder.

## Recovery

`system-profile.json` is included in the Backup & Recovery Center. Profile saves
and restores use the same validation, atomic replacement, and timestamped backup
rules as the catalog, boost, and Discord role acknowledgement files.
