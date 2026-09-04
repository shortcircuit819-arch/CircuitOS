# CircuitOS Installation and Updates

## Installing (recommended)

Download **CircuitOS-win-Setup.exe** from the [latest release](https://github.com/shortcircuit819-arch/CircuitOS/releases/latest) or [itch.io](https://shortcircuit819.itch.io/circuitos). Run it and complete setup. The installer enables in-app updates.

Requirements: Windows x64 and Microsoft Edge WebView2. The .NET runtime is bundled. Live redemptions require channel-point rewards to be available on your Twitch channel.

### Verify your download

Current releases are unsigned. Windows may show an unknown-publisher or SmartScreen warning. Compare your file with the SHA-256 checksum in the matching GitHub release notes:

```powershell
Get-FileHash .\CircuitOS-win-Setup.exe -Algorithm SHA256
```

A matching checksum confirms your file matches the published asset; it does not establish that software is risk-free. If you choose to run the verified file and Windows offers the option, select **More info → Run anyway**. Do not disable antivirus. Report unexpected detections with the vendor and detection name.

### Where things live

| What | Where |
|---|---|
| The program (replaced each update) | `%LocalAppData%\CircuitOS\current` |
| **Your data** (kept across updates) | **`%LocalAppData%\CircuitOS\Data`** |

Your data — collections, inventories, profiles, backups, settings — is deliberately kept **outside** the
versioned program folder. An update swaps the `current` folder; your `Data` sits beside it, untouched.
Keep a backup before updating: storage separation protects saves from application replacement but does not replace recovery precautions. On first launch the app seeds `Data` with a starter
catalog so the setup wizard has something to work with. (Settings → About → *Open data folder* jumps
straight there.)

## Updating

**Settings → About → Check for updates.** If a newer version exists you'll see the version change and a
**Download & Restart** button. That's it.

If the panel says this copy *isn't managed by the updater*, you're running a ZIP copy or a dev build —
use the installer for managed updates. Before switching from a ZIP or custom data folder, back it up and note its path; do not assume the installed copy will select the same folder.

If checking reports a network/fetch error, the release feed isn't reachable — that's the feed, not your
installation.

## Alternate: portable / ZIP install

The current release provides **CircuitOS-win-Portable.zip**. Extract it into a new folder and launch CircuitOS.exe. Portable copies do not use the managed installer update flow.

Before replacing a portable copy, close CircuitOS, back up its data, and note the location shown by **Settings → About → Open data folder**. Extract the newer release into a separate folder. CircuitOS discovers an existing Data folder near the executable when present; otherwise it falls back to %LocalAppData%\CircuitOS\Data. A custom --data launch option overrides discovery. A portable executable does not necessarily mean saves live beside it.

Old CircuitOS-Update-&lt;version&gt;.zip instructions belong to the legacy packaging workflow; those archives are not shipped with 1.0.2.

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

Run your configured inventory and balance commands (`!components` and `!scrap` in the starter profile), then one test redemption. Confirm `inventory.json` and
`overlay\overlay-state.json` update inside your data folder.

## Recovery

Every managed config file — catalog, profile, boost, and Discord role acknowledgements — is saved with
validation, atomic replacement, and a timestamped backup. Restore any of them from the **Backup &
Recovery Center**. Backups are pruned to the most recent N (Settings → backup retention; default 30).

**Your inventory is never overwritten without a timestamped backup first.**

## Building a release (maintainers)

```powershell
# Build current unsigned installer, portable ZIP, and update feed locally
tools\package\Build-CircuitOSVelopack.ps1
```

See [release signing](release-signing.md) and [versioning](versioning.md) before publishing.
