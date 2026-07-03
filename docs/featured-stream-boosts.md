# Featured Stream Boosts

CircuitOS reads `featured-boost.json` from the active `Data` folder when applying
a redemption.

Normal odds remain active when the file is missing or `enabled` is `false`.

## Enable Quantum Boost

```json
{
  "enabled": true,
  "displayName": "Quantum Boost",
  "collectionMultipliers": {
    "quantum": 3.0
  }
}
```

With the current base weights, a `3.0` Quantum multiplier changes its effective
chance from 5% to approximately 13.64%.

Set `enabled` back to `false` after the featured stream. Multiple collection
multipliers can be configured in the same object.

An enabled boost with an unknown collection, missing display name, missing
multipliers, or a multiplier at or below zero will stop the redemption and log
the configuration error. Inventory data will not be changed.
