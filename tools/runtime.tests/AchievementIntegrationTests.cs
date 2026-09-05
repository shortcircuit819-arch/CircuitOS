using System.Text.Json.Nodes;
using System.Reflection;
using CircuitOS.Runtime;

internal static class AchievementIntegrationTests
{
    public static void Run()
    {
        var failures = new List<string>();
        Check("Achievements backfill inactive profiles without a new pull", BackfillAllProfiles, failures);
        Check("Achievement migration and reads are idempotent", IdempotentAndReadOnly, failures);
        Check("Existing holdings receive custom rewards once without another pull", BackfillCustomRewards, failures);
        Check("Saving settings after a failed backfill can be retried without partial grant", SaveFailureRetry, failures);
        foreach (var file in new[] { "inventory.json", "system-profile.json", "components.json" })
            Check("Achievement migration preserves corrupt " + file, () => CorruptionIsPreserved(file), failures);
        Check("Achievement awards and rewards commit together and only once", RewardsAreAtomic, failures);
        Check("Achievement reward overflow leaves the complete inventory unchanged", OverflowIsAtomic, failures);
        Check("Achievement settings reject invalid rewards without writes", InvalidSettingsDoNotWrite, failures);
        Check("Malformed achievement state never becomes an empty replacement", MalformedStateIsPreserved, failures);
        Check("Malformed existing viewer cannot be replaced by a new redemption", MalformedViewerDispatch, failures);
        Check("Profile switch cannot redirect achievement settings or grants", SaveTargetStaysPinned, failures);
        Check("Imported modules and packs reject invalid achievement settings before creating profiles", InvalidImportsDoNotWrite, failures);
        Check("General profile save preserves concurrent achievement edits", GeneralSavePreservesAchievements, failures);
        Check("Old profiles receive collision-free badge commands without changing existing aliases", BadgeCommandMigration, failures);
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }

    private static void BackfillAllProfiles()
    {
        using var fixture = new Fixture();
        fixture.AddProfile("other", 2);
        fixture.Store.SwitchProfile("other");
        var original = fixture.Inventory("default").DeepClone();
        var summary = fixture.Service.InitializeAchievements();
        Require((summary["errors"] as JsonArray)?.Count == 0, "Healthy profiles should reconcile without errors.");
        foreach (var id in new[] { "default", "other" })
        {
            var viewer = fixture.Inventory(id)["viewer"]!;
            Require(viewer["achievements"]?["builtin:starter:first"] is JsonObject, "Existing viewer must receive the first-item badge in " + id);
            Require(viewer["achievements"]?["builtin:starter:100"] is null, "Partial collection must not receive completion in " + id);
            Require(JsonUtil.Long(viewer["wallet"] as JsonObject, "scrap") == 7, "Default migration must not change money.");
            Require(JsonUtil.Long(viewer["achievementStats"] as JsonObject, "lifetimePulls") == 0, "Past pulls must not be invented from holdings.");
            Require(viewer["achievements"]?["builtin:starter:first"]?["earnedAtUtc"] is null, "Historical award must not claim an invented earning date.");
        }
        Require(fixture.Inventory("default")["viewer"]?["achievements"]?["builtin:starter:50"] is null, "Profile one must keep its own one-item progress.");
        Require(fixture.Inventory("other")["viewer"]?["achievements"]?["builtin:starter:50"] is JsonObject, "Profile two must use its own two-item progress.");
        Require(fixture.Store.ActiveProfileId == "other", "Migration must preserve editing selection.");
        Require(!fixture.Store.ListProfiles().Single(p => p.Id == "other").IsLive, "Migration must not activate a draft.");
        var backups = Directory.GetFiles(Path.Combine(fixture.Root, "profiles", "default", "config-backups"), "inventory_*.json");
        Require(backups.Any(path => JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), original)), "Migration must back up the original inventory before replacing it.");
    }

    private static void IdempotentAndReadOnly()
    {
        using var fixture = new Fixture();
        fixture.Service.InitializeAchievements();
        var before = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
        var count = fixture.Store.ListBackups().Count;
        fixture.Service.InitializeAchievements();
        _ = fixture.Service.GetAchievements();
        Require(before.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Repeated migration/read must leave inventory bytes unchanged.");
        Require(fixture.Store.ListBackups().Count == count, "No-op migrations/GET must not produce backup churn.");
    }

    private static void BackfillCustomRewards()
    {
        using var fixture = new Fixture();
        fixture.Service.InitializeAchievements();
        var settings = HoldingRewardSettings();
        Require(fixture.Service.SaveAchievements(settings).Status == 200, "Eligible existing-holdings reward should save and reconcile.");
        var viewer = fixture.Inventory("default")["viewer"]!;
        Require(viewer["achievements"]?["existing_item"] is JsonObject, "Existing viewer should get custom award without a pull.");
        Require(JsonUtil.Long(viewer["wallet"] as JsonObject, "scrap") == 20, "Backfill should pay configured reward once.");
        Require(viewer["achievements"]?["existing_item"]?["earnedAtUtc"] is null, "Backfill must not invent the historical earning time.");
        Require(JsonUtil.Bool(viewer["achievements"]?["existing_item"] as JsonObject, "backfilled"), "Backfill origin should be recorded.");
        var paidRecord = viewer["achievements"]!["existing_item"]!.DeepClone();
        settings["definitions"]![0]!["name"] = "Renamed after earning";
        settings["definitions"]![0]!["reward"] = 99;
        Require(fixture.Service.SaveAchievements(settings).Status == 200, "Editing an existing definition should save.");
        settings["definitions"] = new JsonArray();
        Require(fixture.Service.SaveAchievements(settings).Status == 200, "Removing a definition should save.");
        fixture.Service.InitializeAchievements();
        viewer = fixture.Inventory("default")["viewer"]!;
        Require(JsonNode.DeepEquals(viewer["achievements"]?["existing_item"], paidRecord), "An earned record must remain stable after rename/remove/restart.");
        Require(JsonUtil.Long(viewer["wallet"] as JsonObject, "scrap") == 20, "Editing/removing/restarting must not duplicate reward.");
    }

    private static void SaveFailureRetry()
    {
        using var fixture = new Fixture();
        fixture.Service.InitializeAchievements();
        var path = fixture.PathFor("default", "inventory.json");
        var before = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = fixture.Service.SaveAchievements(HoldingRewardSettings());
            Require(result.Status != 200 || (result.Body["backfill"]?["errors"] as JsonArray)?.Count > 0, "Save must surface reconciliation failure even when settings were persisted.");
            Require(before.SequenceEqual(File.ReadAllBytes(path)), "Failed backfill must not partly grant awards or rewards.");
        }
        Require(fixture.Service.SaveAchievements(HoldingRewardSettings()).Status == 200, "Retry should finish persisted settings reconciliation.");
        fixture.Service.InitializeAchievements();
        var viewer = fixture.Inventory("default")["viewer"]!;
        Require(JsonUtil.Long(viewer["wallet"] as JsonObject, "scrap") == 20, "Retried backfill should grant exactly one configured reward.");
        Require(viewer["achievements"]?["existing_item"] is JsonObject, "Retried reward and award must be committed together.");
    }

    private static JsonObject HoldingRewardSettings()
    {
        var settings = AchievementEngine.DefaultSettings();
        settings["definitions"] = new JsonArray(new JsonObject { ["id"] = "existing_item", ["name"] = "An existing item", ["description"] = "Own one item", ["metric"] = "uniqueItems", ["threshold"] = 1, ["reward"] = 13, ["enabled"] = true });
        return settings;
    }

    private static void CorruptionIsPreserved(string file)
    {
        using var fixture = new Fixture();
        fixture.AddProfile("healthy", 1);
        var inventoryBefore = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
        File.WriteAllText(fixture.PathFor("default", file), "{broken achievement-test");
        var damaged = File.ReadAllBytes(fixture.PathFor("default", file));
        var summary = fixture.Service.InitializeAchievements();
        Require((summary["errors"] as JsonArray)?.OfType<JsonObject>().Any(row => JsonUtil.String(row, "profileId") == "default") == true, "Corruption must be surfaced with its profile id.");
        Require(damaged.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", file))), "Migration must not overwrite a corrupt file.");
        if (file != "inventory.json") Require(inventoryBefore.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Corrupt configuration must not produce inventory grants.");
        Require(fixture.Inventory("healthy")["viewer"]?["achievements"]?["builtin:starter:first"] is JsonObject, "One damaged profile must not prevent healthy profile migration.");
    }

    private static void RewardsAreAtomic()
    {
        using var fixture = new Fixture();
        fixture.Service.InitializeAchievements();
        Require(fixture.Service.SaveAchievements(RewardSettings()).Status == 200, "Valid future-pull reward should save.");
        var path = fixture.PathFor("default", "inventory.json");
        var original = File.ReadAllBytes(path);
        // On Windows this denies the deletion/replace required by the atomic inventory writer.
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ServiceResult? failed = null;
            Exception? error = null;
            try { failed = fixture.Service.DispatchRuntimeAction(Pull()); } catch (Exception ex) { error = ex; }
            Require(error is not null || failed?.Status != 200, "A failed inventory write must not report a successful award.");
            Require(original.SequenceEqual(File.ReadAllBytes(path)), "A failed grant must preserve holdings, awards, stats and wallet together.");
        }
        var result = fixture.Service.DispatchRuntimeAction(Pull());
        Require(result.Status == 200, "Retry after a definite failed write must succeed: " + result.Body);
        var viewer = fixture.Inventory("default")["viewer"]!;
        Require(JsonUtil.Long(viewer["wallet"] as JsonObject, "scrap") == 18, "First new pull must pay the custom reward exactly once.");
        Require(viewer["achievements"]?["first_new_pull"] is JsonObject, "Paid reward must have a persisted permanent award.");
        Require(JsonUtil.Long(viewer["achievementStats"] as JsonObject, "lifetimePulls") == 1, "Failed write must not count as a lifetime pull.");
        Require(fixture.Service.DispatchRuntimeAction(Pull()).Status == 200, "A second pull should still run.");
        fixture.Service.InitializeAchievements();
        Require(fixture.Service.SaveAchievements(RewardSettings()).Status == 200, "Saving the same reward should succeed.");
        Require(JsonUtil.Long(fixture.Inventory("default")["viewer"]?["wallet"] as JsonObject, "scrap") == 18, "Repeated pull/startup/save must not repay the award.");
    }

    private static void OverflowIsAtomic()
    {
        using var fixture = new Fixture();
        fixture.Service.InitializeAchievements();
        Require(fixture.Service.SaveAchievements(RewardSettings()).Status == 200, "Reward setup should save.");
        var inventory = fixture.Inventory("default");
        inventory["viewer"]!["wallet"]!["scrap"] = long.MaxValue;
        fixture.Store.WriteProfileData("default", DataKeys.Inventory, inventory);
        var before = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
        ServiceResult? result = null;
        Exception? error = null;
        try { result = fixture.Service.DispatchRuntimeAction(Pull()); } catch (Exception ex) { error = ex; }
        Require(error is not null || result?.Status != 200, "Overflow must be surfaced as an error.");
        Require(before.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Overflow must not partly grant an item, badge, stats or negative balance.");
    }

    private static void InvalidSettingsDoNotWrite()
    {
        using var fixture = new Fixture();
        var profileBefore = File.ReadAllBytes(fixture.PathFor("default", "system-profile.json"));
        var inventoryBefore = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
        foreach (var reward in new JsonNode[] { JsonValue.Create(-1)!, JsonValue.Create(1000001)!, JsonValue.Create(1.5)!, JsonValue.Create("999999999999999999999999")! })
        {
            var settings = RewardSettings();
            settings["definitions"]![0]!["reward"] = reward.DeepClone();
            Require(fixture.Service.SaveAchievements(settings).Status != 200, "Invalid rewards must be rejected.");
        }
        Require(profileBefore.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "system-profile.json"))), "Rejected settings must leave profile unchanged.");
        Require(inventoryBefore.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Rejected settings must leave inventory unchanged.");
    }

    private static void MalformedStateIsPreserved()
    {
        foreach (var field in new[] { "achievementStats", "achievements", "wallet" })
        {
            using var fixture = new Fixture();
            var inventory = fixture.Inventory("default");
            inventory["viewer"]![field] = null;
            fixture.Store.WriteProfileData("default", DataKeys.Inventory, inventory);
            var before = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
            var summary = fixture.Service.InitializeAchievements();
            Require((summary["errors"] as JsonArray)?.Count > 0, "Explicit null " + field + " must be reported as malformed, not missing.");
            Require(before.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Malformed " + field + " must not be overwritten during migration.");
        }
    }

    private static void MalformedViewerDispatch()
    {
        foreach (var malformed in new JsonNode?[] { null, new JsonArray(), JsonValue.Create("damaged") })
        {
            using var fixture = new Fixture();
            var inventory = fixture.Inventory("default");
            inventory["viewer"] = malformed?.DeepClone();
            fixture.Store.WriteProfileData("default", DataKeys.Inventory, inventory);
            var before = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
            ServiceResult? result = null;
            Exception? error = null;
            try { result = fixture.Service.DispatchRuntimeAction(Pull()); } catch (Exception ex) { error = ex; }
            Require(error is not null || result?.Status != 200, "Malformed existing viewer must cancel redemption.");
            Require(before.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Redemption must not turn malformed viewer into empty/new inventory.");
        }
    }

    private static void SaveTargetStaysPinned()
    {
        using var fixture = new Fixture();
        fixture.AddProfile("other", 2);
        fixture.Service.InitializeAchievements();
        var secondBefore = fixture.Inventory("other").DeepClone();
        var profileBefore = fixture.Store.ReadProfileDataStrict("other", DataKeys.Profile)!.DeepClone();
        var gate = typeof(CircuitService).GetMethod("InventoryLock", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { "default" })!;
        ServiceResult? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = fixture.Service.SaveAchievements(HoldingRewardSettings()); } catch (Exception ex) { error = ex; }
        });
        lock (gate)
        {
            thread.Start();
            Require(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000), "Save must reach inventory lock.");
            fixture.Store.SwitchProfile("other");
        }
        Require(thread.Join(5000), "Pinned save must finish.");
        if (error is not null) throw error;
        Require(result?.Status == 200, "Pinned save must succeed.");
        Require(JsonNode.DeepEquals(secondBefore, fixture.Inventory("other")), "New editing profile must not receive the first profile's grants.");
        Require(JsonNode.DeepEquals(profileBefore, fixture.Store.ReadProfileDataStrict("other", DataKeys.Profile)), "New editing profile settings must remain unchanged.");
        Require(JsonUtil.Long(fixture.Inventory("default")["viewer"]?["wallet"] as JsonObject, "scrap") == 20, "Original profile should receive the requested reward.");
    }

    private static void InvalidImportsDoNotWrite()
    {
        using var fixture = new Fixture();
        foreach (var kind in new[] { "module", "pack" })
        {
            var exported = kind == "module" ? fixture.Service.ExportModule() : fixture.Service.ExportCollectionPack("starter");
            Require(exported.Status == 200, "Fixture should export a valid " + kind);
            var invalidReward = RewardSettings();
            invalidReward["definitions"]![0]!["reward"] = -10;
            foreach (var invalid in new JsonNode?[] { invalidReward, new JsonArray(), null })
            {
                var payload = (JsonObject)exported.Body.DeepClone();
                payload["profile"]!["achievements"] = invalid?.DeepClone();
                var beforeCount = fixture.Store.ListProfiles().Count;
                var result = kind == "module" ? fixture.Service.ImportModule(payload) : fixture.Service.ImportCollectionPack(payload, "Invalid achievements");
                Require(result.Status != 200, "Malformed achievement settings must reject imported " + kind);
                Require(fixture.Store.ListProfiles().Count == beforeCount, "Rejected " + kind + " must not create a profile.");
            }
        }
    }

    private static void GeneralSavePreservesAchievements()
    {
        using var fixture = new Fixture();
        fixture.AddProfile("other", 1);
        var requested = (JsonObject)fixture.Service.GetSystemProfile()["profile"]!.DeepClone();
        requested["gameName"] = "Updated title";
        var otherBefore = fixture.Store.ReadProfileDataStrict("other", DataKeys.Profile)!.DeepClone();
        var gate = typeof(CircuitService).GetMethod("InventoryLock", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { "default" })!;
        ServiceResult? result = null;
        Exception? error = null;
        var thread = new Thread(() => { try { result = fixture.Service.SaveSystemProfile(requested); } catch (Exception ex) { error = ex; } });
        lock (gate)
        {
            thread.Start();
            Require(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000), "General save must share the achievement settings lock.");
            Require(fixture.Service.SaveAchievements(HoldingRewardSettings()).Status == 200, "Concurrent achievement save should finish first.");
            fixture.Store.SwitchProfile("other");
        }
        Require(thread.Join(5000), "General profile save must finish.");
        if (error is not null) throw error;
        Require(result?.Status == 200, "General save should succeed.");
        var original = fixture.Store.ReadProfileDataStrict("default", DataKeys.Profile)!;
        Require(JsonUtil.String(original, "gameName") == "Updated title", "General edit belongs to original profile.");
        Require(JsonNode.DeepEquals(original["achievements"], HoldingRewardSettings()), "General save must preserve latest separately-saved achievement settings.");
        Require(JsonNode.DeepEquals(otherBefore, fixture.Store.ReadProfileDataStrict("other", DataKeys.Profile)), "Profile switch must not redirect general save.");
    }

    private static void BadgeCommandMigration()
    {
        using var fixture = new Fixture();
        fixture.AddProfile("other", 1);
        foreach (var id in new[] { "default", "other" })
        {
            var profile = fixture.Store.ReadProfileDataStrict(id, DataKeys.Profile)!;
            var commands = (JsonObject)profile["commands"]!;
            commands.Remove("badges");
            if (id == "default") commands["inventory"] = "badges";
            fixture.Store.WriteProfileData(id, DataKeys.Profile, profile);
        }
        fixture.Service.InitializeAchievements();
        var firstCommands = (JsonObject)fixture.Store.ReadProfileDataStrict("default", DataKeys.Profile)!["commands"]!;
        var secondCommands = (JsonObject)fixture.Store.ReadProfileDataStrict("other", DataKeys.Profile)!["commands"]!;
        var firstAlias = JsonUtil.String(firstCommands, "badges");
        var secondAlias = JsonUtil.String(secondCommands, "badges");
        Require(JsonUtil.String(firstCommands, "inventory") == "badges", "Existing command alias must remain unchanged.");
        Require(firstAlias.Length > 0 && firstAlias != "badges" && firstAlias != secondAlias, "Migrated badge aliases must avoid existing and each other's commands.");
        var before = File.ReadAllBytes(fixture.PathFor("default", "inventory.json"));
        var result = fixture.Service.DispatchRuntimeAction(new JsonObject { ["action"] = "command", ["profileId"] = "default", ["viewerId"] = "viewer", ["viewerName"] = "Viewer", ["command"] = firstAlias, ["arg"] = "2147483647" });
        Require(result.Status == 200 && result.Body["messages"]?.ToJsonString().Contains("First item") == true, "Migrated badge command must return persisted award names.");
        Require(before.SequenceEqual(File.ReadAllBytes(fixture.PathFor("default", "inventory.json"))), "Badge command must not rewrite or regrant inventory.");
    }

    private static JsonObject RewardSettings()
    {
        var settings = AchievementEngine.DefaultSettings();
        settings["definitions"] = new JsonArray(new JsonObject { ["id"] = "first_new_pull", ["name"] = "First new pull", ["description"] = "One pull after tracking started", ["metric"] = "lifetimePulls", ["threshold"] = 1, ["reward"] = 11, ["enabled"] = true });
        return settings;
    }

    private static JsonObject Pull() => new() { ["action"] = "redeem", ["profileId"] = "default", ["viewerId"] = "viewer", ["viewerName"] = "Viewer", ["rngSeed"] = 1 };
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Check(string name, Action action, List<string> failures)
    {
        try { action(); Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL: " + failures[^1]); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "CircuitOS-achievement-integration-" + Guid.NewGuid().ToString("N"));
        public LocalFileDataStore Store { get; }
        public CircuitService Service { get; }
        public Fixture() { Store = new LocalFileDataStore(Root); Service = new CircuitService(Store); AddProfile("default", 1); }
        public void AddProfile(string id, int owned)
        {
            if (id != "default") Store.CreateProfile(id, id);
            var profile = (JsonObject)Service.GetSystemProfile()["profile"]!.DeepClone();
            profile["redeemCooldownSeconds"] = 0;
            profile["achievements"] = AchievementEngine.DefaultSettings();
            Store.WriteProfileData(id, DataKeys.Profile, profile);
            var parts = new JsonArray();
            for (var i = 0; i < 4; i++) parts.Add(new JsonObject { ["id"] = "item" + i, ["name"] = "Item " + i });
            Store.WriteProfileData(id, DataKeys.Catalog, new JsonObject { ["schemaVersion"] = 1, ["collections"] = new JsonObject { ["starter"] = new JsonObject { ["displayName"] = "Starter", ["type"] = "permanent", ["weight"] = 100, ["salvageValue"] = 1, ["parts"] = parts } } });
            Store.WriteProfileData(id, DataKeys.Boost, new JsonObject { ["enabled"] = false });
            var components = new JsonObject();
            for (var i = 0; i < owned; i++) components["item" + i] = 1;
            Store.WriteProfileData(id, DataKeys.Inventory, new JsonObject { ["viewer"] = new JsonObject { ["displayName"] = "Viewer", ["components"] = components, ["wallet"] = new JsonObject { ["scrap"] = 7 } } });
        }
        public JsonObject Inventory(string id) => Store.ReadProfileDataStrict(id, DataKeys.Inventory)!;
        public string PathFor(string id, string file) => Path.Combine(Root, "profiles", id, file);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
