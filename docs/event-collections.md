# Event Collections

Event collections live inside the top-level `collections` object in the live
`components.json` file. Start with `data/event-collection-template.json`, then
copy its collection entry into `collections` and replace the example key,
names, component IDs, dates, and weight.

## Required Metadata

```json
"event_example": {
  "displayName": "Example Event Collection",
  "type": "event",
  "enabled": false,
  "activeFromUtc": "2099-01-01T00:00:00Z",
  "activeUntilUtc": "2099-01-08T00:00:00Z",
  "weight": 10,
  "parts": []
}
```

- `type` must be `event`.
- `enabled` must be `true` before the event can appear in redemptions.
- `activeFromUtc` is inclusive.
- `activeUntilUtc` is exclusive.
- `weight` joins the active permanent and featured-boost weights.
- `salvageValue` sets the Scrap awarded for each consumed extra copy.
- Component IDs must remain globally unique and should begin with the event key.

Keep new events disabled while editing and validating them. Enabling an event
with missing or invalid UTC dates stops the redemption before inventory data is
changed. Disabled, upcoming, and ended events have no effect on pull odds.

The redemption action automatically supports event pulls, completion tracking,
featured boosts, rare labels, and streak odds. `!collection <event key>` reports
the event schedule and viewer progress. The older `!components`, `!missing`, and
`!dupes` commands still report permanent collections only.
