# Collection Importer

The Collection Importer adds many items to the CircuitOS editor without
requiring IDs to be typed by hand. Open **Collections** and select **Import
Components**. (Event Collections has the same importer via its **Import
Components** button.)

## Destinations

- **Create a new collection** generates a unique collection key and lets you
  set its initial pull weight and salvage value.
- **Add to an existing collection** appends items to a permanent collection.

The importer applies changes to the editor only. **Save Catalog** still
requires confirmation, validates the complete catalog, creates timestamped
configuration backups, and then performs the atomic live write.

## Input Formats

Names-only input uses one item per line:

```text
Pikachu
Raichu
Pichu
```

CSV input accepts `id,name` with or without a header:

```csv
id,name
base_pikachu,Pikachu
base_raichu,Raichu
base_mr_mime,"Mr. Mime"
```

When the header row includes a `tier` column, each item's tier is imported with
it. This is the fast path for assigning rarity tiers to a large catalog:

```csv
id,name,tier
base_pikachu,Pikachu,common
base_charizard,Charizard,rare
base_mewtwo,Mewtwo,ultra_rare
```

The `tier` value must match a tier ID defined on the destination collection.
When importing into an existing collection, an unrecognized tier ID is flagged
as a review warning (the item still imports — fix it in the editor or define the
tier). When creating a new collection, tier values are carried through and take
effect once matching tiers are defined. Items without a tier value import
unassigned; you can fill them in later with the bulk-assign toolbar.

Tab-delimited files are also accepted. Auto Detect chooses CSV when the input
contains a comma or tab; otherwise it treats the input as names-only.

## Preview Rules

- Missing IDs are generated from the collection key and display name.
- Generated ID collisions receive a numeric suffix.
- Supplied IDs are normalized to lowercase letters, numbers, and underscores.
- Duplicate supplied IDs, duplicate names, and conflicts with the current
  catalog block the Apply button.
- Normalized or adjusted IDs are shown as review warnings before import.

Always inspect the preview. Applying an import does not remove or overwrite
existing items.

## Large Catalog Navigation

The Collections screen starts with compact collection summaries instead of
rendering every item editor at once. Use the collection or item search to find
names and IDs across the catalog, then open only the collection that needs work.
Expanded item lists scroll inside the collection card so Save Catalog and
the rest of the catalog remain close by. Expand All and Collapse All are
available when reviewing several collections together.
