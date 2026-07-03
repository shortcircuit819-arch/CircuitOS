# Catalog Commands

CircuitOS answers these chat commands natively once you connect Twitch — no setup
beyond naming them in the Game Profile:

- `!components`
- `!missing`
- `!dupes`
- `!leaderboard`

Each command reads the triggering word from chat, so renaming a command in the
Game Profile takes effect immediately.

## Event Behavior

- `!components` includes all permanent collections, active events, and ended
  events for which the viewer owns items or has a completion record.
- `!missing` includes permanent collections and active events. It does not tell
  viewers to chase unavailable event parts.
- `!dupes` searches every catalog collection, including ended events.
- `!leaderboard` ranks unique permanent progress by default. Supplying a
  collection key or display name ranks that collection, including events.

Long output is split into messages below 440 characters. All four commands are
read-only.
