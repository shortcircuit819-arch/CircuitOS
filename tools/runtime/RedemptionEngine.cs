using System.Globalization;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// The full redemption pipeline. PullEngine.Roll is only the INNER roll
// (dup-protection → tier → variant) over an already-chosen collection; RedemptionEngine adds
// the two surrounding pieces:
//
//   1. SelectCollection — weighted collection pick + featured-boost multipliers + event-window
//      gating.
//   2. ApplyRedemption  — the inventory read-modify-write: owned counts, completion detection
//      and seeding, pull-streak / triple-pull tracking, and the dup-protection counter.
//
// Pure over the catalog/inventory JSON (System.Text.Json.Nodes); the RNG is injected so tests
// are deterministic. Output formatting (chat templates, overlay state) and cooldown remain the
// caller's concern. Config errors throw InvalidDataException; the caller decides how to surface them.
//
// NOTE: the legacy weight/rare-label fallbacks below (basic/power/advanced/broken/quantum) are
// Circuit-Components parity shims for un-upgraded catalogs. Catalogs that set explicit weights
// and rareLabels never hit them.
internal sealed record CollectionSelection(
    string Key,
    JsonObject Collection,
    string DisplayName,
    double Probability,          // effective collection pull probability (weight / total)
    string ActiveBoostName);     // "" unless a featured boost applies to THIS collection

internal sealed record RedemptionResult(
    string CollectionKey,
    string CollectionName,
    string ActiveBoostName,
    PullOutcome Pull,            // inner-roll result (part, variants, tier, per-item odds)
    int OwnedAfter,
    int TotalParts,
    int Quantity,               // count of the pulled part after increment (>1 = duplicate)
    bool IsDuplicate,
    bool NewlyCompleted,
    int ConsecutivePullCount,   // identical-part streak length (3 = triple)
    double StreakSequenceProbability,
    string RareLabel);

internal static class RedemptionEngine
{
    // Runs one redemption end to end: pick a collection, roll an item, apply it to the viewer's
    // inventory, and report what happened. Mutates `inventory` in place. Throws
    // InvalidDataException on invalid catalog/boost config.
    public static RedemptionResult ApplyRedemption(
        JsonObject catalog,
        JsonObject? boost,
        JsonObject inventory,
        string viewerId,
        string viewerName,
        DateTimeOffset now,
        Random rng,
        int dupProtectionTurns = 0)
    {
        if (string.IsNullOrWhiteSpace(viewerId))
            throw new ArgumentException("viewerId is required.", nameof(viewerId));

        var collections = catalog["collections"] as JsonObject
            ?? throw new InvalidDataException("Catalog is missing a top-level 'collections' object.");

        var selection = SelectCollection(collections, boost, now, rng);
        var parts = selection.Collection["parts"] as JsonArray;
        if (parts is null || parts.Count == 0)
            throw new InvalidDataException($"Collection '{selection.Key}' has no parts.");

        // Ensure the viewer's inventory shape exists.
        var viewer = inventory[viewerId] as JsonObject;
        if (viewer is null)
        {
            viewer = new JsonObject { ["displayName"] = viewerName, ["components"] = new JsonObject() };
            inventory[viewerId] = viewer;
        }
        if (!string.IsNullOrWhiteSpace(viewerName)) viewer["displayName"] = viewerName;

        var components = viewer["components"] as JsonObject;
        if (components is null) { components = new JsonObject(); viewer["components"] = components; }

        var completed = viewer["completedCollections"] as JsonObject;
        if (completed is null) { completed = new JsonObject(); viewer["completedCollections"] = completed; }

        SeedExistingCompletions(components, collections, completed, now);

        // Dup-protection counter: with no stored counter, protection is treated as inactive
        // (matches the action — a fresh viewer is never force-steered).
        var pullsSinceLastDup = dupProtectionTurns;
        if (dupProtectionTurns > 0 && TryGetInt(viewer["pullsSinceLastDup"], out var storedCounter))
            pullsSinceLastDup = storedCounter;

        var ownedCounts = BuildOwnedCounts(components);

        var pull = PullEngine.Roll(selection.Collection, selection.Probability, ownedCounts, dupProtectionTurns, pullsSinceLastDup, rng)
            ?? throw new InvalidDataException($"Collection '{selection.Key}' produced no usable part.");

        var ownedBefore = CountOwnedParts(components, parts);

        var quantity = TryGetInt(components[pull.PartId], out var existing) ? existing : 0;
        quantity++;
        components[pull.PartId] = quantity;

        if (dupProtectionTurns > 0)
            viewer["pullsSinceLastDup"] = quantity > 1 ? 0 : Math.Min(pullsSinceLastDup + 1, dupProtectionTurns);

        var (consecutive, sequenceProbability) = UpdatePullStreak(viewer, pull.PartId, pull.Probability);

        var ownedAfter = CountOwnedParts(components, parts);
        var totalParts = parts.Count;
        var newlyCompleted = false;
        if (ownedBefore < totalParts && ownedAfter == totalParts && completed[selection.Key] is null)
        {
            completed[selection.Key] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            newlyCompleted = true;
        }

        return new RedemptionResult(
            selection.Key, selection.DisplayName, selection.ActiveBoostName, pull,
            ownedAfter, totalParts, quantity, quantity > 1, newlyCompleted,
            consecutive, sequenceProbability, GetRarePullLabel(selection.Key, selection.Collection));
    }

    // Weighted collection pick honoring featured-boost multipliers and event windows.
    public static CollectionSelection SelectCollection(
        JsonObject collections, JsonObject? boost, DateTimeOffset now, Random rng)
    {
        var activeBoostName = "";
        JsonObject? multipliers = null;
        if (ToBool(boost?["enabled"]))
        {
            activeBoostName = boost!["displayName"]?.ToString() ?? "";
            multipliers = boost["collectionMultipliers"] as JsonObject;
            if (string.IsNullOrWhiteSpace(activeBoostName))
                throw new InvalidDataException("An enabled boost needs a displayName.");
            if (multipliers is null || multipliers.Count == 0)
                throw new InvalidDataException("An enabled boost needs collectionMultipliers.");
            foreach (var (key, value) in multipliers)
            {
                if (collections[key] is null)
                    throw new InvalidDataException("Boost references unknown collection: " + key);
                if (ToDouble(value) <= 0)
                    throw new InvalidDataException("Boost multipliers must be greater than zero.");
            }
        }

        var keys = new List<string>();
        var weights = new List<double>();
        double totalWeight = 0;
        foreach (var (key, value) in collections)
        {
            if (value is not JsonObject collection) continue;
            if (!IsCollectionActive(key, collection, now)) continue;
            var weight = GetCollectionWeight(key, collection);
            if (multipliers?[key] is JsonNode multiplier) weight *= ToDouble(multiplier);
            if (weight <= 0) continue;
            keys.Add(key);
            weights.Add(weight);
            totalWeight += weight;
        }
        if (keys.Count == 0 || totalWeight <= 0)
            throw new InvalidDataException("No collections have a positive weight.");

        var roll = rng.NextDouble() * totalWeight;
        double cumulative = 0;
        var chosen = keys.Count - 1;      // fall back to the last bucket on rounding
        for (var i = 0; i < keys.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) { chosen = i; break; }
        }

        var chosenKey = keys[chosen];
        var chosenCollection = (JsonObject)collections[chosenKey]!;
        var displayName = chosenCollection["displayName"]?.ToString() ?? chosenKey;
        // The boost label only applies if the SELECTED collection actually had a multiplier.
        var appliedBoost = !string.IsNullOrWhiteSpace(activeBoostName) && multipliers?[chosenKey] is not null
            ? activeBoostName : "";
        return new CollectionSelection(chosenKey, chosenCollection, displayName, weights[chosen] / totalWeight, appliedBoost);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static bool IsCollectionActive(string key, JsonObject collection, DateTimeOffset now)
    {
        var enabledToken = collection["enabled"];
        if (enabledToken is not null && !ToBool(enabledToken)) return false;

        var type = collection["type"]?.ToString() ?? "permanent";
        if (type.Equals("permanent", StringComparison.OrdinalIgnoreCase)) return true;
        if (!type.Equals("event", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unknown collection type for {key}: {type}");

        // Event collections must be explicitly enabled and inside their UTC window.
        if (enabledToken is null) return false;
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        var validFrom = DateTimeOffset.TryParse(collection["activeFromUtc"]?.ToString() ?? "", CultureInfo.InvariantCulture, styles, out var from);
        var validUntil = DateTimeOffset.TryParse(collection["activeUntilUtc"]?.ToString() ?? "", CultureInfo.InvariantCulture, styles, out var until);
        if (!validFrom || !validUntil || until <= from)
            throw new InvalidDataException($"Event collection has an invalid UTC schedule: {key}");
        return now >= from && now < until;
    }

    private static double GetCollectionWeight(string key, JsonObject collection)
    {
        var configured = collection["weight"];
        if (configured is not null)
        {
            var weight = ToDouble(configured);
            if (weight < 0) throw new InvalidDataException($"Collection weight cannot be negative: {key}");
            return weight;
        }
        return key switch
        {
            "basic" => 45, "power" => 25, "advanced" => 15, "broken" => 10, "quantum" => 5,
            _ => throw new InvalidDataException($"Collection is missing a weight: {key}")
        };
    }

    private static string GetRarePullLabel(string key, JsonObject collection)
    {
        var configured = collection["rareLabel"]?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return key switch { "broken" => "RARE CIRCUIT PULL", "quantum" => "QUANTUM PULL", _ => "" };
    }

    private static int CountOwnedParts(JsonObject components, JsonArray parts)
    {
        var count = 0;
        foreach (var node in parts)
        {
            if (node is not JsonObject part) continue;
            var id = part["id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(id) && TryGetInt(components[id], out var c) && c > 0) count++;
        }
        return count;
    }

    // Silently records any collection the viewer already completed before this pull, so a
    // collection finished off-engine still counts (and won't re-fire as "newly completed").
    private static void SeedExistingCompletions(JsonObject components, JsonObject collections, JsonObject completed, DateTimeOffset now)
    {
        var recordedAt = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        foreach (var (key, value) in collections)
        {
            if (completed[key] is not null) continue;
            if (value is not JsonObject collection || collection["parts"] is not JsonArray parts || parts.Count == 0) continue;
            if (CountOwnedParts(components, parts) == parts.Count) completed[key] = recordedAt;
        }
    }

    private static (int Count, double SequenceProbability) UpdatePullStreak(JsonObject viewer, string partId, double partProbability)
    {
        var streak = viewer["pullStreak"] as JsonObject;
        if (streak is null) { streak = new JsonObject(); viewer["pullStreak"] = streak; }

        var previousPartId = streak["partId"]?.ToString() ?? "";
        TryGetInt(streak["count"], out var previousCount);
        var previousProbability = ToDouble(streak["sequenceProbability"]);

        int count;
        double sequenceProbability;
        if (previousPartId == partId && previousCount > 0 && previousProbability > 0)
        {
            count = previousCount + 1;
            sequenceProbability = previousProbability * partProbability;
        }
        else
        {
            count = 1;
            sequenceProbability = partProbability;
        }

        streak["partId"] = partId;
        streak["count"] = count;
        streak["sequenceProbability"] = sequenceProbability;
        return (count, sequenceProbability);
    }

    private static Dictionary<string, int> BuildOwnedCounts(JsonObject components)
    {
        var owned = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in components)
            if (TryGetInt(value, out var count)) owned[key] = count;
        return owned;
    }

    private static bool ToBool(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b)) return b;
            if (value.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed)) return parsed;
        }
        return false;
    }

    private static bool TryGetInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is not JsonValue jsonValue) return false;
        if (jsonValue.TryGetValue<int>(out value)) return true;
        if (jsonValue.TryGetValue<long>(out var l)) { value = (int)l; return true; }
        if (jsonValue.TryGetValue<double>(out var d)) { value = (int)d; return true; }
        if (jsonValue.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        return false;
    }

    private static double ToDouble(JsonNode? node)
    {
        if (node is not JsonValue value) return 0;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0;
    }
}
