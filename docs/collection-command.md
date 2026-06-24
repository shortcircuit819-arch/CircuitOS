# Collection Command

Create a Streamer.bot command trigger for `!collection` and run the C# action in
`streamerbot-actions/StreamerbotCollection.txt`.

Examples:

- `!collection basic`
- `!collection quantum`
- `!collection Broken Collection`

The action reads the command value from `rawInput`, with fallbacks for `input0`
and `argument0`. It resolves both catalog keys and display names.

The command reports:

- Unique progress for the requested collection
- Current or previously recorded completion status
- Completion date when available
- Owned parts
- Missing parts
- Duplicate quantities

The command reads `components.json` dynamically and does not write to
`inventory.json`. Collections added to the catalog become available without
adding another hard-coded command branch.
