# Catalog Commands

`streamerbot-actions/StreamerbotCatalogCommands.txt` replaces the three older
standalone source files for:

- `!components`
- `!missing`
- `!dupes`
- `!leaderboard`

Create one Streamer.bot C# action from this source and point all three command
triggers to that action. The action reads the triggering command from the
standard `command` argument, with `commandName` as a fallback.

## Event Behavior

- `!components` includes all permanent collections, active events, and ended
  events for which the viewer owns items or has a completion record.
- `!missing` includes permanent collections and active events. It does not tell
  viewers to chase unavailable event parts.
- `!dupes` searches every catalog collection, including ended events.
- `!leaderboard` ranks unique permanent progress by default. Supplying a
  collection key or display name ranks that collection, including events.

Long output is split into messages below 440 characters. All three modes are
read-only and avoid Newtonsoft.

The older `StreamerbotCheck.txt`, `StreamerbotMissing.txt`, and
`StreamerbotDupes.txt` files remain as historical standalone versions, but they
do not support event collections.
