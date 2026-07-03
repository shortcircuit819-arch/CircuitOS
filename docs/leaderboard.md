# Collection Leaderboard

`!leaderboard` is answered natively once you connect Twitch — the same command
family as `!components`, `!missing`, and `!dupes`.

## Usage

- `!leaderboard` ranks unique permanent components out of 25.
- `!leaderboard quantum` ranks Quantum Collection progress out of 6.
- `!leaderboard <event key>` ranks progress for that event, even after it ends.

The command shows the top five. A caller outside the top five also receives
their personal rank.

Duplicate quantities do not increase rank. Overall ties use the number of
currently complete permanent collections, followed by display name for stable
ordering. Collection-specific ties use display name.

The command is read-only and loads collection sizes and IDs from
`components.json`.
