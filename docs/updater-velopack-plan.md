# Installer + Auto-Updater plan (Velopack)

Chosen 2026-06-29: **Velopack** for both the installer and in-app auto-updates, with **GitHub
Releases** as the update feed. This is the actionable plan so the integration is fast once the repo
exists. Nothing here is built yet — it's gated on Step 0.

## Why this is low-risk for CircuitOS
- **User data is safe by construction.** Data lives in `Documents\CircuitOS\Data`, completely
  separate from the program folder. Every update only swaps program files — it can never touch a
  viewer's collection.
- The app is already a self-contained single-file publish, which Velopack packs cleanly.

## Step 0 — GitHub repo (gates everything; the user's call)
- Create the repo (name + public/private = user's decision). Then before first push:
  - `.gitattributes` is in place (done — line endings normalized).
  - Re-verify `.gitignore` covers every `*.local.json` + `circuitos-settings.json` (done).
  - `git remote add origin …` then push `main` + tags.
- Velopack publishes releases here (`vpk upload github`), and the app reads updates from the same repo.

## Step 1 — Velopack in the app
- Add the `Velopack` NuGet package to `CircuitOS.Runtime.csproj`.
- **Make `VelopackApp.Build().Run()` the very first statement in `Main`** — before `RuntimeOptions.Parse`,
  before any window/HTTP work. Velopack uses it to handle install/update/uninstall hooks and may exit
  early (e.g. during install). Getting this ordering wrong breaks the whole app, so it goes in first.
- Add an update service (thin wrapper around `UpdateManager` pointed at the GitHub source):
  `CheckForUpdate()` → `DownloadUpdates()` → `ApplyUpdatesAndRestart()`.
- Wire it to the Settings **About** panel's future "Check for updates" button (the note/placeholder is
  already there): show "Up to date" / "Update available: x.y.z → [notes]" / progress → "Restart to
  finish."
- Headless mode (`--headless`, the preview) must **skip** the updater — guard on `!headless`.

## Step 2 — Build with `vpk`
- Install the Velopack CLI: `dotnet tool install -g vpk`.
- After `dotnet publish` (existing self-contained single-file output), run
  `vpk pack --packId CircuitOS --packVersion <ver> --packDir <publish> --mainExe CircuitOS.exe`
  to produce the `Setup.exe`, full package, and delta packages.
- `vpk upload github --repoUrl <repo> --tag v<ver>` to publish the release + feed.
- Fold this into `Build-CircuitOSPackage.ps1` (or a sibling script) so a release is one command.
  Keep the current ZIP output too until Velopack is proven.

## Step 3 — Code signing (fast-follow)
- Unsigned `Setup.exe`/updates trip SmartScreen ("unknown publisher") — bad for non-technical
  streamers. Sign the packed output (Velopack has a `--signParams` hook).
- Options: a standard code-signing cert (~$100–200/yr) or Azure Trusted Signing (cheaper, newer).
- `Sign-CircuitOSRelease.ps1` already exists as a starting point.

## Things to know / gotchas
- **Install location:** Velopack installs **per-user** (`%LocalAppData%\CircuitOS`), not Program Files.
  That's deliberate — it's what lets it self-update with no admin prompt. Fine for this app.
- **WebView2:** Win11 ships it; Velopack can bundle/check the runtime if we ever target older Windows.
- **First cutover:** existing ZIP-install users won't auto-migrate to the Velopack install — they
  install the new `Setup.exe` once. Their data (in Documents) carries over untouched.
- **Testing:** cut a throwaway `v0.0.1` and `v0.0.2` to a test repo, install v1, confirm it detects and
  applies v2. Can't be fully tested until real releases exist — so land this once the feed is live.

## Sequence summary
Step 0 (user: repo) → Step 1 (app code) → Step 2 (vpk build) → cut a real release → Step 3 (signing).
