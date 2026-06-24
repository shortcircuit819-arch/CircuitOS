using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Shared chat-command logic, the read/query counterpart to RedemptionEngine. Ported from the
// generated Streamer.bot command actions so the native Twitch path, Streamer.bot, and MixItUp
// answer commands identically:
//   - StreamerbotCatalogCommands.txt → Inventory, Missing, Duplicates, Balance, Leaderboard
//   - StreamerbotCollection.txt       → CollectionDetail
//   - StreamerbotSalvage.txt          → Salvage (the one WRITE command)
//
// The actions hand-parse JSON (they intentionally avoid Newtonsoft); here we reimplement the
// same behavior cleanly over System.Text.Json.Nodes. Read commands return the chat line(s) to
// send (with the same ~440-char segmentation). Salvage mutates the inventory in place and
// reports what to persist + say. Configurable wording (terminology + message templates) comes
// in via CommandContext so the engine stays game-agnostic; the caller builds it from the profile.
//
// NOTE: the wallet currency is stored under the fixed key "scrap" (matching the actions and the
// saved inventory); CommandContext.CurrencyName is only the display label. Legacy salvageValue
// fallbacks (basic/power/advanced/broken/quantum) mirror the action for un-upgraded catalogs.
internal sealed record CommandContext(
    string GameName,
    string ItemSingular,
    string ItemPlural,
    string CollectionSingular,
    string RedemptionName,
    string CurrencyName,
    string CollectionCommand,
    string SalvageCommand,
    string NoInventoryTemplate,
    string BalanceTemplate,
    string NoDuplicatesTemplate,
    string CollectionUsageTemplate,
    string CollectionSummaryTemplate,
    string SalvageUsageTemplate,
    string NothingToSalvageTemplate,
    string SalvageSuccessTemplate);

internal sealed record SalvageResult(
    string Message,             // the single chat line to send
    bool Mutated,               // true when inventory changed (caller should persist)
    long ConsumedComponents,
    long EarnedCurrency,
    long NewBalance);

internal static class CommandEngine
{
    private const string WalletCurrencyKey = "scrap";
    private const int MaxChatSegmentLength = 440;

    // ── Read commands ───────────────────────────────────────────────────────────

    public static IReadOnlyList<string> Inventory(
        JsonObject catalog, JsonObject inventory, CommandContext ctx, string viewerId, string viewerName, DateTimeOffset now)
    {
        var viewer = inventory[viewerId] as JsonObject;
        if (viewer is null) return [NoInventoryMessage(ctx, viewerName)];

        var components = viewer["components"] as JsonObject ?? new JsonObject();
        var completions = viewer["completedCollections"] as JsonObject;

        var summaries = new List<string>();
        foreach (var collection in LoadCollections(catalog, now))
        {
            var owned = CountOwned(components, collection.PartIds);
            var hasCompletion = completions?[collection.Key] is not null;
            if (collection.IsEvent && !collection.IsActive && owned == 0 && !hasCompletion) continue;

            var summary = $"{ShortName(collection.DisplayName)} {owned}/{collection.PartIds.Count}";
            if (collection.IsEvent) summary += collection.IsActive ? " [ACTIVE]" : " [HISTORICAL]";
            summaries.Add(summary);
        }
        return Segment(viewerName, "Collections: ", summaries, "No collections found.");
    }

    public static IReadOnlyList<string> Missing(
        JsonObject catalog, JsonObject inventory, CommandContext ctx, string viewerId, string viewerName, DateTimeOffset now)
    {
        var viewer = inventory[viewerId] as JsonObject;
        if (viewer is null) return [NoInventoryMessage(ctx, viewerName)];
        var components = viewer["components"] as JsonObject ?? new JsonObject();

        var summaries = new List<string>();
        foreach (var collection in LoadCollections(catalog, now))
        {
            if (collection.IsEvent && !collection.IsActive) continue;
            var missing = new List<string>();
            for (var i = 0; i < collection.PartIds.Count; i++)
                if (Quantity(components, collection.PartIds[i]) <= 0) missing.Add(collection.PartNames[i]);
            summaries.Add($"{ShortName(collection.DisplayName)}: {JoinOrDefault(missing, ", ", "COMPLETE")}");
        }
        return Segment(viewerName, "Missing: ", summaries, "No active collections found.");
    }

    public static IReadOnlyList<string> Duplicates(
        JsonObject catalog, JsonObject inventory, CommandContext ctx, string viewerId, string viewerName, DateTimeOffset now)
    {
        var viewer = inventory[viewerId] as JsonObject;
        if (viewer is null) return [NoInventoryMessage(ctx, viewerName)];
        var components = viewer["components"] as JsonObject ?? new JsonObject();

        var duplicates = new List<string>();
        foreach (var collection in LoadCollections(catalog, now))
            for (var i = 0; i < collection.PartIds.Count; i++)
            {
                var quantity = Quantity(components, collection.PartIds[i]);
                if (quantity > 1) duplicates.Add($"{ShortName(collection.DisplayName)} {collection.PartNames[i]} x{quantity}");
            }

        if (duplicates.Count == 0)
            return [FormatMessage(ctx.NoDuplicatesTemplate, "viewer", viewerName, "itemPlural", ctx.ItemPlural)];
        return Segment(viewerName, "Duplicates: ", duplicates, "No duplicates found.");
    }

    public static string Balance(JsonObject inventory, CommandContext ctx, string viewerId, string viewerName)
    {
        var viewer = inventory[viewerId] as JsonObject;
        if (viewer is null) return NoInventoryMessage(ctx, viewerName);
        var wallet = viewer["wallet"] as JsonObject;
        var balance = wallet is not null && TryGetLong(wallet[WalletCurrencyKey], out var scrap) ? scrap : 0L;
        return FormatMessage(ctx.BalanceTemplate, "viewer", viewerName, "currency", ctx.CurrencyName, "balance", balance.ToString());
    }

    public static IReadOnlyList<string> CollectionDetail(
        JsonObject catalog, JsonObject inventory, CommandContext ctx, string viewerId, string viewerName, string requestedCollection, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(requestedCollection))
            return [FormatMessage(ctx.CollectionUsageTemplate, "viewer", viewerName, "collectionCommand", ctx.CollectionCommand, "collectionSingular", ctx.CollectionSingular)];

        var collections = catalog["collections"] as JsonObject
            ?? throw new InvalidDataException("Catalog is missing a top-level 'collections' object.");

        var key = ResolveCollectionKey(collections, requestedCollection);
        if (key is null)
            return [$"@{viewerName} unknown collection. Available: {JoinOrDefault(collections.Select(kv => kv.Key).ToList(), ", ", "none")}"];

        var collection = (JsonObject)collections[key]!;
        var displayName = AsString(collection["displayName"]) is { Length: > 0 } d ? d : key;
        var (partIds, partNames) = ReadParts(collection);
        if (partIds.Count == 0) return [$"@{viewerName} {displayName} has no valid parts."];

        var viewer = inventory[viewerId] as JsonObject;
        var components = viewer?["components"] as JsonObject ?? new JsonObject();
        var completions = viewer?["completedCollections"] as JsonObject;

        var owned = new List<string>();
        var missing = new List<string>();
        var duplicates = new List<string>();
        for (var i = 0; i < partIds.Count; i++)
        {
            var quantity = Quantity(components, partIds[i]);
            if (quantity > 0)
            {
                owned.Add(partNames[i]);
                if (quantity > 1) duplicates.Add($"{partNames[i]} x{quantity}");
            }
            else missing.Add(partNames[i]);
        }

        var completedAt = AsString(completions?[key]);
        var status = CompletionStatus(owned.Count, partIds.Count, completedAt);
        var availability = AvailabilityStatus(collection, now);

        return
        [
            FormatMessage(ctx.CollectionSummaryTemplate,
                "viewer", viewerName, "collection", displayName,
                "owned", owned.Count.ToString(), "total", partIds.Count.ToString(),
                "status", status, "availability", availability),
            $"@{viewerName} Owned: {JoinOrDefault(owned, ", ", "none")} | Missing: {JoinOrDefault(missing, ", ", "none")} | Duplicates: {JoinOrDefault(duplicates, ", ", "none")}"
        ];
    }

    public static IReadOnlyList<string> Leaderboard(
        JsonObject catalog, JsonObject inventory, CommandContext ctx, string viewerId, string viewerName, string? requestedCollection, DateTimeOffset now)
    {
        var collections = LoadCollections(catalog, now);
        List<CollectionDef> ranked;
        CollectionDef? selected = null;

        if (!string.IsNullOrWhiteSpace(requestedCollection))
        {
            var normalized = NormalizeCollectionName(requestedCollection);
            selected = collections.FirstOrDefault(c =>
                NormalizeCollectionName(c.Key) == normalized || NormalizeCollectionName(c.DisplayName) == normalized);
            if (selected is null) return [$"@{viewerName} unknown collection for leaderboard."];
            ranked = [selected];
        }
        else
        {
            ranked = collections.Where(c => !c.IsEvent).ToList();
        }

        var maximumUnique = ranked.Sum(c => c.PartIds.Count);
        var entries = new List<(string ViewerId, string DisplayName, int Unique, int Completed)>();
        foreach (var (id, value) in inventory)
        {
            if (value is not JsonObject viewer || viewer["components"] is not JsonObject components) continue;
            var unique = 0;
            var completed = 0;
            foreach (var collection in ranked)
            {
                var owned = CountOwned(components, collection.PartIds);
                unique += owned;
                if (owned == collection.PartIds.Count) completed++;
            }
            if (unique > 0)
                entries.Add((id, AsString(viewer["displayName"]) is { Length: > 0 } name ? name : id, unique, completed));
        }

        entries.Sort((left, right) =>
        {
            var byUnique = right.Unique.CompareTo(left.Unique);
            if (byUnique != 0) return byUnique;
            var byCompleted = right.Completed.CompareTo(left.Completed);
            return byCompleted != 0 ? byCompleted : StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        });

        if (entries.Count == 0) return ["No viewers have progress for that leaderboard yet."];

        var leaders = new List<string>();
        var shown = Math.Min(5, entries.Count);
        for (var i = 0; i < shown; i++)
        {
            var entry = entries[i];
            var line = $"#{i + 1} {entry.DisplayName} {entry.Unique}/{maximumUnique}";
            if (selected is null && entry.Completed > 0) line += $" ({entry.Completed} complete)";
            leaders.Add(line);
        }

        var title = selected is null ? $"{ctx.GameName} Leaderboard: " : $"{ShortName(selected.DisplayName)} Leaderboard: ";
        var lines = new List<string>(Segment(viewerName, title, leaders, "No leaderboard entries yet."));

        for (var i = shown; i < entries.Count; i++)
            if (entries[i].ViewerId == viewerId)
            {
                lines.Add($"@{viewerName} Your rank: #{i + 1} with {entries[i].Unique}/{maximumUnique}.");
                break;
            }
        return lines;
    }

    // ── Salvage (write) ─────────────────────────────────────────────────────────

    // Converts a viewer's duplicate copies into currency, mutating `inventory` in place. The
    // caller is responsible for the inventory lock + atomic persist (the write discipline) when
    // Mutated is true. Throws InvalidDataException on invalid catalog/inventory data.
    public static SalvageResult Salvage(
        JsonObject catalog, JsonObject inventory, CommandContext ctx, string viewerId, string viewerName, string requestedCollection)
    {
        if (string.IsNullOrWhiteSpace(requestedCollection))
            return new SalvageResult(
                FormatMessage(ctx.SalvageUsageTemplate, "viewer", viewerName, "salvageCommand", ctx.SalvageCommand, "collectionSingular", ctx.CollectionSingular),
                false, 0, 0, 0);

        var collections = catalog["collections"] as JsonObject
            ?? throw new InvalidDataException("Catalog is missing a top-level 'collections' object.");

        var normalized = NormalizeCollectionName(requestedCollection);
        var selected = new List<(string Key, JsonObject Collection)>();
        foreach (var (key, value) in collections)
        {
            if (value is not JsonObject collection) throw new InvalidDataException($"Collection '{key}' must be a JSON object.");
            if (normalized == "all" || NormalizeCollectionName(key) == normalized || NormalizeCollectionName(AsString(collection["displayName"])) == normalized)
            {
                _ = SalvageValue(key, collection); // validate up front (throws on bad value)
                selected.Add((key, collection));
                if (normalized != "all") break;
            }
        }
        if (selected.Count == 0) return new SalvageResult($"@{viewerName} unknown collection.", false, 0, 0, 0);

        var viewer = inventory[viewerId] as JsonObject;
        var components = viewer?["components"] as JsonObject;
        if (viewer is null || components is null)
            return new SalvageResult($"@{viewerName} you don't have any components to salvage.", false, 0, 0, 0);

        long consumed = 0;
        long earned = 0;
        foreach (var (key, collection) in selected)
        {
            var salvageValue = SalvageValue(key, collection);
            if (collection["parts"] is not JsonArray parts) continue;
            foreach (var node in parts)
            {
                if (node is not JsonObject part) continue;
                var partId = AsString(part["id"]);
                if (string.IsNullOrWhiteSpace(partId) || components[partId] is null) continue;
                if (!TryGetLong(components[partId], out var quantity) || quantity < 0)
                    throw new InvalidDataException($"Invalid component quantity for '{partId}'.");
                var extras = Math.Max(0L, quantity - 1L);
                if (extras > 0)
                {
                    components[partId] = 1L;
                    consumed = checked(consumed + extras);
                    earned = checked(earned + checked(extras * salvageValue));
                }
            }
        }

        if (consumed == 0)
            return new SalvageResult(
                FormatMessage(ctx.NothingToSalvageTemplate, "viewer", viewerName, "selection", SelectionLabel(selected, normalized)),
                false, 0, 0, 0);

        var wallet = viewer["wallet"] as JsonObject;
        if (wallet is null) { wallet = new JsonObject(); viewer["wallet"] = wallet; }
        var current = TryGetLong(wallet[WalletCurrencyKey], out var existing) ? existing : 0L;
        if (current < 0) throw new InvalidDataException("Wallet balance is invalid.");
        var newBalance = checked(current + earned);
        wallet[WalletCurrencyKey] = newBalance;

        var message = FormatMessage(ctx.SalvageSuccessTemplate,
            "viewer", viewerName,
            "count", consumed.ToString(),
            "itemWord", consumed == 1 ? ctx.ItemSingular : ctx.ItemPlural,
            "earned", earned.ToString(),
            "currency", ctx.CurrencyName,
            "balance", newBalance.ToString());
        return new SalvageResult(message, true, consumed, earned, newBalance);
    }

    // ── Catalog/collection helpers ───────────────────────────────────────────────

    private sealed record CollectionDef(string Key, string DisplayName, bool IsEvent, bool IsActive, List<string> PartIds, List<string> PartNames);

    private static List<CollectionDef> LoadCollections(JsonObject catalog, DateTimeOffset now)
    {
        var collections = catalog["collections"] as JsonObject
            ?? throw new InvalidDataException("Catalog is missing a top-level 'collections' object.");

        var result = new List<CollectionDef>();
        foreach (var (key, value) in collections)
        {
            if (value is not JsonObject collection) continue;
            var displayName = AsString(collection["displayName"]) is { Length: > 0 } d ? d : key;
            var isEvent = string.Equals(AsString(collection["type"]), "event", StringComparison.OrdinalIgnoreCase);
            var isActive = !isEvent || IsEventActive(collection, now);
            var (ids, names) = ReadParts(collection);
            if (ids.Count > 0) result.Add(new CollectionDef(key, displayName, isEvent, isActive, ids, names));
        }
        return result;
    }

    private static (List<string> Ids, List<string> Names) ReadParts(JsonObject collection)
    {
        var ids = new List<string>();
        var names = new List<string>();
        if (collection["parts"] is JsonArray parts)
            foreach (var node in parts)
            {
                if (node is not JsonObject part) continue;
                var id = AsString(part["id"]);
                var name = AsString(part["name"]);
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) { ids.Add(id); names.Add(name); }
            }
        return (ids, names);
    }

    private static bool IsEventActive(JsonObject collection, DateTimeOffset now)
    {
        if (collection["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out var on) || !on) return false;
        if (!DateTimeOffset.TryParse(AsString(collection["activeFromUtc"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateTimeOffset.TryParse(AsString(collection["activeUntilUtc"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) ||
            until <= from)
            return false;
        return now >= from.ToUniversalTime() && now < until.ToUniversalTime();
    }

    private static string AvailabilityStatus(JsonObject collection, DateTimeOffset now)
    {
        if (!string.Equals(AsString(collection["type"]), "event", StringComparison.OrdinalIgnoreCase)) return "";
        if (collection["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out var on) || !on) return " | Event disabled";
        if (!DateTimeOffset.TryParse(AsString(collection["activeFromUtc"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateTimeOffset.TryParse(AsString(collection["activeUntilUtc"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) ||
            until <= from)
            return " | Event schedule invalid";
        if (now < from.ToUniversalTime()) return " | Event starts " + from.ToUniversalTime().ToString("yyyy-MM-dd");
        if (now >= until.ToUniversalTime()) return " | Event ended " + until.ToUniversalTime().ToString("yyyy-MM-dd");
        return " | Event active until " + until.ToUniversalTime().ToString("yyyy-MM-dd");
    }

    private static string CompletionStatus(int owned, int total, string? completedAt)
    {
        var recordedDate = string.IsNullOrWhiteSpace(completedAt) ? ""
            : DateTime.TryParse(completedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? " since " + date.ToString("yyyy-MM-dd") : " (recorded)";
        if (!string.IsNullOrWhiteSpace(completedAt) && owned == total) return "COMPLETE" + recordedDate;
        if (!string.IsNullOrWhiteSpace(completedAt)) return "Previously completed" + recordedDate;
        if (owned == total) return "COMPLETE (record pending)";
        return "In progress";
    }

    private static long SalvageValue(string key, JsonObject collection)
    {
        if (collection["salvageValue"] is not null)
        {
            if (!TryGetLong(collection["salvageValue"], out var value))
                throw new InvalidDataException($"Invalid salvageValue for '{key}'.");
            if (value <= 0) throw new InvalidDataException($"salvageValue must be positive: {key}");
            return value;
        }
        return key switch
        {
            "basic" => 1, "power" => 2, "advanced" => 3, "broken" => 5, "quantum" => 10,
            _ => throw new InvalidDataException($"Collection is missing salvageValue: {key}")
        };
    }

    private static string? ResolveCollectionKey(JsonObject collections, string requested)
    {
        var normalized = NormalizeCollectionName(requested);
        foreach (var (key, value) in collections)
        {
            if (NormalizeCollectionName(key) == normalized) return key;
            if (value is JsonObject collection && NormalizeCollectionName(AsString(collection["displayName"])) == normalized) return key;
        }
        return null;
    }

    private static string SelectionLabel(List<(string Key, JsonObject Collection)> selected, string normalizedRequest)
    {
        if (normalizedRequest == "all") return "all collections";
        var displayName = AsString(selected[0].Collection["displayName"]);
        return string.IsNullOrWhiteSpace(displayName) ? selected[0].Key : displayName;
    }

    // ── Small helpers ────────────────────────────────────────────────────────────

    private static int CountOwned(JsonObject components, List<string> partIds)
    {
        var count = 0;
        foreach (var id in partIds) if (Quantity(components, id) > 0) count++;
        return count;
    }

    private static int Quantity(JsonObject components, string partId)
        => TryGetLong(components[partId], out var value) && value > 0 ? (int)Math.Min(value, int.MaxValue) : 0;

    private static string NoInventoryMessage(CommandContext ctx, string viewerName)
        => FormatMessage(ctx.NoInventoryTemplate,
            "viewer", viewerName, "itemPlural", ctx.ItemPlural,
            "redemption", ctx.RedemptionName, "collectionSingular", ctx.CollectionSingular);

    private static string ShortName(string displayName)
    {
        const string suffix = " Collection";
        return displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? displayName[..^suffix.Length] : displayName;
    }

    private static string NormalizeCollectionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant()) if (char.IsLetterOrDigit(character)) builder.Append(character);
        var result = builder.ToString();
        const string suffix = "collection";
        return result.EndsWith(suffix, StringComparison.Ordinal) ? result[..^suffix.Length] : result;
    }

    private static string JoinOrDefault(List<string> values, string separator, string fallback)
        => values.Count == 0 ? fallback : string.Join(separator, values);

    // Splits a list of segments into chat lines no longer than ~440 chars, each re-prefixed with
    // "@viewer {prefix}" — mirrors the action's SendSegments.
    private static IReadOnlyList<string> Segment(string viewerName, string prefix, List<string> segments, string emptyMessage)
    {
        if (segments.Count == 0) return [$"@{viewerName} {emptyMessage}"];

        var lines = new List<string>();
        var messagePrefix = $"@{viewerName} {prefix}";
        var current = messagePrefix;
        foreach (var segment in segments)
        {
            var separator = current == messagePrefix ? "" : " | ";
            if (current.Length > messagePrefix.Length && current.Length + separator.Length + segment.Length > MaxChatSegmentLength)
            {
                lines.Add(current);
                current = messagePrefix + segment;
            }
            else current += separator + segment;
        }
        if (current.Length > messagePrefix.Length) lines.Add(current);
        return lines;
    }

    private static string FormatMessage(string template, params string[] replacements)
    {
        var result = template ?? "";
        for (var i = 0; i + 1 < replacements.Length; i += 2)
            result = result.Replace("{" + replacements[i] + "}", replacements[i + 1] ?? "");
        return result;
    }

    private static string? AsString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : node?.ToString();

    private static bool TryGetLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue jsonValue) return false;
        if (jsonValue.TryGetValue<long>(out value)) return true;
        if (jsonValue.TryGetValue<int>(out var i)) { value = i; return true; }
        if (jsonValue.TryGetValue<double>(out var d)) { value = (long)d; return true; }
        if (jsonValue.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        return false;
    }
}
