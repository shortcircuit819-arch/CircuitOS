# Collection Packs

A **collection pack** (`.circuitcollection`) lets you share collections with another streamer. Unlike a
full profile export, a pack carries the *content and flavor* of your collections but adopts the
**importer's own theme** — so a shared collection looks like *their* game, not yours.

## Pack vs. module

|                         | Carries                                                             | Wears whose theme | Imports as    |
| ----------------------- | ------------------------------------------------------------------ | ----------------- | ------------- |
| **Collection pack** (`.circuitcollection`) | one or all collections + commands, messages, terminology, tuning | the **importer's** | a new profile |
| **Module** (`.circuitmodule`)              | the whole profile (every collection + the sharer's branding)     | the **sharer's**   | a new profile |

## Sharing

- **One collection:** on the **Collections** page, click **Share** on a collection card. It downloads a
  `.circuitcollection` file you can send to anyone.
- **All collections:** click **Share All** in the Collections toolbar to bundle every permanent
  collection into a single pack.

**What travels with a pack:** the collection(s) and their items, rarity tiers, variants, pull weights,
and salvage values; your chat commands and message templates; and your terminology (item and collection
names, currency name, redemption name).

**What does *not* travel:** your colors and branding (the importer keeps their own), viewer inventories,
and **event collections** — events are tied to your channel's schedule, so they never share (and the
Share button doesn't appear on event collections).

## Importing

1. On the **Profiles** page, click **Import Module / Pack** and choose the `.circuitcollection` file.
2. Name the new profile — it's pre-filled with the sharer's game name; change it if you like.
3. CircuitOS creates a new profile containing the shared collection(s), using **your** current theme.
4. Switch to it from the Profiles page.

If any of the pack's chat commands clash with a profile you already have **live**, CircuitOS asks you to
rename them before that profile can go live.

## Notes

- Importing never overwrites an existing profile — it always creates a new one, and de-duplicates the
  name (e.g. `Circuit Components (2)`) if the name is already taken.
- Packs are plain files you share directly (Discord, etc.) — no cloud or account required.
