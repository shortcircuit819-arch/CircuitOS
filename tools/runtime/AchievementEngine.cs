using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CircuitOS.Runtime;

// Pure inventory transformation. Callers persist the whole viewer under the inventory lock.
internal static class AchievementEngine
{
    public static JsonObject DefaultSettings() => new()
    {
        ["enabled"] = true, ["collectionBadges"] = true, ["announce"] = true,
        ["overlay"] = true, ["defaultReward"] = 0, ["definitions"] = new JsonArray()
    };

    public static List<string> ValidateSettings(JsonObject settings, JsonObject catalog)
        => ValidateSettingsCore(settings, catalog, false);

    public static List<string> ValidateStoredSettings(JsonObject settings, JsonObject catalog)
        => ValidateSettingsCore(settings, catalog, true);

    private static List<string> ValidateSettingsCore(JsonObject settings, JsonObject catalog, bool allowMissingCollections)
    {
        var errors = new List<string>();
        foreach (var flag in new[] { "enabled", "collectionBadges", "announce", "overlay" })
            if (settings[flag] is not null && (settings[flag] is not JsonValue v || !v.TryGetValue<bool>(out _)))
                errors.Add($"{flag} must be true or false.");
        if (!Integer(settings["defaultReward"], out var defaultReward) || defaultReward is < 0 or > 1000000)
            errors.Add("defaultReward must be an integer from 0 to 1000000.");
        if (settings["definitions"] is not JsonArray definitions)
        { errors.Add("definitions must be an array."); return errors; }
        if (definitions.Count > 100) errors.Add("At most 100 achievement definitions are allowed.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in definitions)
        {
            if (node is not JsonObject d) { errors.Add("Each achievement must be an object."); continue; }
            foreach (var field in new[] { "id", "name", "description", "metric", "collectionKey" })
                if (d[field] is not null && (d[field] is not JsonValue text || !text.TryGetValue<string>(out _)))
                    errors.Add($"Achievement {field} must be text.");
            var id = Text(d, "id");
            if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9_-]{0,63}$") || id.StartsWith("builtin", StringComparison.Ordinal))
                errors.Add("Achievement IDs must be 1–64 lowercase letters, numbers, underscores or hyphens, and cannot start with builtin.");
            if (!ids.Add(id)) errors.Add($"Duplicate achievement ID: {id}.");
            if (string.IsNullOrWhiteSpace(Text(d, "name")) || Text(d, "name").Length > 80) errors.Add($"{id}: name must be 1–80 characters.");
            if (Text(d, "description").Length > 300) errors.Add($"{id}: description must be at most 300 characters.");
            var metric = Text(d, "metric");
            if (metric is not ("collectionPercent" or "uniqueItems" or "completedCollections" or "lifetimePulls")) errors.Add($"{id}: unknown metric.");
            if (!Integer(d["threshold"], out var threshold) || threshold <= 0 || (metric == "collectionPercent" && threshold > 100)) errors.Add($"{id}: invalid positive integer threshold.");
            if (!Integer(d["reward"], out var reward) || reward is < 0 or > 1000000) errors.Add($"{id}: reward must be an integer from 0 to 1000000.");
            var key = Text(d, "collectionKey");
            if (metric == "collectionPercent" && key.Length == 0) errors.Add($"{id}: a collection is required.");
            if (!allowMissingCollections && key.Length > 0 && (catalog["collections"] as JsonObject)?[key] is not JsonObject) errors.Add($"{id}: unknown collection.");
            if (key.Length > 0 && metric is not ("collectionPercent" or "uniqueItems")) errors.Add($"{id}: this metric cannot select a collection.");
            if (d["enabled"] is not null && (d["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out _))) errors.Add($"{id}: enabled must be true or false.");
        }
        return errors;
    }

    public static JsonArray Evaluate(JsonObject catalog, JsonObject viewer, JsonObject settings, DateTimeOffset now, bool backfill)
    {
        var errors = ValidateSettingsCore(settings, catalog, true);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        var earned = new JsonArray();
        var existing = viewer["achievements"] as JsonObject;
        if (viewer.ContainsKey("achievements") && existing is null) throw new InvalidDataException("Viewer achievements must be an object.");
        if (existing is not null && existing.Any(entry => entry.Value is not JsonObject))
            throw new InvalidDataException("Stored achievement records must be objects.");
        var stats = viewer["achievementStats"] as JsonObject;
        if (viewer.ContainsKey("achievementStats") && stats is null) throw new InvalidDataException("Viewer achievement statistics must be an object.");
        if (stats?.ContainsKey("lifetimePulls") == true && (!Integer(stats["lifetimePulls"], out var pulls) || pulls < 0))
            throw new InvalidDataException("Viewer lifetime pull count is invalid.");
        var wallet = viewer["wallet"] as JsonObject;
        if (viewer.ContainsKey("wallet") && wallet is null) throw new InvalidDataException("Viewer wallet must be an object.");
        long balance = 0;
        if (wallet?.ContainsKey("scrap") == true && (!Integer(wallet["scrap"], out balance) || balance < 0)) throw new InvalidDataException("Viewer Scrap balance is invalid.");
        if (!Flag(settings, "enabled")) return earned;
        var candidates = GetProgress(catalog, viewer, settings).OfType<JsonObject>()
            .Where(row => !Flag(row, "earned", false) && Number(row["current"]) >= Number(row["target"]))
            .ToList();
        if (candidates.Count == 0) return earned;
        // Preflight the entire reward batch before mutating anything, including silent backfill.
        long total = 0;
        checked { foreach (var row in candidates) total += Number(row["reward"]); balance += total; }
        var timestamp = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var awards = existing ?? new JsonObject();
        foreach (var row in candidates)
        {
            var record = new JsonObject
            {
                ["id"] = Text(row, "id"), ["name"] = Text(row, "name"), ["description"] = Text(row, "description"),
                ["recordedAtUtc"] = timestamp, ["earnedAtUtc"] = backfill ? null : timestamp,
                ["backfilled"] = backfill, ["reward"] = Number(row["reward"])
            };
            awards[Text(row, "id")] = record;
            earned.Add(record.DeepClone());
        }
        if (existing is null) viewer["achievements"] = awards;
        if (total > 0)
        {
            if (wallet is null) { wallet = new JsonObject(); viewer["wallet"] = wallet; }
            wallet["scrap"] = balance;
        }
        return earned;
    }

    public static JsonArray GetProgress(JsonObject catalog, JsonObject viewer, JsonObject settings)
    {
        var rows = new JsonArray();
        var awards = viewer["achievements"] as JsonObject;
        var collections = catalog["collections"] as JsonObject ?? new JsonObject();
        var components = viewer["components"] as JsonObject;
        var completed = viewer["completedCollections"] as JsonObject;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string id, string name, string description, string metric, long current, long target, long reward)
        {
            if (!seen.Add(id)) return;
            var award = awards?[id] as JsonObject;
            rows.Add(new JsonObject { ["id"] = id, ["name"] = award is null ? name : Text(award, "name"),
                ["description"] = award is null ? description : Text(award, "description"), ["metric"] = metric,
                ["current"] = current, ["target"] = target, ["earned"] = awards?.ContainsKey(id) == true,
                ["reward"] = award is null ? reward : Number(award["reward"]),
                ["backfilled"] = award?["backfilled"]?.DeepClone(),
                ["recordedAtUtc"] = award?["recordedAtUtc"]?.DeepClone(),
                ["earnedAtUtc"] = award?["earnedAtUtc"]?.DeepClone() });
        }
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, node) in collections) allIds.UnionWith(PartIds(node as JsonObject));
        long Owned(IEnumerable<string> ids) => ids.LongCount(id => Number(components?[id]) > 0);
        var completedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (completed is not null)
            foreach (var (key, date) in completed)
                if (date is not null) completedKeys.Add(key);
        foreach (var (key, node) in collections)
        {
            var ids = PartIds(node as JsonObject);
            if (ids.Count > 0 && Owned(ids) == ids.Count) completedKeys.Add(key);
        }
        if (Flag(settings, "enabled") && Flag(settings, "collectionBadges"))
        {
            foreach (var (key, node) in collections)
            {
                var collection = node as JsonObject;
                var ids = PartIds(collection);
                if (ids.Count == 0) continue;
                var name = Text(collection, "displayName", key);
                var owned = Owned(ids);
                var historical = completed?[key] is not null;
                foreach (var (suffix, label, percent) in new[] { ("first", "First item", 0), ("25", "25% collected", 25), ("50", "50% collected", 50), ("100", "Collection complete", 100) })
                {
                    var target = percent == 0 ? 1 : (long)Math.Ceiling(ids.Count * (percent / 100.0));
                    Add($"builtin:{key}:{suffix}", $"{name}: {label}", $"Collect {target} unique {(target == 1 ? "item" : "items")} in {name}.", "uniqueItems", historical ? Math.Max(owned, target) : owned, target, Number(settings["defaultReward"]));
                }
            }
        }
        if (Flag(settings, "enabled") && settings["definitions"] is JsonArray definitions)
        {
            foreach (var d in definitions.OfType<JsonObject>())
            {
                if (!Flag(d, "enabled")) continue;
                var key = Text(d, "collectionKey");
                var ids = key.Length == 0 ? allIds : PartIds(collections[key] as JsonObject);
                var metric = Text(d, "metric");
                long current = metric switch
                {
                    "uniqueItems" => Owned(ids),
                    "collectionPercent" => completed?[key] is not null ? 100 : ids.Count == 0 ? 0 : Owned(ids) * 100 / ids.Count,
                    "completedCollections" => completedKeys.Count,
                    "lifetimePulls" => Number((viewer["achievementStats"] as JsonObject)?["lifetimePulls"]),
                    _ => 0
                };
                Add(Text(d, "id"), Text(d, "name"), Text(d, "description"), metric, current, Number(d["threshold"]), Number(d["reward"]));
            }
        }
        if (awards is not null)
            foreach (var (id, node) in awards)
                if (!seen.Contains(id) && node is JsonObject award)
                    Add(id, Text(award, "name", id), Text(award, "description"), "recorded", 1, 1, Number(award["reward"]));
        return rows;
    }

    private static HashSet<string> PartIds(JsonObject? collection) => new(
        (collection?["parts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
        .Select(p => Text(p, "id")).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
    private static string Text(JsonObject? obj, string key, string fallback = "") => obj?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;
    private static bool Flag(JsonObject obj, string key, bool fallback = true) => obj[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
    private static bool Integer(JsonNode? node, out long value)
    {
        value = 0;
        return node is JsonValue v && (v.TryGetValue<long>(out value)
            || (v.GetValueKind() == System.Text.Json.JsonValueKind.Number && long.TryParse(v.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            || (v.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)));
    }
    private static long Number(JsonNode? node) => Integer(node, out var value) ? Math.Max(0, value) : 0;
}
