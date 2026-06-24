# Rare Pull Messages

The redemption action sends an additional odds message for Broken and Quantum
parts. The calculation uses the active collection weights, featured boost
multipliers, and number of parts in the selected collection.

Each viewer also receives a small inventory metadata object:

```json
"pullStreak": {
  "partId": "quantum_resistor",
  "count": 3,
  "sequenceProbability": 0.0000005787037037
}
```

When the same exact part is pulled three consecutive times, the action sends a
`TRIPLE MATCH` message. It fires when the count reaches three, not again on the
fourth or later matching pull. Pulling a different part resets the count to one.

The stored sequence probability is multiplied once per pull. This keeps the
reported odds accurate when a featured boost changes during a streak.

`rareLabel` in `components.json` controls the rare message label. Broken and
Quantum also have code fallbacks so older live catalogs still receive messages.
