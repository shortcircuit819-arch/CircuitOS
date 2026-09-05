using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed partial class CircuitService
{
    private JsonObject _achievementBackfill = new() { ["profiles"] = new JsonArray(), ["errors"] = new JsonArray() };

    private static JsonObject AchievementSettings(JsonObject? profile)
    {
        if (profile is null || !profile.ContainsKey("achievements")) return AchievementEngine.DefaultSettings();
        if (profile["achievements"] is not JsonObject settings)
            throw new InvalidDataException("Achievement settings must be an object.");
        return (JsonObject)settings.DeepClone();
    }

    // Explicit startup migration, never a GET side effect. Each profile is its own recoverable
    // transaction; no editing selection is changed and a failed profile cannot erase another.
    public JsonObject InitializeAchievements()
    {
        var profiles = new JsonArray();
        var errors = new JsonArray();
        var all = _store.ListProfiles();
        var usedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in all)
            if (_store.ReadProfileData(p.Id, DataKeys.Profile)?["commands"] is JsonObject commands)
                foreach (var command in commands) usedCommands.Add(command.Value?.ToString() ?? "");
        foreach (var p in all)
        {
            try
            {
                lock (InventoryLock(p.Id))
                {
                    var raw = _store.ReadProfileDataStrict(p.Id, DataKeys.Profile);
                    if (raw is not null && (raw["commands"] as JsonObject)?["badges"] is null)
                    {
                        var commands = raw["commands"] as JsonObject;
                        if (raw["commands"] is not null && commands is null)
                            throw new InvalidDataException("Profile commands must be an object.");
                        commands ??= new JsonObject();
                        var alias = "badges";
                        for (var suffix = 2; usedCommands.Contains(alias); suffix++) alias = $"badges-{suffix}";
                        commands["badges"] = alias;
                        raw["commands"] = commands;
                        _store.WriteProfileData(p.Id, DataKeys.Profile, raw);
                        usedCommands.Add(alias);
                    }
                    profiles.Add(ReconcileAchievements(p.Id));
                }
            }
            catch (Exception ex)
            {
                errors.Add(new JsonObject { ["profileId"] = p.Id, ["error"] = ex.Message });
            }
        }
        _achievementBackfill = new JsonObject { ["profiles"] = profiles, ["errors"] = errors };
        return (JsonObject)_achievementBackfill.DeepClone();
    }

    private JsonObject ReconcileAchievements(string profileId)
    {
        lock (InventoryLock(profileId))
        {
            var profile = _store.ReadProfileDataStrict(profileId, DataKeys.Profile);
            var settings = AchievementSettings(profile);
            var catalog = _store.ReadProfileDataStrict(profileId, DataKeys.Catalog)
                ?? throw new InvalidDataException("Catalog is missing.");
            var inventory = _store.ReadProfileDataStrict(profileId, DataKeys.Inventory);
            var awarded = 0;
            if (inventory is null) return new JsonObject { ["profileId"] = profileId, ["viewers"] = 0, ["awarded"] = 0 };
            var before = inventory.ToJsonString();
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in inventory)
            {
                if (pair.Value is not JsonObject viewer) throw new InvalidDataException($"Viewer '{pair.Key}' must be an object.");
                EnsureAchievementStats(viewer, now);
                awarded += AchievementEngine.Evaluate(catalog, viewer, settings, now, true).Count;
            }
            if (before != inventory.ToJsonString()) WriteProfileData(profileId, DataKeys.Inventory, inventory);
            return new JsonObject { ["profileId"] = profileId, ["viewers"] = inventory.Count, ["awarded"] = awarded };
        }
    }

    private static JsonObject EnsureAchievementStats(JsonObject viewer, DateTimeOffset now)
    {
        if (viewer.ContainsKey("achievementStats") && viewer["achievementStats"] is not JsonObject)
            throw new InvalidDataException("Viewer achievementStats must be an object.");
        var stats = viewer["achievementStats"] as JsonObject;
        if (stats is null)
        {
            stats = new JsonObject { ["lifetimePulls"] = 0L, ["trackingStartedAtUtc"] = now.UtcDateTime.ToString("O") };
            viewer["achievementStats"] = stats;
        }
        if (!TryInteger(stats["lifetimePulls"], out var count) || count < 0)
            throw new InvalidDataException("Tracked lifetime pulls must be a nonnegative integer.");
        return stats;
    }

    public JsonObject GetAchievements()
    {
        var catalog = _store.TryRead(DataKeys.Catalog) ?? new JsonObject();
        var collections = catalog["collections"] as JsonObject ?? new JsonObject();
        return new JsonObject
        {
            ["settings"] = AchievementSettings(_store.TryRead(DataKeys.Profile)),
            ["collections"] = new JsonArray(collections.Select(pair => (JsonNode)new JsonObject
                { ["key"] = pair.Key, ["displayName"] = JsonUtil.String(pair.Value as JsonObject, "displayName", pair.Key) }).ToArray()),
            ["backfill"] = _achievementBackfill.DeepClone()
        };
    }

    public ServiceResult SaveAchievements(JsonObject settings)
    {
        var profileId = _store.ActiveProfileId;
        lock (InventoryLock(profileId))
        {
            var catalog = _store.ReadProfileDataStrict(profileId, DataKeys.Catalog) ?? new JsonObject();
            var errors = AchievementEngine.ValidateSettings(settings, catalog);
            if (errors.Count > 0) return Error(errors);
            var profile = _store.ReadProfileDataStrict(profileId, DataKeys.Profile) ?? DefaultProfile();
            profile["achievements"] = settings.DeepClone();
            // Settings first, then the independent idempotent inventory transaction. A failed
            // inventory commit is visible and retryable; the grant is never stored separately.
            _store.WriteProfileData(profileId, DataKeys.Profile, profile);
            try
            {
                var result = ReconcileAchievements(profileId);
                _achievementBackfill = new JsonObject { ["profiles"] = new JsonArray(result), ["errors"] = new JsonArray() };
                return Ok(new JsonObject { ["ok"] = true, ["backfill"] = _achievementBackfill.DeepClone() });
            }
            catch (Exception ex)
            {
                _achievementBackfill = new JsonObject { ["profiles"] = new JsonArray(), ["errors"] = new JsonArray(new JsonObject { ["profileId"] = profileId, ["error"] = ex.Message }) };
                return Ok(new JsonObject { ["ok"] = true, ["backfill"] = _achievementBackfill.DeepClone() });
            }
        }
    }

    private static IReadOnlyList<string> BadgeMessages(JsonObject inventory, string viewerId, string viewerName, string argument)
    {
        var awards = inventory[viewerId]?["achievements"] as JsonObject;
        var names = awards?.Select(a => JsonUtil.String(a.Value as JsonObject, "name", a.Key)).ToList() ?? [];
        if (names.Count == 0) return [$"@{viewerName} no badges earned yet."];
        var page = int.TryParse(argument, out var n) ? Math.Max(1, n) : 1;
        var pages = (names.Count + 2) / 3;
        page = Math.Min(page, pages);
        return [$"@{viewerName} badges ({page}/{pages}): {string.Join(" | ", names.Skip((page - 1) * 3).Take(3))}"];
    }
}
