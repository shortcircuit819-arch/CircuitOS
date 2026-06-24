using System.Globalization;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed partial class CircuitService
{
    private sealed class CollectionMetric
    {
        public required string Key { get; init; }
        public required string DisplayName { get; init; }
        public required string Type { get; init; }
        public long SalvageValue { get; init; }
        public int PartCount { get; init; }
        public long ViewerOwners { get; set; }
        public long UniqueOwned { get; set; }
        public long DuplicateUnits { get; set; }
        public long UnclaimedScrap { get; set; }
    }

    private long GetSalvageValue(string key, JsonObject collection)
    {
        if (TryInteger(collection["salvageValue"], out var configured)) return configured;
        return key switch { "basic" => 1, "power" => 2, "advanced" => 3, "broken" => 5, "quantum" => 10, _ => 0 };
    }

    public JsonObject GetInventoryAnalytics()
    {
        var inventory = _store.TryRead(DataKeys.Inventory);
        if (inventory is null)
        {
            return new JsonObject
            {
                ["summary"] = EmptyAnalyticsSummary(), ["collections"] = new JsonArray(), ["viewers"] = new JsonArray(),
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O")
            };
        }

        var catalog = _store.ReadRequired(DataKeys.Catalog);
        var catalogCollections = JsonUtil.Object(catalog, "collections") ?? new JsonObject();
        var metrics = new Dictionary<string, CollectionMetric>(StringComparer.Ordinal);
        foreach (var (key, node) in catalogCollections)
        {
            var collection = node as JsonObject ?? new JsonObject();
            metrics[key] = new CollectionMetric
            {
                Key = key,
                DisplayName = JsonUtil.String(collection, "displayName", key),
                Type = JsonUtil.String(collection, "type", "permanent"),
                SalvageValue = GetSalvageValue(key, collection),
                PartCount = JsonUtil.Array(collection, "parts")?.Count ?? 0
            };
        }

        var viewers = new JsonArray();
        var balances = new List<long>();
        long totalScrap = 0, totalDuplicates = 0, totalUnclaimed = 0, totalUnits = 0, totalUnique = 0;
        foreach (var (viewerId, viewerNode) in inventory)
        {
            var viewer = viewerNode as JsonObject;
            var components = JsonUtil.Object(viewer, "components");
            if (viewer is null || components is null) continue;
            var displayName = JsonUtil.String(viewer, "displayName", viewerId);
            var wallet = JsonUtil.Object(viewer, "wallet");
            var completed = JsonUtil.Object(viewer, "completedCollections");
            var scrap = Math.Max(0, JsonUtil.Long(wallet, "scrap"));
            long viewerUnits = 0, viewerUnique = 0, viewerDuplicates = 0, viewerUnclaimed = 0;
            var viewerCompletions = 0;
            var viewerCollections = new JsonArray();

            foreach (var (key, collectionNode) in catalogCollections)
            {
                var collection = collectionNode as JsonObject ?? new JsonObject();
                var parts = JsonUtil.Array(collection, "parts") ?? new JsonArray();
                var salvageValue = GetSalvageValue(key, collection);
                var ownedCount = 0;
                long collectionUnits = 0, collectionDuplicates = 0;
                var partRows = new JsonArray();
                foreach (var partNode in parts)
                {
                    var part = partNode as JsonObject;
                    var partId = JsonUtil.String(part, "id");
                    var partName = JsonUtil.String(part, "name", partId);
                    var quantity = Math.Max(0, JsonUtil.Long(components, partId));
                    var duplicates = Math.Max(0, quantity - 1);
                    if (quantity > 0)
                    {
                        ownedCount++;
                        viewerUnique++;
                        metrics[key].UniqueOwned++;
                    }
                    collectionUnits += quantity;
                    collectionDuplicates += duplicates;
                    viewerUnits += quantity;
                    viewerDuplicates += duplicates;
                    viewerUnclaimed += duplicates * salvageValue;
                    partRows.Add(new JsonObject { ["id"] = partId, ["name"] = partName, ["quantity"] = quantity });
                }
                var completionDate = JsonUtil.String(completed, key);
                if (!string.IsNullOrWhiteSpace(completionDate)) viewerCompletions++;
                if (ownedCount > 0) metrics[key].ViewerOwners++;
                metrics[key].DuplicateUnits += collectionDuplicates;
                metrics[key].UnclaimedScrap += collectionDuplicates * salvageValue;
                viewerCollections.Add(new JsonObject
                {
                    ["key"] = key, ["displayName"] = JsonUtil.String(collection, "displayName", key),
                    ["type"] = JsonUtil.String(collection, "type", "permanent"), ["ownedCount"] = ownedCount,
                    ["totalCount"] = parts.Count, ["totalUnits"] = collectionUnits,
                    ["duplicateUnits"] = collectionDuplicates, ["completionDate"] = completionDate, ["parts"] = partRows
                });
            }

            totalScrap += scrap;
            totalDuplicates += viewerDuplicates;
            totalUnclaimed += viewerUnclaimed;
            totalUnits += viewerUnits;
            totalUnique += viewerUnique;
            balances.Add(scrap);
            viewers.Add(new JsonObject
            {
                ["id"] = viewerId, ["displayName"] = displayName, ["scrap"] = scrap,
                ["totalUnits"] = viewerUnits, ["uniqueComponents"] = viewerUnique,
                ["duplicateUnits"] = viewerDuplicates, ["unclaimedScrap"] = viewerUnclaimed,
                ["completedCollections"] = viewerCompletions, ["collections"] = viewerCollections
            });
        }

        var sortedViewers = new JsonArray(viewers.Select(node => node?.DeepClone()).OrderBy(node => JsonUtil.String(node as JsonObject, "displayName"), StringComparer.OrdinalIgnoreCase).ToArray());
        balances.Sort();
        var average = balances.Count > 0 ? totalScrap / (double)balances.Count : 0;
        var median = balances.Count switch
        {
            0 => 0,
            var count when count % 2 == 1 => balances[count / 2],
            var count => (balances[count / 2 - 1] + balances[count / 2]) / 2.0
        };
        var collectionRows = new JsonArray();
        foreach (var metric in metrics.Values.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            collectionRows.Add(new JsonObject
            {
                ["key"] = metric.Key, ["displayName"] = metric.DisplayName, ["type"] = metric.Type,
                ["salvageValue"] = metric.SalvageValue, ["partCount"] = metric.PartCount,
                ["viewerOwners"] = metric.ViewerOwners, ["uniqueOwned"] = metric.UniqueOwned,
                ["duplicateUnits"] = metric.DuplicateUnits, ["unclaimedScrap"] = metric.UnclaimedScrap
            });
        }
        return new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["viewerCount"] = sortedViewers.Count, ["totalScrap"] = totalScrap,
                ["averageScrap"] = Math.Round(average, 2), ["medianScrap"] = Math.Round(median, 2),
                ["duplicateUnits"] = totalDuplicates, ["unclaimedScrap"] = totalUnclaimed,
                ["totalOwnedUnits"] = totalUnits, ["totalUniqueOwned"] = totalUnique
            },
            ["collections"] = collectionRows, ["viewers"] = sortedViewers,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    public ServiceResult ResetViewer(JsonObject body)
    {
        var viewerId = JsonUtil.String(body, "viewerId");
        if (string.IsNullOrWhiteSpace(viewerId)) return Error(["A viewer ID is required."]);
        var inventory = _store.TryRead(DataKeys.Inventory) ?? new JsonObject();
        if (!inventory.ContainsKey(viewerId)) return Error([$"Viewer '{viewerId}' not found in inventory."]);
        var displayName = JsonUtil.String(inventory[viewerId] as JsonObject, "displayName", viewerId);
        inventory.Remove(viewerId);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        _store.WriteAtomic(DataKeys.Inventory, inventory, "inventory", timestamp);
        return Ok(new JsonObject { ["ok"] = true, ["viewerId"] = viewerId, ["displayName"] = displayName });
    }

    public ServiceResult RemoveInventoryItem(JsonObject body)
    {
        var viewerId = JsonUtil.String(body, "viewerId");
        var itemId = JsonUtil.String(body, "itemId");
        if (string.IsNullOrWhiteSpace(viewerId) || string.IsNullOrWhiteSpace(itemId))
            return Error(["A viewer ID and item ID are required."]);
        var inventory = _store.TryRead(DataKeys.Inventory) ?? new JsonObject();
        var viewer = inventory[viewerId] as JsonObject;
        if (viewer is null) return Error([$"Viewer '{viewerId}' not found in inventory."]);
        var components = JsonUtil.Object(viewer, "components");
        if (components is null || !components.ContainsKey(itemId))
            return Error([$"Item '{itemId}' not found in this viewer's inventory."]);
        components.Remove(itemId);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        _store.WriteAtomic(DataKeys.Inventory, inventory, "inventory", timestamp);
        return Ok(new JsonObject { ["ok"] = true, ["viewerId"] = viewerId, ["itemId"] = itemId });
    }

    private static JsonObject EmptyAnalyticsSummary() => new()
    {
        ["viewerCount"] = 0, ["totalScrap"] = 0, ["averageScrap"] = 0,
        ["medianScrap"] = 0, ["duplicateUnits"] = 0, ["unclaimedScrap"] = 0,
        ["totalOwnedUnits"] = 0, ["totalUniqueOwned"] = 0
    };

    private JsonObject DefaultRoleState()
    {
        var catalog = _store.ReadRequired(DataKeys.Catalog);
        var collections = JsonUtil.Object(catalog, "collections") ?? new JsonObject();
        var names = new JsonObject();
        foreach (var (key, node) in collections)
            names[key] = $"{JsonUtil.String(node as JsonObject, "displayName", key)} Collector";
        return new JsonObject { ["roleNames"] = names, ["fulfilled"] = new JsonObject() };
    }

    private JsonObject GetRoleState()
    {
        var state = _store.TryRead(DataKeys.Roles) ?? DefaultRoleState();
        state["roleNames"] ??= new JsonObject();
        state["fulfilled"] ??= new JsonObject();
        return state;
    }

    public JsonObject GetDiscordRoleAwards()
    {
        var catalog = _store.ReadRequired(DataKeys.Catalog);
        var collections = JsonUtil.Object(catalog, "collections") ?? new JsonObject();
        var state = GetRoleState();
        var storedNames = JsonUtil.Object(state, "roleNames") ?? new JsonObject();
        var fulfilled = JsonUtil.Object(state, "fulfilled") ?? new JsonObject();
        var roleNames = new JsonObject();
        foreach (var (key, node) in collections)
        {
            var fallback = $"{JsonUtil.String(node as JsonObject, "displayName", key)} Collector";
            var configured = JsonUtil.String(storedNames, key, fallback);
            roleNames[key] = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }

        var awardRows = new List<JsonObject>();
        var inventory = _store.TryRead(DataKeys.Inventory);
        if (inventory is not null)
        {
            foreach (var (viewerId, viewerNode) in inventory)
            {
                var viewer = viewerNode as JsonObject;
                var completed = JsonUtil.Object(viewer, "completedCollections");
                if (viewer is null || completed is null) continue;
                var displayName = JsonUtil.String(viewer, "displayName", viewerId);
                foreach (var (collectionKey, completionNode) in completed)
                {
                    if (collections[collectionKey] is not JsonObject collection) continue;
                    var completion = completionNode?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(completion)) continue;
                    var awardKey = $"{viewerId}::{collectionKey}";
                    var assignment = fulfilled[awardKey] as JsonObject;
                    var assignedAt = JsonUtil.String(assignment, "assignedAtUtc");
                    awardRows.Add(new JsonObject
                    {
                        ["awardKey"] = awardKey, ["userId"] = viewerId, ["displayName"] = displayName,
                        ["collectionKey"] = collectionKey,
                        ["collectionName"] = JsonUtil.String(collection, "displayName", collectionKey),
                        ["roleName"] = JsonUtil.String(roleNames, collectionKey), ["completedAtUtc"] = completion,
                        ["assignedAtUtc"] = assignedAt, ["assigned"] = !string.IsNullOrWhiteSpace(assignedAt)
                    });
                }
            }
        }
        var ordered = awardRows.OrderBy(row => JsonUtil.Bool(row, "assigned"))
            .ThenByDescending(row => ParseDate(JsonUtil.String(row, "completedAtUtc"))).ToList();
        var awards = new JsonArray(ordered.Select(row => row.DeepClone()).ToArray());
        var pending = ordered.Count(row => !JsonUtil.Bool(row, "assigned"));
        return new JsonObject
        {
            ["roleNames"] = roleNames, ["awards"] = awards,
            ["summary"] = new JsonObject { ["pending"] = pending, ["assigned"] = ordered.Count - pending, ["total"] = ordered.Count },
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    public ServiceResult UpdateDiscordRoleAwards(JsonObject request)
    {
        var operation = JsonUtil.String(request, "operation");
        var state = GetRoleState();
        var catalog = _store.ReadRequired(DataKeys.Catalog);
        var collections = JsonUtil.Object(catalog, "collections") ?? new JsonObject();
        if (operation == "saveRoleNames")
        {
            var requested = JsonUtil.Object(request, "roleNames");
            if (requested is null) return Error(["roleNames is required."]);
            var roleNames = new JsonObject();
            foreach (var (key, _) in collections)
            {
                var name = JsonUtil.String(requested, key).Trim();
                if (name.Length is < 1 or > 100) return Error([$"Role name for '{key}' must contain 1 to 100 characters."]);
                roleNames[key] = name;
            }
            state["roleNames"] = roleNames;
        }
        else if (operation == "setAssigned")
        {
            var userId = JsonUtil.String(request, "userId");
            var collectionKey = JsonUtil.String(request, "collectionKey");
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(collectionKey) || !TryBool(request["assigned"], out var assigned))
                return Error(["A viewer, collection, and boolean assigned value are required."]);
            if (!collections.ContainsKey(collectionKey)) return Error([$"Unknown collection '{collectionKey}'."]);
            var inventory = _store.TryRead(DataKeys.Inventory);
            if (inventory is null) return Error(["Inventory is unavailable."]);
            var viewer = inventory[userId] as JsonObject;
            var completion = JsonUtil.Object(viewer, "completedCollections")?[collectionKey]?.ToString();
            if (string.IsNullOrWhiteSpace(completion)) return Error([$"This viewer does not have a recorded completion for '{collectionKey}'."]);
            var fulfilled = JsonUtil.Object(state, "fulfilled")!;
            var awardKey = $"{userId}::{collectionKey}";
            if (assigned)
            {
                fulfilled[awardKey] = new JsonObject
                {
                    ["assignedAtUtc"] = DateTime.UtcNow.ToString("O"),
                    ["roleName"] = JsonUtil.String(JsonUtil.Object(state, "roleNames"), collectionKey)
                };
            }
            else fulfilled.Remove(awardKey);
        }
        else return Error(["Unknown role award operation."]);

        var backup = _store.WriteAtomic(DataKeys.Roles, state, "discord-role-awards", Timestamp());
        return Ok(new JsonObject
        {
            ["ok"] = true, ["savedAtUtc"] = DateTime.UtcNow.ToString("O"), ["backup"] = backup
        });
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.MinValue;
}
