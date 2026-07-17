# CircuitOS Installation and Updates

## Installing (recommended)

Run **`CircuitOS-win-Setup.exe`**.

It installs for your user only — no admin prompt — and creates Desktop and Start Menu shortcuts. This is
the recommended way to install, because it's what enables **automatic updates**.

If Windows shows *"Windows protected your PC — unknown publisher"*, the release you have is unsigned.
Signed public releases don't show this. Never disable your antivirus to run it.

### Where things live

| What | Where |
|---|---|
| The program (replaced each update) | `%LocalAppData%\CircuitOS\current` |
| **Your data** (kept across updates) | **`%LocalAppData%\CircuitOS\Data`** |

Your data — collections, inventories, profiles, backups, settings — is deliberately kept **outside** the
versioned program folder. An update swaps the `current` folder; your `Data` sits beside it, untouched.
**An update can never touch a viewer's collection.** On first launch the app seeds `Data` with a starter
catalog so the setup wizard has something to work with. (Settings → About → *Open data folder* jumps
straight there.)

## Updating

**Settings → About → Check for updates.** If a newer version exists you'll see the version change and a
**Download & Restart** button. That's it.

If the panel says this copy *isn't managed by the updater*, you're running a ZIP copy or a dev build —
run the latest `Setup.exe` once and you'll be on the updater from then on. Your data carries over
untouched.

If checking reports a network/fetch error, the release feed isn't reachable — that's the feed, not your
installation.

## Alternate: portable / ZIP install

`CircuitOS-Windows-x64.zip` is a self-contained folder you unpack yourself. Launch the top-level
`CircuitOS.exe`; it finds the adjacent `App` and `Data` folders automatically. A ZIP copy does **not**
auto-update — to update it, close CircuitOS and copy the contents of the matching
`CircuitOS-Update-<version>.zip` over your installed folder. That archive has no `Data` directory, so
your catalog, profiles, inventory, boosts, role state, and backups are never replaced. `version.json`
records the application and data-schema versions.

`CircuitOS.exe` is a self-contained Windows x64 application — no .NET runtime, SDK, or PowerShell policy
changes needed. It uses the Microsoft Edge WebView2 runtime included with current Windows.

## First run

On first launch (no `system-profile.json`) the setup wizard runs once. It configures:

- Game, administrator, and redemption names
- Item, collection, and currency terminology
- Your chat commands (inventory, missing, duplicates, leaderboard, balance, collection, salvage)
- Your **theme and accent color** (six themes including a light one; fine-tune later in
  **Appearance → Design Mode**)

Fresh installs intentionally omit `Data\system-profile.json` so the wizard runs. Updates never restart
first-run setup.

`data\system-profile.template.json` documents the version 1 profile schema.

## Go live on Twitch

Open **Twitch** and connect your account. The one-time login needs no developer account or config files.
CircuitOS creates and manages the channel-point reward, then handles redemptions, chat commands, and
pull announcements directly through EventSub.

Optionally connect a **bot chat account** on the same page so replies post from your bot instead of your
channel account.

## Verification

Run `!components`, `!scrap`, and one test redemption. Confirm `inventory.json` and
`overlay\overlay-state.json` update inside your data folder.

## Recovery

Every managed config file — catalog, profile, boost, and Discord role acknowledgements — is saved with
validation, atomic replacement, and a timestamped backup. Restore any of them from the **Backup &
Recovery Center**. Backups are pruned to the most recent N (Settings → backup retention; default 30).

**Your inventory is never overwritten without a timestamped backup first.**

## Building a release (maintainers)

```powershell
# Installer + update feed (+ signing, + optional upload) — the modern path
tools\package\Build-CircuitOSVelopack.ps1 -CertificateThumbprint <THUMBPRINT> -Upload

# Legacy ZIP packages
tools\package\Build-CircuitOSPackage.ps1
```

See `docs\release-signing.md` for signing and `docs\updater-velopack-plan.md` for the updater design.
