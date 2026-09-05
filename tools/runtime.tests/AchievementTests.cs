using System.Text.Json.Nodes;
using CircuitOS.Runtime;

internal static class AchievementTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
    public static void Run()
    {
        var settings = AchievementEngine.DefaultSettings();
        var catalog = Catalog();
        Require(AchievementEngine.ValidateSettings(settings, catalog).Count == 0, "Defaults validate.");
        var viewer = JsonNode.Parse("{\"components\":{\"a\":1},\"wallet\":{\"scrap\":5}}")!.AsObject();
        settings["defaultReward"] = 2;
        var before = viewer.ToJsonString();
        AchievementEngine.GetProgress(catalog, viewer, settings);
        Require(viewer.ToJsonString() == before, "Progress must be read-only.");
        var earned = AchievementEngine.Evaluate(catalog, viewer, settings, Now, false);
        Require(earned.Count == 4 && viewer["wallet"]!["scrap"]!.GetValue<long>() == 13, "Tiny collection awards all four thresholds atomically.");
        Require(AchievementEngine.Evaluate(catalog, viewer, settings, Now, false).Count == 0, "Awards are idempotent.");
        viewer["components"] = new JsonObject();
        settings["enabled"] = false;
        Require(AchievementEngine.GetProgress(new JsonObject(), viewer, settings).Count == 4, "Deleted collection and disabled setting retain badges.");
        settings["enabled"] = true;
        var legacy = JsonNode.Parse("{\"components\":{},\"completedCollections\":{\"set\":\"2020-01-01\"}}")!.AsObject();
        Require(AchievementEngine.Evaluate(catalog, legacy, settings, Now, true).Count == 4, "Historical completion supplies all milestones.");
        Require(legacy["wallet"]!["scrap"]!.GetValue<long>() == 8 && legacy["achievements"]!["builtin:set:100"]!["earnedAtUtc"] is null, "Backfill grants configured rewards without inventing earned dates.");
        Require(AchievementEngine.Evaluate(catalog, legacy, settings, Now, true).Count == 0 && legacy["wallet"]!["scrap"]!.GetValue<long>() == 8, "Backfill rewards are granted exactly once.");
        var legacyProgress = AchievementEngine.GetProgress(catalog, legacy, settings)[0]!;
        Require(legacyProgress["backfilled"]!.GetValue<bool>() && legacyProgress["earnedAtUtc"] is null && legacyProgress["recordedAtUtc"]!.GetValue<string>() == Now.UtcDateTime.ToString("O"), "Progress exposes backfill date metadata.");
        foreach (var broken in new[] { "{\"achievements\":[]}", "{\"achievements\":null}", "{\"achievements\":{\"existing\":null}}", "{\"achievementStats\":[]}", "{\"achievementStats\":{\"lifetimePulls\":null}}", "{\"achievementStats\":{\"lifetimePulls\":\"oops\"}}", "{\"wallet\":[]}", "{\"wallet\":{\"scrap\":-1}}" })
        {
            var malformed = JsonNode.Parse(broken)!.AsObject();
            var snapshot = malformed.ToJsonString();
            try { AchievementEngine.Evaluate(catalog, malformed, settings, Now, true); throw new Exception("Malformed ledger accepted."); }
            catch (InvalidDataException) { }
            Require(snapshot == malformed.ToJsonString(), "Malformed ledger must not be reset.");
        }
        var overflow = new JsonObject { ["components"] = new JsonObject { ["a"] = 1 }, ["wallet"] = new JsonObject { ["scrap"] = long.MaxValue } };
        before = overflow.ToJsonString();
        try { AchievementEngine.Evaluate(catalog, overflow, settings, Now, false); throw new Exception("Overflow accepted."); }
        catch (OverflowException) { }
        Require(before == overflow.ToJsonString(), "Overflow must not mutate inventory.");
        var emptyCatalog = JsonNode.Parse("{\"collections\":{\"empty\":{\"parts\":[]}}}")!.AsObject();
        Require(AchievementEngine.Evaluate(emptyCatalog, new JsonObject(), settings, Now, false).Count == 0, "Empty collection earns nothing.");
        settings["collectionBadges"] = false;
        settings["definitions"] = JsonNode.Parse("[{\"id\":\"first-complete\",\"name\":\"First complete collection\",\"metric\":\"completedCollections\",\"threshold\":1,\"reward\":7}]");
        var fullyOwned = new JsonObject { ["components"] = new JsonObject { ["a"] = 1 } };
        Require(AchievementEngine.Evaluate(catalog, fullyOwned, settings, Now, true).Count == 1 && fullyOwned["wallet"]!["scrap"]!.GetValue<long>() == 7, "Current full ownership backfills custom completion reward without historical map.");
        Require(fullyOwned["completedCollections"] is null, "Completion progress does not invent historical completion records.");
        settings["definitions"] = JsonNode.Parse("[{\"id\":\"pull-ten\",\"name\":\"Ten pulls\",\"metric\":\"lifetimePulls\",\"threshold\":10,\"reward\":5,\"enabled\":true}]");
        var history = new JsonObject { ["components"] = new JsonObject { ["a"] = 100 } };
        Require(AchievementEngine.Evaluate(catalog, history, settings, Now, false).Count == 0, "Inventory units cannot imply lifetime pulls.");
        history["achievementStats"] = new JsonObject { ["lifetimePulls"] = 10 };
        Require(AchievementEngine.Evaluate(catalog, history, settings, Now, false).Count == 1, "Tracked pulls award custom milestone.");
        settings["definitions"]![0]!["reward"] = 100;
        settings["definitions"]![0]!["name"] = "Edited name";
        Require(AchievementEngine.Evaluate(catalog, history, settings, Now, true).Count == 0 && history["wallet"]!["scrap"]!.GetValue<long>() == 5, "Edited definition cannot regrant reward.");
        Require(AchievementEngine.GetProgress(catalog, history, settings)[0]!["name"]!.GetValue<string>() == "Ten pulls", "Award name remains immutable after rule editing.");
        settings["definitions"]![0]!["threshold"] = -1;
        Require(AchievementEngine.ValidateSettings(settings, catalog).Count > 0, "Negative threshold rejected.");
        settings["definitions"]![0]!["threshold"] = 10;
        settings["definitions"]![0]!["id"] = "builtin:pretend";
        Require(AchievementEngine.ValidateSettings(settings, catalog).Count > 0, "Reserved ID rejected.");
        settings["definitions"] = new JsonArray("bad");
        Require(AchievementEngine.ValidateSettings(settings, catalog).Count > 0, "Malformed definition rejected.");
        Console.WriteLine("PASS: Achievement engine milestones, persistence, backfill, custom rules, validation and atomic reward overflow");
    }
    private static JsonObject Catalog() => JsonNode.Parse("{\"collections\":{\"set\":{\"displayName\":\"Set\",\"parts\":[{\"id\":\"a\"}]}}}")!.AsObject();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
