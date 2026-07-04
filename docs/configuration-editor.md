# CircuitOS Configuration Editor

CircuitOS Control Core is a local-only desktop interface for managing a live
collection-game profile. Circuit Components is the default profile.

## Launch

Double-click:

`tools\admin\start-admin.vbs`

The silent launcher starts the self-contained .NET CircuitOS application. The
Control Core opens in its own window without a command prompt or browser tab.
Close the CircuitOS window to stop its private local server.

## Editable Configuration

- Permanent collection names, keys, weights, and salvage values
- Component IDs and display names
- Per-collection item variants (label and roll chance) and per-item variant
  display
- Per-collection rarity tiers (label and weight), per-item tier assignment, and
  bulk tier assignment (assign-all / assign-unassigned)
- Rare pull labels
- Event enable state and local start/end times
- Featured boost name, state, and collection multipliers
- New permanent collections and events
- Bulk names/CSV collection import with generated IDs and validation preview
- Collapsible collection summaries, cross-catalog item search, and bounded item scrolling
- Event deletion with confirmation
- Scrap circulation, average and median balances
- Duplicate pressure and unclaimed Scrap by collection
- Top Scrap balances
- Searchable read-only viewer collection details
- Rate Lab with expected rates, tier-aware simulated redeem distribution,
  per-part odds, a rarity-tier breakdown panel, and exact three-identical-pulls
  odds
- Discord-ready patch note drafts generated from editor changes
- Discord role award queue driven by recorded collection completions
- Backup history, validation previews, downloads, comparisons, and recovery
- Versioned branding, terminology, and live theme configuration
- One-click Twitch connect that creates the channel-point reward and goes live

The overview calculates effective pull rates using active event windows and the
current featured boost.

The Collection Importer supports names-only lists and CSV files (`id,name` with
an optional `tier` column). It can create a permanent collection or append to an
existing one, and it never saves automatically. See
`docs/collection-importer.md` for formats and preview rules.

Large catalogs are rendered on demand. Collection item inputs are created only
when a collection is expanded, and Viewer Inspector item rows are created only
when that viewer collection is opened. This keeps navigation responsive without
changing the stored catalog or inventory format.

The Rate Lab uses the values currently shown in the editor, including unsaved
changes. Simulations are limited to 100,000 redeems and never read or modify a
viewer inventory. When a collection defines rarity tiers, both the breakdown
panel and the simulation weight items by their tier.

## Patch Notes

The Patch Notes screen compares the current editor values with the configuration
loaded at the start of the session. It summarizes collection, component, rate,
Scrap, event, and featured boost changes in Discord Markdown. Saving does not
clear the draft. After posting it, use **Mark Published** to make the current
editor values the starting point for the next update.

Discord messages are limited to 2,000 characters. The character counter turns
amber when a draft needs to be shortened or split into multiple posts.

## Discord Role Awards

The Discord Roles screen reads one-time collection completion dates from
`inventory.json`. Each completion appears as a pending task until **Mark
Assigned** is used. The app records that acknowledgement in
`discord-role-awards.json`; it does not edit the viewer inventory or assign the
Discord role automatically.

Role names are configurable per collection and should exactly match the names
used in the Discord server. Assignment history includes an Undo control for an
accidental acknowledgement. The server validates every acknowledgement against
a real recorded completion before saving it.

## Backup & Recovery

The Backups screen manages timestamped recovery points for `components.json`,
`featured-boost.json`, and `discord-role-awards.json`. Select a backup to see a
summary of how it differs from the live file, validation results, and the stored
JSON before taking action.

Backups can be downloaded for off-machine archiving. Restore validates the
selected JSON and its references against related live configuration. Immediately
before replacement, the current live file is saved as another timestamped
backup. Backup filenames are resolved only inside the managed backup directory.

Viewer `inventory.json` files are intentionally excluded from listing, preview,
download, and restore operations.

## Game Profile and Setup

The Game Profile (formerly "Branding") is stored in `system-profile.json`. The
profile controls the game, administrator, redemption, item, collection, and
currency names plus the core interface colors and chat command names. Stable
data keys remain unchanged. CircuitOS platform attribution remains fixed outside
the editable profile.

When no live profile exists, the editor opens the Game Profile screen as a
first-run step. After saving the profile, connect your Twitch account from the
Twitch screen to create the channel-point reward and go live. See
`docs/installation-and-updates.md` for the complete workflow.

## Message Templates

The Messages screen edits 13 viewer-facing chat templates for pulls (including
the optional variant-pull message), completions, inventory, balance, duplicates,
collection details, and salvage. Each template has a whitelist of supported
placeholders, a rendered sample, 450-character validation, an individual Reset
control, and Reset All Messages. The variant-pull message is optional — leaving
it blank disables that extra line.

Messages are stored inside `system-profile.json` and use the profile backup and
recovery path. Native Twitch applies saved messages immediately — the next pull
or command uses them, with nothing to reinstall. Standardized error messages
remain non-editable for troubleshooting consistency.

## Safety

- The server binds only to `127.0.0.1`.
- The editor writes live `components.json`, `featured-boost.json`, the separate
  `discord-role-awards.json` acknowledgement file, and `system-profile.json`.
- `inventory.json` is read to calculate sanitized analytics and viewer details.
  It is also read to validate role eligibility. There is no inventory write
  endpoint, editor control, or save payload for it.
- Client and server validation reject invalid IDs, duplicate component IDs,
  negative weights, invalid salvage values, bad event windows, and broken boost
  references.
- Every live save creates timestamped files in the active profile's
  `config-backups` folder before atomic replacement.
- Discord role acknowledgement updates use the same atomic replacement and
  timestamped backup process.
- Recovery creates a new backup of the outgoing live file before restoring the
  selected version.
- Import loads configuration into the editor for review; it does not save
  automatically.
- Export downloads a combined configuration snapshot.

The editor updates the live data folder only. Use Export when a configuration
snapshot should also be archived elsewhere or copied back into project source.
