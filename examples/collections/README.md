# Starter collection packs

Three original, editable games for CircuitOS 1.0.2 or later. Each pack contains 15 items, four rarity tiers, one variant, and themed terminology and chat commands. They contain no viewer inventories, Twitch credentials, external images, or branding.

| Download | Theme | Inventory command | Variant |
| --- | --- | --- | --- |
| [Fantasy loot](fantasy-loot.circuitcollection) | Wayfarer's Cache: lanterns, enchanted curios, and legendary treasures | `!loot` | ENCHANTED |
| [Cozy cafe](cozy-cafe.circuitcollection) | Cloudberry Cafe: warm drinks, pastries, and celebration treats | `!cafe` | GOLDEN |
| [Space discoveries](space-discoveries.circuitcollection) | Quiet Orbit Discoveries: survey finds, cosmic artifacts, and distant wonders | `!orbit` | RADIANT |

## Import a pack

1. Open a download above and use GitHub's **Download raw file** button. Keep the `.circuitcollection` extension.
2. In CircuitOS, open **Profiles** and choose **Import Module / Pack**.
3. Select the file and confirm the new profile name.
4. Switch to the imported profile from **Profiles**. Review its catalog and game settings before going live.
5. Connect Twitch, configure a channel-point reward for this profile, and add the overlay to OBS using the setup guide. Importing content does not create a Twitch reward.

Import creates a separate profile with an empty inventory. It copies your current colors, branding, and overlay settings; it does not replace your existing profile. Imported profiles start inactive. Set the profile live when ready; switching the editor to it alone does not activate it.

[Installation and setup guide](../../docs/installation-and-updates.md)

## Odds and customization

Each pack has one permanent collection. Tier weights are **60 Common / 25 Uncommon / 12 Rare / 3 Legendary**, with 6 / 4 / 3 / 2 items respectively. With every item eligible, each common item has a 10% chance, uncommon 6.25%, rare 4%, and legendary 1.5%. Duplicate protection can change these effective odds by restricting eligible items.

The variant has an independent **5% chance** on any item. A variant is a special reveal label; it does not add another base item to the 15-item catalog. The collection's base salvage value is 1; review salvage tuning in your game settings before use.

The command prefix differs between packs so they can run together: `loot`, `cafe`, or `orbit`. Append `missing`, `dupes`, `leaders`, `balance`, `collection`, or `salvage` to that prefix for the other commands (for example, `!cafemissing`). Existing custom profiles may still conflict; CircuitOS checks command collisions when activating a profile.

Edit names, tiers, variants, and terminology to fit your channel. Add your own licensed artwork if desired; these packs use text and your existing overlay presentation. Content is provided under the repository's [MIT license](../../LICENSE), with AI-assisted writing. No third-party characters or franchises are included.

## Validation

All three files were imported through CircuitOS's real `ImportCollectionPack` service into isolated local test data. For each imported catalog, 10,000 seeded rolls through the runtime's `PullEngine` reached all 15 items and all four tiers and produced the configured variant. No live streamer data was used.
