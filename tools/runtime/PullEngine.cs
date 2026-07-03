using System.Globalization;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Shared pull/roll logic — the SINGLE source of truth for selecting an item from a
// collection, with dup protection, tier weighting, and variant rolling. Pure over the
// catalog JSON; the RNG is injected so tests are deterministic.
//
// NOTE: one injected RNG drives both the tier roll and the variant rolls. The draws are
// still independent, so the distribution is unaffected; a single seed just makes tests
// reproducible.
internal sealed record PullOutcome(
    string PartId,
    string PartName,
    string DisplayPartName,            // variant prefix + base name, e.g. "SHINY Capacitor"
    IReadOnlyList<string> VariantLabels,
    string TierLabel,                  // "" when the pull was not tier-selected
    double Probability);               // effective per-pull probability (for odds display)

internal static class PullEngine
{
    // Rolls an item from `collection` (a catalog collection with "parts", optional
    // "tiers", optional "variants").
    //   collectionProbability — the collection's effective pull probability
    //   ownedCounts           — partId -> count already owned (for dup protection); may be empty
    //   dupProtectionTurns    — 0 disables dup protection
    //   pullsSinceLastDup     — dup-protection counter for this viewer
    // Returns null only when the collection has no usable part.
    public static PullOutcome? Roll(
        JsonObject collection,
        double collectionProbability,
        IReadOnlyDictionary<string, int>? ownedCounts,
        int dupProtectionTurns,
        int pullsSinceLastDup,
        Random rng)
    {
        if (collection["parts"] is not JsonArray parts || parts.Count == 0) return null;

        var eligible = new List<JsonObject>();
        foreach (var node in parts) if (node is JsonObject p) eligible.Add(p);
        if (eligible.Count == 0) return null;

        // 1. Dup protection — restrict to unowned parts while protection is active.
        if (dupProtectionTurns > 0 && pullsSinceLastDup < dupProtectionTurns)
        {
            var unowned = eligible
                .Where(p =>
                {
                    var id = p["id"]?.ToString();
                    return !string.IsNullOrEmpty(id) && CountOf(ownedCounts, id) == 0;
                })
                .ToList();
            if (unowned.Count > 0) eligible = unowned;
        }

        // 2. Tier-weighted selection, with equal-odds fallback.
        JsonObject selected;
        double probability;
        string tierLabel = "";
        if (collection["tiers"] is JsonArray { Count: > 0 } tiers
            && TrySelectByTier(tiers, eligible, collectionProbability, rng, out selected, out probability, out tierLabel))
        {
            // selected within a tier
        }
        else
        {
            selected = eligible[rng.Next(eligible.Count)];
            probability = collectionProbability / eligible.Count;
        }

        var partId = selected["id"]?.ToString() ?? "";
        var partName = selected["name"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(partId) || string.IsNullOrWhiteSpace(partName)) return null;

        // 3. Variant rolling — independent, cap 2, no duplicate labels.
        var variantLabels = RollVariants(collection["variants"] as JsonArray, rng);
        var prefix = variantLabels.Count > 0 ? string.Join(" ", variantLabels) + " " : "";

        return new PullOutcome(partId, partName, prefix + partName, variantLabels, tierLabel, probability);
    }

    private static int CountOf(IReadOnlyDictionary<string, int>? owned, string id)
        => owned is not null && owned.TryGetValue(id, out var count) ? count : 0;

    private static bool TrySelectByTier(
        JsonArray tiers, List<JsonObject> eligible, double collectionProbability, Random rng,
        out JsonObject selected, out double probability, out string tierLabel)
    {
        selected = null!;
        probability = 0;
        tierLabel = "";

        // Group eligible parts by tier, skipping empty groups and non-positive weights.
        var groups = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        double totalWeight = 0;
        foreach (var node in tiers)
        {
            if (node is not JsonObject tier) continue;
            var tid = tier["id"]?.ToString() ?? "";
            var weight = ToDouble(tier["weight"]);
            if (string.IsNullOrEmpty(tid) || weight <= 0) continue;
            var group = eligible.Where(p => p["tier"]?.ToString() == tid).ToList();
            if (group.Count == 0) continue;
            groups[tid] = group;
            weights[tid] = weight;
            labels[tid] = tier["label"]?.ToString() ?? "";
            totalWeight += weight;
        }
        if (groups.Count == 0 || totalWeight <= 0) return false;

        // Roll a tier proportionally, then a random item within it.
        var roll = rng.NextDouble() * totalWeight;
        double cumulative = 0;
        string chosen = "";
        double chosenWeight = 0;
        foreach (var kv in weights)
        {
            cumulative += kv.Value;
            if (roll < cumulative || chosen == "")
            {
                chosen = kv.Key;
                chosenWeight = kv.Value;
                if (roll < cumulative) break;
            }
        }

        var chosenGroup = groups[chosen];
        selected = chosenGroup[rng.Next(chosenGroup.Count)];
        probability = collectionProbability * (chosenWeight / totalWeight) / chosenGroup.Count;
        tierLabel = labels[chosen];
        return true;
    }

    private static List<string> RollVariants(JsonArray? variants, Random rng)
    {
        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (variants is null) return labels;
        foreach (var node in variants)
        {
            if (labels.Count >= 2) break;
            if (node is not JsonObject variant) continue;
            var label = (variant["label"]?.ToString() ?? "").Trim();
            var chance = ToDouble(variant["chance"]);
            if (string.IsNullOrWhiteSpace(label) || chance <= 0) continue;
            if (!seen.Contains(label) && rng.NextDouble() < chance)
            {
                seen.Add(label);
                labels.Add(label);
            }
        }
        return labels;
    }

    private static double ToDouble(JsonNode? node)
        => node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}

