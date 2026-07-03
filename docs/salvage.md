# Duplicate Salvage

Salvage safely converts duplicate copies into Scrap. It never consumes the
viewer's final copy of a component.

## Commands

- `!salvage basic` salvages every extra Basic component.
- `!salvage quantum` salvages every extra Quantum component.
- `!salvage <event key>` salvages extras from an event collection.
- `!salvage all` explicitly salvages extras from every catalog collection.
- `!scrap` displays the viewer's current Scrap balance.

An empty `!salvage` command only shows usage. This prevents accidental
inventory-wide salvage.

## Values

| Collection | Scrap per extra copy |
| --- | ---: |
| Basic | 1 |
| Power | 2 |
| Advanced | 3 |
| Broken | 5 |
| Quantum | 10 |

Event collections define their own positive `salvageValue` in
`components.json`.

## Inventory Schema

The wallet is created only after a successful salvage:

```json
"wallet": {
  "scrap": 12
}
```

Salvage shares the redemption inventory lock. It validates the result, creates a
timestamped backup, and atomically replaces `inventory.json`. If validation or
saving fails, no live inventory changes are committed.

The five permanent values also have code fallbacks while the live catalog is
being upgraded. Event collections require an explicit value.
