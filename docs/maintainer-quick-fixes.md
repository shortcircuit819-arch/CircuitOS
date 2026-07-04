# CircuitOS Maintainer Guide: UI and Small Fixes

This guide covers low-risk CircuitOS maintenance when automated development help
is unavailable. It is intended for text changes, small layout adjustments,
simple browser-side behavior, documentation, and narrowly understood bug fixes.

## Know the Main Files

- `tools/admin/index.html`: page structure, sidebar, buttons, labels, and panels
- `tools/admin/styles.css`: layout, colors, spacing, scrolling, and responsive UI
- `tools/admin/app.js`: browser behavior, rendering, validation, and API calls
- `tools/runtime/`: the packaged Windows application's .NET source
- `tools/runtime.tests/Program.cs`: first-run and engine smoke tests
- `tools/package/Build-CircuitOSPackage.ps1`: validated release packaging
- `data/`: starter data only; never substitute a viewer's live inventory here

## Safe Changes

Usually safe for a `0.3.x` patch:

- Correcting labels, descriptions, help text, or documentation
- Adjusting CSS spacing, widths, colors, or scroll behavior
- Adding a button that only reads existing data or downloads a local report
- Fixing a clearly isolated browser-side display bug
- Improving validation without changing the saved JSON format

Stop and use a full development/test pass if a change affects:

- `inventory.json`, backups, locks, salvage balances, or completion tracking
- Catalog, profile, or inventory schema fields
- Collection weighting, pull odds, event eligibility, or boosts
- API request or response formats
- First-run setup, restores, or migrations

## Make a Working Copy First

Do not test against the live Circuit Components folder. From the project root,
create disposable data:

```powershell
$test = "tools\admin\.manual-test-data"
New-Item -ItemType Directory -Path $test -Force | Out-Null
Copy-Item data\components.json,data\featured-boost.json,data\inventory.json $test -Force
Copy-Item data\system-profile.template.json "$test\system-profile.json" -Force
```

The disposable inventory contains no live viewer data.

## Run the UI Locally

Start the .NET app in headless mode — it serves the API and the static UI
without opening the CircuitOS window:

```powershell
dotnet run --project tools\runtime\CircuitOS.Runtime.csproj -c Release -- `
  --headless `
  --data tools\admin\.manual-test-data `
  --ui tools\admin `
  --port 8810
```

Open `http://127.0.0.1:8810/` yourself. Keep the PowerShell window open while
testing. Press `Ctrl+C` when finished.

## UI Editing Loop

1. Change only the smallest relevant file.
2. Refresh the browser with `Ctrl+F5`.
3. Check the feature you changed and one neighboring screen.
4. Open browser developer tools with `F12` and check the Console for red errors.
5. Test at the normal window size and a narrower window.
6. Confirm sidebar and page scrolling still work.
7. Confirm Save buttons are unchanged unless saving is part of the fix.

Useful syntax check for `app.js` when Node.js is installed:

```powershell
node --check tools\admin\app.js
```

## Small JavaScript Fixes

Follow the existing patterns in `app.js`:

- Use `document.getElementById` for unique controls.
- Use the existing `element()` helper when rendering UI nodes.
- Use `showNotice(message, "success")` or `showNotice(message, "error")` for feedback.
- Update the relevant render function instead of directly editing unrelated DOM.
- Register a new button listener near the other listeners at the bottom.
- Do not place viewer identities or inventory contents in downloadable diagnostics.

If the browser reports `Cannot read properties of null`, the JavaScript is
looking for an HTML element that does not exist or whose ID does not match.

## Small CSS Fixes

- Prefer editing an existing class over adding inline styles.
- Test both long and short collection names.
- Avoid fixed heights for catalog lists unless the container scrolls.
- Keep buttons and form controls keyboard accessible.
- Check the existing responsive rules near the bottom of `styles.css`.
- Do not remove `min-width: 0` from grid or flex children without testing overflow.

## Version Rules

- Within a milestone, iterate the four-part version: a sub-feature bumps the
  third part (`0.6.1`), an iteration or hotfix bumps the fourth (`0.6.0.6`).
- A new major feature area advances the milestone (second part), e.g. `0.6` to
  `0.7` — only once the current milestone is fully satisfactory.
- Do not use `1.0` until the signed stable release.

For every application version bump, update all four locations:

1. `tools/runtime/CircuitOS.Runtime.csproj` Version, FileVersion, AssemblyVersion
2. `tools/runtime/Program.cs` health-response version
3. `tools/runtime/CircuitService.Modules.cs` `circuitosVersion`
4. `README.md` current application version

The package builder deliberately fails when executable and source versions do
not match.

## Build and Test

Install the official .NET 9 SDK from Microsoft first. From the project root:

```powershell
dotnet run --project tools\runtime.tests\CircuitOS.Runtime.SmokeTests.csproj `
  -c Release -- data
```

Expected result:

```text
Smoke tests passed: first run is safe, the pull + redemption + command engines behave, and the Appwrite + Twitch config loaders behave.
```

Publish the Windows executable:

```powershell
dotnet publish tools\runtime\CircuitOS.Runtime.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o tools\admin\runtime-next
```

Check the version before promotion:

```powershell
(Get-Item tools\admin\runtime-next\CircuitOS.exe).VersionInfo.ProductVersion
```

When correct, replace `tools/admin/runtime/CircuitOS.exe` with the tested file.

## Package the Release

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\package\Build-CircuitOSPackage.ps1
```

The builder validates version consistency, required UI, user-data boundaries,
checksum manifests, and ZIP creation. Do not distribute a
release if this command reports an error.

Release files:

- `dist/CircuitOS-Windows-x64.zip`: fresh installation
- `dist/CircuitOS-Update-<version>.zip`: update without a `Data` folder

## Final Manual Checklist

- Application opens directly from `CircuitOS.exe`.
- Version shown in CircuitOS matches the intended release.
- Overview, Profile Settings, Collections, Events, and Twitch open.
- Browser/developer console has no errors.
- Fresh ZIP has starter data but no `Data/system-profile.json`.
- Update ZIP has no `Data` folder.
- Existing live `inventory.json` was never used or modified during testing.
- Unsigned test releases are clearly identified as unsigned.

## Cleaning Up

After stopping the local test server, remove only the disposable folders you
created:

```powershell
Remove-Item tools\admin\.manual-test-data -Recurse -Force
Remove-Item tools\admin\runtime-next -Recurse -Force
```

Never run cleanup commands against the live Circuit Components data folder.
