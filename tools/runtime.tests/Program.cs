using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CircuitOS.Runtime;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CircuitOS.Runtime.SmokeTests <source-data-path> <action-path>");
    return 2;
}

var testPath = Path.Combine(Path.GetTempPath(), "CircuitOSFirstRun-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testPath);

try
{
    foreach (var fileName in new[] { "components.json", "featured-boost.json", "inventory.json" })
        File.Copy(Path.Combine(args[0], fileName), Path.Combine(testPath, fileName));

    // Constructing the store runs the 0.5 migration, moving the flat data files into
    // profiles/default/. Read the inventory hash from there, after migration.
    var store = new LocalFileDataStore(testPath);
    var service = new CircuitService(store, args[1]);
    var profileDir = Path.Combine(testPath, "profiles", "default");
    var inventoryPath = Path.Combine(profileDir, "inventory.json");
    var inventoryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inventoryPath)));
    var defaultProfile = service.GetSystemProfile()["profile"] as JsonObject
        ?? throw new InvalidOperationException("Default profile was unavailable.");
    var profile = JsonUtil.Clone(defaultProfile) as JsonObject
        ?? throw new InvalidOperationException("Default profile could not be cloned.");
    profile["gameName"] = "Test Collection Game";
    profile["adminName"] = "Test Game Admin";

    var components = new JsonObject
    {
        ["schemaVersion"] = 1,
        ["collections"] = new JsonObject
        {
            ["starter"] = new JsonObject
            {
                ["displayName"] = "Starter Collection",
                ["type"] = "permanent",
                ["weight"] = 100,
                ["salvageValue"] = 1,
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["id"] = "starter_alpha", ["name"] = "Alpha" },
                    new JsonObject { ["id"] = "starter_beta", ["name"] = "Beta" },
                    new JsonObject { ["id"] = "starter_gamma", ["name"] = "Gamma" }
                }
            }
        }
    };
    var boost = new JsonObject
    {
        ["enabled"] = false,
        ["displayName"] = "Featured Boost",
        ["collectionMultipliers"] = new JsonObject()
    };
    var request = new JsonObject
    {
        ["profile"] = profile,
        ["configuration"] = new JsonObject { ["components"] = components, ["boost"] = boost }
    };

    var first = service.CompleteFirstRun(request);
    Require(first.Status == 200, "First completion should succeed.");
    Require(File.Exists(Path.Combine(profileDir, "system-profile.json")), "Profile should be written last.");
    Require(Directory.EnumerateFiles(Path.Combine(profileDir, "config-backups")).Any(), "Configuration backups should exist.");
    Require(inventoryHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inventoryPath))), "Inventory must remain unchanged.");

    var saved = service.GetConfiguration();
    var collections = (JsonObject)((JsonObject)saved["components"]!)["collections"]!;
    var parts = (JsonArray)((JsonObject)collections["starter"]!)["parts"]!;
    Require(collections.Count == 1 && parts.Count == 3, "New catalog should contain one three-item collection.");

    var second = service.CompleteFirstRun(request);
    Require(second.Status == 409, "A second completion should be rejected.");

    var setup = service.GetStreamerBotSetup(profile);
    Require(setup.Status == 200, "Streamer.bot action generation should succeed.");
    var actions = setup.Body["actions"] as JsonArray
        ?? throw new InvalidOperationException("Generated Streamer.bot actions were unavailable.");
    Require(actions.Count == 4, "Exactly four Streamer.bot actions should be generated.");
    foreach (var actionNode in actions)
    {
        var action = actionNode as JsonObject
            ?? throw new InvalidOperationException("Generated action was invalid.");
        var key = action["key"]?.ToString() ?? "unknown";
        var source = action["source"]?.ToString() ?? "";
        Require(GetBraceDepth(source) == 0, $"Generated {key} action must have balanced braces.");
        if (key == "redeem")
        {
            var helperIndex = source.IndexOf("private string FormatMessage", StringComparison.Ordinal);
            Require(helperIndex > 0, "Generated redemption action should contain FormatMessage.");
            Require(GetBraceDepth(source, helperIndex) == 1,
                "Generated redemption helper methods must remain at class scope.");
        }
    }

    TestPullEngine();
    TestAppwriteOptions();
    TestTwitchOptions();

    Console.WriteLine("Smoke tests passed: first run is safe, generated Streamer.bot C# is structurally valid, the pull engine distributes correctly, and the Appwrite + Twitch config loaders behave.");
    return 0;
}
finally
{
    if (Directory.Exists(testPath)) Directory.Delete(testPath, true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// Verifies the shared PullEngine: tier-weighted distribution, variant rate + prefix,
// dup protection, and equal-odds fallback. Deterministic via seeded RNGs.
static void TestPullEngine()
{
    var collection = new JsonObject
    {
        ["tiers"] = new JsonArray
        {
            new JsonObject { ["id"] = "common", ["label"] = "COMMON", ["weight"] = 70 },
            new JsonObject { ["id"] = "rare", ["label"] = "RARE", ["weight"] = 25 },
            new JsonObject { ["id"] = "ultra", ["label"] = "ULTRA", ["weight"] = 5 }
        },
        ["variants"] = new JsonArray
        {
            new JsonObject { ["id"] = "shiny", ["label"] = "SHINY", ["chance"] = 0.25 }
        },
        ["parts"] = new JsonArray
        {
            new JsonObject { ["id"] = "c1", ["name"] = "C1", ["tier"] = "common" },
            new JsonObject { ["id"] = "c2", ["name"] = "C2", ["tier"] = "common" },
            new JsonObject { ["id"] = "r1", ["name"] = "R1", ["tier"] = "rare" },
            new JsonObject { ["id"] = "u1", ["name"] = "U1", ["tier"] = "ultra" }
        }
    };

    const int n = 200000;
    var rng = new Random(12345);
    var empty = new Dictionary<string, int>();
    int common = 0, rare = 0, ultra = 0, shiny = 0;
    for (var i = 0; i < n; i++)
    {
        var outcome = PullEngine.Roll(collection, 1.0, empty, 0, 0, rng)
            ?? throw new InvalidOperationException("PullEngine returned null for a valid collection.");
        switch (outcome.TierLabel)
        {
            case "COMMON": common++; break;
            case "RARE": rare++; break;
            case "ULTRA": ultra++; break;
            default: throw new InvalidOperationException($"Unexpected tier label '{outcome.TierLabel}'.");
        }
        if (outcome.VariantLabels.Contains("SHINY"))
        {
            shiny++;
            Require(outcome.DisplayPartName.StartsWith("SHINY "), "Variant prefix should be on the display name.");
        }
    }

    double pc = common * 100.0 / n, pr = rare * 100.0 / n, pu = ultra * 100.0 / n, ps = shiny * 100.0 / n;
    Require(Math.Abs(pc - 70) < 1.5, $"COMMON tier should be ~70% (got {pc:F1}%).");
    Require(Math.Abs(pr - 25) < 1.5, $"RARE tier should be ~25% (got {pr:F1}%).");
    Require(Math.Abs(pu - 5) < 1.0, $"ULTRA tier should be ~5% (got {pu:F1}%).");
    Require(Math.Abs(ps - 25) < 1.5, $"SHINY variant should fire ~25% (got {ps:F1}%).");

    // Dup protection: c1/r1/u1 owned, so only c2 is unowned — it must always be picked.
    var owned = new Dictionary<string, int> { ["c1"] = 1, ["r1"] = 1, ["u1"] = 1 };
    var rngDup = new Random(999);
    for (var i = 0; i < 5000; i++)
    {
        var outcome = PullEngine.Roll(collection, 1.0, owned, 2, 0, rngDup)
            ?? throw new InvalidOperationException("PullEngine returned null under dup protection.");
        Require(outcome.PartId == "c2", $"Dup protection should pick the only unowned item (got '{outcome.PartId}').");
    }

    // No tiers: equal odds, per-item probability = collectionProb / partCount.
    var flat = new JsonObject
    {
        ["parts"] = new JsonArray
        {
            new JsonObject { ["id"] = "a", ["name"] = "A" },
            new JsonObject { ["id"] = "b", ["name"] = "B" }
        }
    };
    var flatOutcome = PullEngine.Roll(flat, 0.5, empty, 0, 0, new Random(1))
        ?? throw new InvalidOperationException("PullEngine returned null for a flat collection.");
    Require(flatOutcome.TierLabel == "", "Flat collection should have no tier label.");
    Require(Math.Abs(flatOutcome.Probability - 0.25) < 1e-9, "Flat per-item probability should be collectionProb / count.");

    Console.WriteLine($"PullEngine: tiers {pc:F1}/{pr:F1}/{pu:F1}% (target 70/25/5), SHINY {ps:F1}% (target 25); dup-protection + flat-odds checks passed.");
}

// Verifies AppwriteOptions.TryLoad: file parsing, env override, defaults, null when
// absent, and validation. Uses a throwaway folder and temporary env vars — no secrets.
static void TestAppwriteOptions()
{
    var dir = Path.Combine(Path.GetTempPath(), "CircuitOSAppwrite-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var envNames = new[]
    {
        "CIRCUITOS_APPWRITE_ENDPOINT", "CIRCUITOS_APPWRITE_PROJECT_ID", "CIRCUITOS_APPWRITE_API_KEY",
        "CIRCUITOS_APPWRITE_DATABASE_ID", "CIRCUITOS_APPWRITE_COLLECTION_ID"
    };
    foreach (var n in envNames) Environment.SetEnvironmentVariable(n, null);
    var file = Path.Combine(dir, AppwriteOptions.FileName);

    try
    {
        // No file, no env → null (stay on local store).
        Require(AppwriteOptions.TryLoad(dir) is null, "No config should yield null.");

        // Full file → all fields load; Describe must not contain the key.
        File.WriteAllText(file, """
        { "endpoint": "https://cloud.appwrite.io/v1", "projectId": "proj123",
          "apiKey": "supersecretkey", "databaseId": "circuitos", "collectionId": "profile_data" }
        """);
        var opts = AppwriteOptions.TryLoad(dir) ?? throw new InvalidOperationException("Config file should load.");
        Require(opts.ProjectId == "proj123" && opts.Endpoint.EndsWith("/v1"), "File fields should load.");
        Require(opts.DatabaseId == "circuitos" && opts.CollectionId == "profile_data", "File db/collection should load.");
        Require(!opts.Describe().Contains("supersecretkey"), "Describe() must not leak the API key.");

        // Env overrides the file.
        Environment.SetEnvironmentVariable("CIRCUITOS_APPWRITE_PROJECT_ID", "envproj");
        var overridden = AppwriteOptions.TryLoad(dir) ?? throw new InvalidOperationException("Config should load with env.");
        Require(overridden.ProjectId == "envproj", "Env var should override the file value.");
        Environment.SetEnvironmentVariable("CIRCUITOS_APPWRITE_PROJECT_ID", null);

        // Missing required field → throws.
        File.WriteAllText(file, """{ "endpoint": "https://cloud.appwrite.io/v1", "apiKey": "k" }""");
        RequireThrows<InvalidDataException>(() => AppwriteOptions.TryLoad(dir), "Missing projectId should throw.");

        // Malformed JSON → throws.
        File.WriteAllText(file, "{ not json");
        RequireThrows<InvalidDataException>(() => AppwriteOptions.TryLoad(dir), "Malformed JSON should throw.");

        // Env-only configuration (no file) works too.
        File.Delete(file);
        foreach (var (n, v) in new[]
        {
            ("CIRCUITOS_APPWRITE_ENDPOINT", "https://cloud.appwrite.io/v1"),
            ("CIRCUITOS_APPWRITE_PROJECT_ID", "p"), ("CIRCUITOS_APPWRITE_API_KEY", "k"),
            ("CIRCUITOS_APPWRITE_DATABASE_ID", "circuitos"), ("CIRCUITOS_APPWRITE_COLLECTION_ID", "profile_data")
        })
            Environment.SetEnvironmentVariable(n, v);
        Require(AppwriteOptions.TryLoad(dir) is not null, "Env-only config should load.");

        Console.WriteLine("AppwriteOptions: file load, env override, env-only, defaults, validation, and key redaction all passed.");
    }
    finally
    {
        foreach (var n in envNames) Environment.SetEnvironmentVariable(n, null);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}

// Verifies TwitchOptions.TryLoad: file parse, default redirect, null when absent,
// validation, and secret redaction. No network.
static void TestTwitchOptions()
{
    var dir = Path.Combine(Path.GetTempPath(), "CircuitOSTwitch-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var file = Path.Combine(dir, TwitchOptions.FileName);
    try
    {
        Require(TwitchOptions.TryLoad(dir) is null, "No twitch config should yield null.");

        File.WriteAllText(file, """{ "clientId": "abc123", "clientSecret": "shh-secret" }""");
        var opts = TwitchOptions.TryLoad(dir) ?? throw new InvalidOperationException("Twitch config should load.");
        Require(opts.ClientId == "abc123", "clientId should load.");
        Require(opts.RedirectUri == TwitchOptions.DefaultRedirectUri, "redirectUri should default when omitted.");
        Require(!opts.Describe().Contains("shh-secret"), "Describe() must not leak the client secret.");

        File.WriteAllText(file, """{ "clientId": "abc123" }""");
        RequireThrows<InvalidDataException>(() => TwitchOptions.TryLoad(dir), "Missing clientSecret should throw.");

        Console.WriteLine("TwitchOptions: file load, default redirect, validation, and secret redaction passed.");
    }
    finally
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}

static void RequireThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    catch (Exception ex) { throw new InvalidOperationException($"{message} (threw {ex.GetType().Name} instead of {typeof(TException).Name})"); }
    throw new InvalidOperationException($"{message} (did not throw)");
}

static int GetBraceDepth(string source, int? endExclusive = null)
{
    var depth = 0;
    var inString = false;
    var inChar = false;
    var inLineComment = false;
    var inBlockComment = false;
    var escaped = false;
    var limit = Math.Min(endExclusive ?? source.Length, source.Length);

    for (var index = 0; index < limit; index++)
    {
        var current = source[index];
        var next = index + 1 < limit ? source[index + 1] : '\0';
        if (inLineComment)
        {
            if (current == '\n') inLineComment = false;
            continue;
        }
        if (inBlockComment)
        {
            if (current == '*' && next == '/') { inBlockComment = false; index++; }
            continue;
        }
        if (inString || inChar)
        {
            if (escaped) { escaped = false; continue; }
            if (current == '\\') { escaped = true; continue; }
            if (inString && current == '"') inString = false;
            if (inChar && current == '\'') inChar = false;
            continue;
        }
        if (current == '/' && next == '/') { inLineComment = true; index++; continue; }
        if (current == '/' && next == '*') { inBlockComment = true; index++; continue; }
        if (current == '"') { inString = true; continue; }
        if (current == '\'') { inChar = true; continue; }
        if (current == '{') depth++;
        if (current == '}') depth--;
        if (depth < 0) return depth;
    }
    return depth;
}
