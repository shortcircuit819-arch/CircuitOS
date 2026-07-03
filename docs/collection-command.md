# Collection Command

`!collection` is answered natively once you connect Twitch. Name the command in
the Game Profile; CircuitOS handles the rest.

Examples:

- `!collection basic`
- `!collection quantum`
- `!collection Broken Collection`

It resolves both catalog keys and display names.

The command reports:

- Unique progress for the requested collection
- Current or previously recorded completion status
- Completion date when available
- Owned parts
- Missing parts
- Duplicate quantities

The command reads `components.json` dynamically and does not write to
`inventory.json`. Collections added to the catalog become available
automatically.
