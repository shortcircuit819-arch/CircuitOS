using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CircuitOS.Runtime;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: CircuitOS.Runtime.SmokeTests <source-data-path>");
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
    var service = new CircuitService(store);
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

    TestFirstRunAllowsDraftCommandCollisions(store, service, profile, components, boost);

    TestActiveProfilesAndCollisions(service, store, defaultProfile);
    TestTwitchRewardPersistence(service, store);
    TestRuntimeDispatch(service, store, testPath);
    TestPullEngine();
    TestRedemptionEngine();
    TestCommandEngine();
    TestAppwriteOptions();
    TestTwitchOptions();
    TestBackupRetention();
    TestCollectionPacks(service, store);
    TestProfileMetaSafetyNet(store, testPath);
    TestThemeNormalization(service);
    TestDesignOverrides(service);
    TestOverlayImageValidation(service);

    // Batch 1 (1.0.1) inventory data-safety regressions.
    TestCorruptInventoryStrictRead();
    TestInventoryBackupBeforeOverwrite();
    TestInventoryBackupPruningBounded();
    TestImportRefusesInventory();
    TestInventoryWriteLockSerialization();

    // Batch 2 (1.0.1) robustness regressions: one bad event collection self-excludes instead of
    // nuking every viewer's pull, naive timestamps are treated as UTC across the gate + the display,
    // and the config validator accepts exactly the windows the engines honor.
    TestBadEventCollectionSelfExcludes();
    TestNaiveTimestampTreatedAsUtc();
    TestEventWindowValidatorMatchesEngine();

    // Batch 3 (1.0.1) security regression: a legacy PLAINTEXT token file is re-encrypted immediately on
    // load (the plaintext window closes on this launch), and an already-encrypted file is never rewritten.
    TestTwitchTokenReEncryptOnLoad();

    Console.WriteLine("Smoke tests passed: first run is safe, the pull + redemption + command engines behave, collection packs round-trip, profiles survive missing metadata, and the Appwrite + Twitch config loaders behave.");
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

// Curated theming (0.8): a profile stores `theme` (a base-theme id) + `accent`. Selecting a base theme
// rewrites the effective `colors` from that palette so the overlay/engine follow; a profile with no
// theme stays "custom" (its own colors), and an unrecognized theme id is dropped, not applied.
static void TestThemeNormalization(CircuitService service)
{
    var baseProfile = service.GetSystemProfile()["profile"] as JsonObject
        ?? throw new InvalidOperationException("Profile was unavailable for the theme test.");

    JsonObject Clone() => (JsonObject)JsonUtil.Clone(baseProfile)!;
    JsonObject SaveAndRead(JsonObject profile, string label)
    {
        var save = service.SaveSystemProfile(profile);
        Require(save.Status == 200, label + " should save.");
        return service.GetSystemProfile()["profile"] as JsonObject
            ?? throw new InvalidOperationException("Profile was unavailable after " + label + ".");
    }

    // 1) A base theme drives the effective colors; the chosen accent flows through.
    var themed = Clone();
    themed["theme"] = "slate";
    themed["accent"] = "#2dd4bf";
    var got1 = SaveAndRead(themed, "A base-theme profile");
    var colors1 = (JsonObject)got1["colors"]!;
    Require(got1["theme"]?.ToString() == "slate", "The base-theme id should persist.");
    Require(got1["accent"]?.ToString() == "#2dd4bf", "The accent should persist.");
    Require(colors1["panel"]?.ToString() == "#161b22", "The base theme should drive colors.panel.");
    Require(colors1["text"]?.ToString() == "#e6edf3", "The base theme should drive colors.text.");
    Require(colors1["accent"]?.ToString() == "#2dd4bf", "The accent should flow into colors.accent.");

    // A newly added base theme resolves through the same path (guards the JS↔C# theme-list mirror).
    var light = Clone();
    light["theme"] = "daylight";
    var gotLight = SaveAndRead(light, "The daylight theme");
    Require(gotLight["theme"]?.ToString() == "daylight", "The daylight theme id should persist.");
    Require(((JsonObject)gotLight["colors"]!)["panel"]?.ToString() == "#ffffff", "Daylight should resolve to a white panel.");
    Require(((JsonObject)gotLight["colors"]!)["background"]?.ToString() == "#e9ebf0", "Daylight should resolve to its light page.");

    // 2) No theme = "custom": the profile keeps its own colors; the accent falls back to colors.accent.
    var custom = Clone();
    custom.Remove("theme");
    custom.Remove("accent");
    ((JsonObject)custom["colors"]!)["panel"] = "#123456";
    ((JsonObject)custom["colors"]!)["accent"] = "#abcdef";
    var got2 = SaveAndRead(custom, "A custom profile");
    Require(got2["theme"] is null, "A custom profile should carry no theme.");
    Require(((JsonObject)got2["colors"]!)["panel"]?.ToString() == "#123456", "Custom colors should be preserved.");
    Require(got2["accent"]?.ToString() == "#abcdef", "The accent should fall back to colors.accent when custom.");

    // 3) An unknown theme id is treated as custom (dropped), never applied over the profile's colors.
    var bogus = Clone();
    bogus.Remove("theme");
    bogus["theme"] = "not-a-real-theme";
    ((JsonObject)bogus["colors"]!)["panel"] = "#0f0f0f";
    var got3 = SaveAndRead(bogus, "An unknown-theme profile");
    Require(got3["theme"] is null, "An unknown theme id should be dropped.");
    Require(((JsonObject)got3["colors"]!)["panel"]?.ToString() == "#0f0f0f", "An unknown theme keeps the profile's own colors.");
}

// Design Mode overrides (0.8 step 5): NormalizeProfile sanitizes the overrides map — whitelisted keys
// with valid values survive; invalid values, non-whitelisted keys, and out-of-range radii are dropped
// (never rejected) so a hand-edited or stale override can't block a save.
static void TestDesignOverrides(CircuitService service)
{
    var baseProfile = service.GetSystemProfile()["profile"] as JsonObject
        ?? throw new InvalidOperationException("Profile was unavailable for the design-overrides test.");

    JsonObject SaveAndRead(JsonObject overrides)
    {
        var profile = (JsonObject)JsonUtil.Clone(baseProfile)!;
        profile["designOverrides"] = overrides;
        var save = service.SaveSystemProfile(profile);
        Require(save.Status == 200, "A profile with design overrides should save.");
        var got = service.GetSystemProfile()["profile"] as JsonObject
            ?? throw new InvalidOperationException("Profile was unavailable after saving design overrides.");
        return got["designOverrides"] as JsonObject
            ?? throw new InvalidOperationException("designOverrides was missing from the saved profile.");
    }

    var ov = SaveAndRead(new JsonObject
    {
        ["--panel"] = "#123456",     // valid structural color → kept
        ["--danger"] = "#abcdef",    // valid status color → kept
        ["--radius"] = "4px",        // valid roundness → kept
        ["--line"] = "not-a-color",  // invalid value → dropped
        ["--bogus"] = "#ffffff"      // not whitelisted → dropped
    });
    Require(ov["--panel"]?.ToString() == "#123456", "A valid structural override should persist.");
    Require(ov["--danger"]?.ToString() == "#abcdef", "A valid status override should persist.");
    Require(ov["--radius"]?.ToString() == "4px", "A valid radius override should persist.");
    Require(ov["--line"] is null, "An invalid color override should be dropped.");
    Require(ov["--bogus"] is null, "A non-whitelisted override should be dropped.");

    var ov2 = SaveAndRead(new JsonObject { ["--radius"] = "40px" });  // out of range
    Require(ov2["--radius"] is null, "An out-of-range radius should be dropped.");
    Require(ov2.Count == 0, "Sanitizing should leave no stray override keys.");
}

// Overlay background uploads are validated by their actual bytes (magic number), never by a
// client-declared Content-Type, so a mislabeled or non-image payload can't be written as a background.
static void TestOverlayImageValidation(CircuitService service)
{
    var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
    var okPng = service.SaveOverlayBackground(png, "");
    Require(okPng.Status == 200, "A real PNG should be accepted as an overlay background.");
    Require(okPng.Body["filename"]?.ToString() == "bg.png", "A PNG background should save as bg.png.");

    var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0 };
    var okGif = service.SaveOverlayBackground(gif, "rare");
    Require(okGif.Status == 200 && okGif.Body["filename"]?.ToString() == "bg-rare.gif", "A per-state GIF should save as bg-rare.gif.");

    var junk = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    Require(service.SaveOverlayBackground(junk, "").Status != 200, "A non-image payload must be rejected.");
    Require(service.SaveOverlayBackground(System.Array.Empty<byte>(), "").Status != 200, "An empty payload must be rejected.");
}

static void TestProfileMetaSafetyNet(IDataStore store, string dataRoot)
{
    var profilesDir = Path.Combine(dataRoot, "profiles");

    // A profile folder with data but NO meta must still be listed (recovered under its folder name).
    var orphanDir = Path.Combine(profilesDir, "orphan-recovered");
    Directory.CreateDirectory(orphanDir);
    File.WriteAllText(Path.Combine(orphanDir, "components.json"), "{\"schemaVersion\":1,\"collections\":{}}");
    Require(store.ListProfiles().Any(p => p.Id == "orphan-recovered"),
        "A profile folder with data but no profile-meta.json must still be listed.");

    // A profile folder with a CORRUPT meta must still be listed.
    var corruptDir = Path.Combine(profilesDir, "corrupt-recovered");
    Directory.CreateDirectory(corruptDir);
    File.WriteAllText(Path.Combine(corruptDir, "system-profile.json"), "{}");
    File.WriteAllText(Path.Combine(corruptDir, "profile-meta.json"), "{ this is not valid json");
    Require(store.ListProfiles().Any(p => p.Id == "corrupt-recovered"),
        "A profile folder with a corrupt profile-meta.json must still be listed.");

    // An empty folder with no profile data must NOT be treated as a profile.
    Directory.CreateDirectory(Path.Combine(profilesDir, "empty-junk"));
    Require(!store.ListProfiles().Any(p => p.Id == "empty-junk"),
        "An empty folder with no profile data must not be listed as a profile.");

    Directory.Delete(orphanDir, true);
    Directory.Delete(corruptDir, true);
    Directory.Delete(Path.Combine(profilesDir, "empty-junk"), true);

    Console.WriteLine("Profile-meta safety net: missing/corrupt metadata recovers the profile; empty folders are ignored.");
}

static void TestCollectionPacks(CircuitService service, IDataStore store)
{
    store.SwitchProfile("default");

    // Add an event collection so we can prove events never travel (single share or share-all).
    var catalog = store.ReadProfileData("default", DataKeys.Catalog) ?? throw new InvalidOperationException("No catalog.");
    var collections = catalog["collections"] as JsonObject ?? throw new InvalidOperationException("No collections.");
    collections["limited"] = new JsonObject
    {
        ["displayName"] = "Limited Event",
        ["type"] = "event",
        ["weight"] = 50,
        ["salvageValue"] = 2,
        ["enabled"] = false,
        ["parts"] = new JsonArray { new JsonObject { ["id"] = "limited_x", ["name"] = "X" } }
    };
    // A second permanent collection so "share all" is genuinely multi-collection.
    collections["extra"] = new JsonObject
    {
        ["displayName"] = "Extra Collection",
        ["type"] = "permanent",
        ["weight"] = 30,
        ["salvageValue"] = 1,
        ["parts"] = new JsonArray { new JsonObject { ["id"] = "extra_a", ["name"] = "A" } }
    };
    store.WriteProfileData("default", DataKeys.Catalog, catalog);

    var export = service.ExportCollectionPack("starter");
    Require(export.Status == 200, "Collection pack export should succeed for an existing collection.");
    var pack = export.Body;
    Require(pack["manifest"]?["format"]?.ToString() == "circuitcollection", "Pack manifest format should be circuitcollection.");
    Require(pack["collections"] is JsonObject sc && sc.Count == 1 && sc["starter"] is not null, "Single pack should carry exactly the shared collection.");
    var packProfile = pack["profile"] as JsonObject;
    Require(packProfile is not null && packProfile["colors"] is null, "Pack must not carry the sharer's colors (theme is the importer's).");
    Require(packProfile!["commands"] is JsonObject, "Pack must carry commands.");

    // Events can't be shared, and share-all excludes them.
    Require(service.ExportCollectionPack("limited").Status != 200, "Exporting an event collection should fail.");
    Require(service.ExportCollectionPack("does-not-exist").Status != 200, "Exporting an unknown collection should fail.");
    var all = service.ExportCollectionPack("*");
    Require(all.Status == 200, "Share-all export should succeed.");
    var allCollections = all.Body["collections"] as JsonObject;
    Require(allCollections is { Count: 2 } && allCollections["starter"] is not null && allCollections["extra"] is not null && allCollections["limited"] is null,
        "Share-all must include ALL permanent collections and exclude events.");
    Require(all.Body["manifest"]?["collectionCount"]?.GetValue<int>() == 2, "collectionCount should match the shared set.");

    var imp1 = service.ImportCollectionPack(pack, "Shared Starter");
    Require(imp1.Status == 200, "Collection pack import should succeed.");
    var id1 = imp1.Body["id"]?.ToString() ?? throw new InvalidOperationException("Import returned no id.");
    Require(imp1.Body["name"]?.ToString() == "Shared Starter", "Imported pack should use the requested name.");
    var importedCollections = store.ReadProfileData(id1, DataKeys.Catalog)?["collections"] as JsonObject;
    Require(importedCollections is { Count: 1 } && importedCollections["starter"] is not null,
        "Imported single pack should contain exactly the shared collection.");

    var impAll = service.ImportCollectionPack(all.Body, "Shared All");
    Require(impAll.Status == 200, "Share-all import should succeed.");
    var allImportedId = impAll.Body["id"]?.ToString() ?? "";
    var allImported = store.ReadProfileData(allImportedId, DataKeys.Catalog)?["collections"] as JsonObject;
    Require(allImported is { Count: 2 } && allImported["starter"] is not null && allImported["extra"] is not null && allImported["limited"] is null,
        "Imported share-all pack should carry all permanent collections only.");

    var imp2 = service.ImportCollectionPack(pack, "Shared Starter");
    Require(imp2.Body["name"]?.ToString() == "Shared Starter (2)", "Duplicate import name should de-dupe to 'Shared Starter (2)'.");

    // A pack whose catalog is invalid is rejected up front and leaves no profile behind.
    var badPack = (JsonObject)all.Body.DeepClone();
    ((JsonObject)((JsonObject)badPack["collections"]!)["starter"]!)["parts"] = new JsonArray();
    var beforeBad = store.ListProfiles().Count;
    Require(service.ImportCollectionPack(badPack, "Should Not Import").Status != 200, "A pack with an invalid catalog must be rejected.");
    Require(store.ListProfiles().Count == beforeBad, "A rejected pack import must not create a profile.");

    foreach (var cleanupId in new[] { id1, allImportedId, imp2.Body["id"]?.ToString() ?? "" })
        try { store.DeleteProfile(cleanupId); } catch { }

    Console.WriteLine("Collection packs: single + share-all round-trip, events never travel, invalid packs are rejected, and duplicate names de-dupe.");
}

// Verifies the active-set model (A) and command-collision guard (B): the default profile is live
// after first-run, new profiles start inactive, activate/deactivate flips the live flag, and a
// profile whose commands collide with a live profile is blocked from activating until renamed.
// First-run initializes the editing draft. It should not be blocked just because another live
// profile already owns the same command words; that guard belongs to activation/go-live.
static void TestFirstRunAllowsDraftCommandCollisions(LocalFileDataStore store, CircuitService service, JsonObject profile, JsonObject components, JsonObject boost)
{
    store.CreateProfile("draft-collision", "Draft Collision");
    store.SwitchProfile("draft-collision");
    try
    {
        var result = service.CompleteFirstRun(new JsonObject
        {
            ["profile"] = JsonUtil.Clone(profile),
            ["configuration"] = new JsonObject
            {
                ["components"] = JsonUtil.Clone(components),
                ["boost"] = JsonUtil.Clone(boost)
            }
        });
        Require(result.Status == 200, "First-run should initialize a draft even when its commands collide with another live profile.");
        Require(store.ReadProfileData("draft-collision", DataKeys.Profile) is not null, "Draft first-run should save the profile.");
        Require(store.ListProfiles().Any(p => p.Id == "draft-collision" && !p.IsLive), "Draft first-run should not automatically make the profile live.");
        var blocked = service.InvokeProfileOperation(new JsonObject { ["operation"] = "activate", ["id"] = "draft-collision" });
        Require(blocked.Status != 200, "Activating the colliding draft should still be blocked until commands are renamed.");
    }
    finally
    {
        store.SwitchProfile("default");
    }

    Console.WriteLine("First-run drafts: command collisions are allowed while initializing, then blocked at go-live.");
}
static void TestActiveProfilesAndCollisions(CircuitService service, IDataStore store, JsonObject defaultProfile)
{
    Require(store.ListProfiles().Any(p => p.Id == "default" && p.IsLive), "Default profile should be live after first-run.");

    var profileId = "second-" + Guid.NewGuid().ToString("N")[..8];
    store.CreateProfile(profileId, "Second Game");
    Require(store.ListProfiles().Any(p => p.Id == profileId && !p.IsLive), "New profiles should start inactive.");
    store.SetProfileActive(profileId, true);
    Require(store.ListProfiles().Any(p => p.Id == profileId && p.IsLive), "SetProfileActive(true) should make a profile live.");
    store.SetProfileActive(profileId, false);
    Require(store.ListProfiles().Any(p => p.Id == profileId && !p.IsLive), "SetProfileActive(false) should clear the live flag.");

    // Collision: give the new profile the SAME commands as the live default profile, then try to activate it.
    var colliding = JsonUtil.Clone(defaultProfile) as JsonObject ?? throw new InvalidOperationException("Profile clone failed.");
    store.ImportProfileData(profileId, new Dictionary<string, JsonNode> { ["profile"] = colliding });
    var blocked = service.InvokeProfileOperation(new JsonObject { ["operation"] = "activate", ["id"] = profileId });
    Require(blocked.Status != 200, "Activating a profile whose commands collide with a live profile should be blocked.");
    Require(store.ListProfiles().Any(p => p.Id == profileId && !p.IsLive), "A blocked activation must leave the profile inactive.");

    // Unique commands but same redemption title -> activation is still blocked, because native
    // Twitch reward sync would otherwise collapse both profiles onto one channel-point reward.
    var rewardCollision = JsonUtil.Clone(defaultProfile) as JsonObject ?? throw new InvalidOperationException("Profile clone failed.");
    var rewardCommands = (JsonObject)rewardCollision["commands"]!;
    foreach (var key in rewardCommands.Select(kv => kv.Key).ToList()) rewardCommands[key] = "r" + rewardCommands[key]!;
    store.ImportProfileData(profileId, new Dictionary<string, JsonNode> { ["profile"] = rewardCollision });
    var rewardBlocked = service.InvokeProfileOperation(new JsonObject { ["operation"] = "activate", ["id"] = profileId });
    Require(rewardBlocked.Status != 200, "Activating a profile whose redemption title collides with a live profile should be blocked.");
    Require(store.ListProfiles().Any(p => p.Id == profileId && !p.IsLive), "A blocked reward-title activation must leave the profile inactive.");

    // Unique commands + unique redemption title -> activation succeeds.
    var unique = JsonUtil.Clone(defaultProfile) as JsonObject ?? throw new InvalidOperationException("Profile clone failed.");
    var commands = (JsonObject)unique["commands"]!;
    foreach (var key in commands.Select(kv => kv.Key).ToList()) commands[key] = "z" + commands[key]!;
    unique["redemptionName"] = "Second Game Reward";
    store.ImportProfileData(profileId, new Dictionary<string, JsonNode> { ["profile"] = unique });
    var allowed = service.InvokeProfileOperation(new JsonObject { ["operation"] = "activate", ["id"] = profileId });
    Require(allowed.Status == 200, "Activating a profile with unique commands and a unique redemption title should succeed.");
    Require(store.ListProfiles().Any(p => p.Id == profileId && p.IsLive), "Successful activation should make the profile live.");

    store.SetProfileActive(profileId, false);
    Console.WriteLine("Active profiles + collisions: live flags, activate/deactivate, command guard, and redemption-title guard all passed.");
}

// Verifies the shared PullEngine: tier-weighted distribution, variant rate + prefix,
// dup protection, and equal-odds fallback. Deterministic via seeded RNGs.
static void TestBackupRetention()
{
    var dir = Path.Combine(Path.GetTempPath(), "CircuitOSBackupRetention-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var store = new LocalFileDataStore(dir);
        var node = new JsonObject { ["schemaVersion"] = 1, ["collections"] = new JsonObject() };
        // First write creates the file (no backup); each later write backs up the prior content.
        for (var i = 0; i < 8; i++)
            store.WriteAtomic(DataKeys.Catalog, node, "components", $"20260101_0000{i:00}_000");
        var before = store.ListBackups().Count(b => b.Key == DataKeys.Catalog);
        Require(before == 7, $"Expected 7 catalog backups before pruning, got {before}.");
        store.PruneBackups(3);
        var after = store.ListBackups().Count(b => b.Key == DataKeys.Catalog);
        Require(after == 3, $"Retention should keep 3 backups, kept {after}.");
        store.PruneBackups(0);
        Require(store.ListBackups().Count(b => b.Key == DataKeys.Catalog) == 3, "Retention 0 (keep all) must not delete backups.");
        Console.WriteLine("Backup retention: backups accumulate, PruneBackups trims to the N most recent, 0 keeps all.");
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

// ── Batch 1 data-safety regressions (CircuitOS 1.0.1) ─────────────────────────
// Lock in the inventory write-path guards: a corrupt inventory must never read as empty (the wipe
// guard), every overwrite snapshots the prior file first, snapshots stay bounded, imports never touch
// inventory, and the per-profile lock serializes concurrent read-modify-writes with no lost update.
// Each test is hermetic — its own temp data folder + LocalFileDataStore, matching TestBackupRetention.

static string FreshDataDir()
{
    var dir = Path.Combine(Path.GetTempPath(), "CircuitOSBatch1-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
}

static JsonObject Viewer(string name) => new()
{
    ["displayName"] = name,
    ["components"] = new JsonObject { ["starter_alpha"] = 1 }
};

// (1) A present-but-corrupt inventory must THROW on the strict read the write path uses — never read as
// empty (which the caller would then write back, wiping every viewer). ReadProfileData stays lenient
// (null), and a genuinely absent inventory is null from BOTH. This is the guard that prevents the wipe.
static void TestCorruptInventoryStrictRead()
{
    var dir = FreshDataDir();
    try
    {
        var store = new LocalFileDataStore(dir);

        // Absent inventory: null from both reads (there is nothing to protect yet).
        Require(store.ReadProfileData("default", DataKeys.Inventory) is null, "Absent inventory should read as null (lenient).");
        Require(store.ReadProfileDataStrict("default", DataKeys.Inventory) is null, "Absent inventory should read as null (strict).");

        // Corrupt the inventory file with non-JSON garbage.
        var invPath = Path.Combine(dir, "profiles", "default", "inventory.json");
        File.WriteAllText(invPath, "{ this is not valid json >>> garbage");

        // Lenient read swallows it → null (the very "looks empty" trap the strict read exists to stop).
        Require(store.ReadProfileData("default", DataKeys.Inventory) is null, "Corrupt inventory reads as null on the lenient path.");
        // Strict read THROWS — corruption can never masquerade as "no data" and get overwritten.
        RequireThrows<Exception>(() => store.ReadProfileDataStrict("default", DataKeys.Inventory),
            "A corrupt inventory must throw on the strict read, never read as empty.");

        Console.WriteLine("Corrupt inventory: strict read throws (wipe guard), lenient read is null, absent is null from both.");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// (2) Overwriting an existing inventory must snapshot the PRIOR contents into config-backups first, as a
// managed inventory_<yyyyMMdd_HHmmss_fff>.json that ListBackups()/FindBackup recognize (not orphaned).
static void TestInventoryBackupBeforeOverwrite()
{
    var dir = FreshDataDir();
    try
    {
        var store = new LocalFileDataStore(dir);

        // First write: the file does not exist yet, so there is nothing to back up.
        store.WriteProfileData("default", DataKeys.Inventory, new JsonObject { ["viewer-a"] = Viewer("A") });
        Require(store.ListBackups().Count(b => b.Key == DataKeys.Inventory) == 0,
            "The first inventory write (no prior file) must not create a backup.");

        // Overwrite: the prior contents must be snapshotted before the new bytes land.
        store.WriteProfileData("default", DataKeys.Inventory,
            new JsonObject { ["viewer-a"] = Viewer("A"), ["viewer-b"] = Viewer("B") });

        var invBackups = store.ListBackups().Where(b => b.Key == DataKeys.Inventory).ToList();
        Require(invBackups.Count == 1, $"Overwriting an existing inventory should create exactly one managed backup (got {invBackups.Count}).");

        // ListBackups recognizing it (Key == Inventory, "Viewer Inventory") proves the filename matches
        // the managed inventory_<timestamp>.json shape — i.e. it is NOT an orphaned file.
        var entry = invBackups[0];
        Require(entry.Label == "Viewer Inventory", "The inventory snapshot should be a recognized managed backup, not orphaned.");
        Require(store.FindBackup(entry.FileName).Key == DataKeys.Inventory, "FindBackup should resolve the inventory snapshot as managed.");

        // And it captured the PRIOR contents (viewer-a only), not the new inventory.
        var snapshot = store.ReadBackupJson(entry.FileName);
        Require(snapshot.ContainsKey("viewer-a") && !snapshot.ContainsKey("viewer-b"),
            "The snapshot must capture the PRIOR inventory (viewer-a only), taken before the overwrite.");

        Console.WriteLine("Inventory overwrite: prior contents are snapshotted first and recognized by ListBackups (not orphaned).");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// (3) Many successive inventory writes must leave at most `backupRetention` snapshots, not unbounded.
static void TestInventoryBackupPruningBounded()
{
    Require(LocalFileDataStore.DefaultBackupRetention == 30, "The default inventory backup retention should be 30.");

    var dir = FreshDataDir();
    try
    {
        var store = new LocalFileDataStore(dir);
        const int keep = 5;
        AppSettings.Set(dir, "backupRetention", keep);

        // Seed the file (first write makes no backup), then overwrite far more times than the retention.
        // Every overwrite snapshots the prior file; the store must prune back to `keep`.
        store.WriteProfileData("default", DataKeys.Inventory, new JsonObject { ["v0"] = Viewer("v0") });
        for (var i = 1; i <= keep + 8; i++)
        {
            WaitForNextTimestamp();  // distinct millisecond → snapshots don't collide by filename
            store.WriteProfileData("default", DataKeys.Inventory, new JsonObject { [$"v{i}"] = Viewer($"v{i}") });
        }

        var count = store.ListBackups().Count(b => b.Key == DataKeys.Inventory);
        Require(count == keep, $"Successive inventory writes must prune to backupRetention ({keep}); found {count}.");

        Console.WriteLine($"Inventory backup pruning: {keep + 8} overwrites left exactly {keep} snapshots (bounded, not unbounded).");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// Busy-waits until the millisecond-resolution timestamp advances, so successive inventory snapshots get
// distinct filenames (the store names them inventory_yyyyMMdd_HHmmss_fff.json).
static void WaitForNextTimestamp()
{
    var start = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
    while (DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") == start) { }
}

// (4) ImportProfileData must refuse any inventory entry and never write inventory.json — imports target a
// freshly created profile and must never become a silent, unbacked, non-atomic inventory clobber.
static void TestImportRefusesInventory()
{
    var dir = FreshDataDir();
    try
    {
        var store = new LocalFileDataStore(dir);
        store.CreateProfile("import-target", "Import Target");
        var invPath = Path.Combine(dir, "profiles", "import-target", "inventory.json");

        RequireThrows<InvalidDataException>(
            () => store.ImportProfileData("import-target", new Dictionary<string, JsonNode>
            {
                [DataKeys.Inventory] = new JsonObject { ["viewer-x"] = Viewer("X") }
            }),
            "Importing an inventory entry must be refused.");
        Require(!File.Exists(invPath), "A refused inventory import must never write inventory.json.");

        // Even mixed into a multi-key import, inventory is still refused and never written.
        RequireThrows<InvalidDataException>(
            () => store.ImportProfileData("import-target", new Dictionary<string, JsonNode>
            {
                [DataKeys.Boost] = new JsonObject { ["enabled"] = false },
                [DataKeys.Inventory] = new JsonObject { ["viewer-y"] = Viewer("Y") }
            }),
            "A multi-key import carrying inventory must still be refused.");
        Require(!File.Exists(invPath), "A refused mixed import must still never write inventory.json.");

        Console.WriteLine("Import guard: ImportProfileData refuses any inventory entry and never writes inventory.json.");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// (5) The per-profile inventory lock must serialize concurrent read-modify-writes so no update is lost.
// Both parallel redeems and a redeem racing an admin edit share InventoryLock(profileId). The redeem
// path, however, does one UNLOCKED pre-read of inventory.json (CircuitService.Core.cs) that races at the
// OS file level with a concurrent atomic replace — that would make a redeem-driven parallel test flaky
// for reasons unrelated to the lock. So this drives concurrent admin removals (ResetViewer), whose read
// AND write are both inside the lock: the focused, deterministic test of the lock's correctness. N
// threads each remove a distinct viewer from one shared inventory; a broken lock would let a
// last-write-wins overwrite resurrect already-removed viewers, leaving some removals lost.
static void TestInventoryWriteLockSerialization()
{
    var dir = FreshDataDir();
    try
    {
        var store = new LocalFileDataStore(dir);
        var service = new CircuitService(store);
        const int n = 60;

        var seed = new JsonObject();
        for (var i = 0; i < n; i++) seed[$"viewer-{i}"] = Viewer($"Viewer {i}");
        store.WriteProfileData(store.ActiveProfileId, DataKeys.Inventory, seed);

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, n, i =>
        {
            try
            {
                var res = service.ResetViewer(new JsonObject { ["viewerId"] = $"viewer-{i}" });
                if (res.Status != 200) failures.Add($"viewer-{i}:{res.Status}");
            }
            catch (Exception ex) { failures.Add($"viewer-{i}:{ex.GetType().Name}"); }
        });
        Require(failures.IsEmpty, "Every concurrent inventory removal should succeed: " + string.Join(", ", failures));

        var remaining = store.ReadProfileDataStrict(store.ActiveProfileId, DataKeys.Inventory) ?? new JsonObject();
        Require(remaining.Count == 0,
            $"The inventory lock must serialize concurrent writes with no lost update: expected 0 viewers left, found {remaining.Count}.");

        Console.WriteLine($"Inventory write lock: {n} concurrent read-modify-write removals serialized with no lost update.");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

static void TestRuntimeDispatch(CircuitService service, IDataStore store, string dataRoot)
{
    var profileId = "dispatch-" + Guid.NewGuid().ToString("N")[..8];
    store.CreateProfile(profileId, "Second Game");

    var defaultProfile = service.GetSystemProfile()["profile"] as JsonObject
        ?? throw new InvalidOperationException("Default profile was unavailable for runtime-dispatch test.");

    var secondProfile = JsonUtil.Clone(defaultProfile) as JsonObject
        ?? throw new InvalidOperationException("Second profile clone failed.");
    var commands = (JsonObject)secondProfile["commands"]!;
    commands["inventory"] = "shop";
    commands["missing"] = "missing2";
    commands["duplicates"] = "dupes2";
    commands["leaderboard"] = "board2";
    commands["balance"] = "scrap2";
    commands["collection"] = "collection2";
    commands["salvage"] = "salvage2";
    secondProfile["gameName"] = "Second Game";
    secondProfile["redemptionName"] = "Second Game Reward";

    var catalog = new JsonObject
    {
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
                    new JsonObject { ["id"] = "starter_beta", ["name"] = "Beta" }
                }
            }
        }
    };

    var boost = new JsonObject { ["enabled"] = false, ["displayName"] = "Featured Boost", ["collectionMultipliers"] = new JsonObject() };
    var inventory = new JsonObject();

    store.ImportProfileData(profileId, new Dictionary<string, JsonNode>
    {
        [DataKeys.Profile] = secondProfile,
        [DataKeys.Catalog] = catalog,
        [DataKeys.Boost] = boost
    });
    // Import refuses inventory now (Batch 1 data-safety fix), so seed the starting empty inventory
    // through the sanctioned write path instead of ImportProfileData.
    store.WriteProfileData(profileId, DataKeys.Inventory, inventory);

    var activate = service.InvokeProfileOperation(new JsonObject { ["operation"] = "activate", ["id"] = profileId });
    Require(activate.Status == 200, "Second profile should activate for runtime-dispatch test.");

    var commandResult = service.DispatchRuntimeAction(new JsonObject
    {
        ["action"] = "command",
        ["profileId"] = profileId,
        ["command"] = "shop",
        ["viewerId"] = "viewer-a",
        ["viewerName"] = "Viewer A"
    });
    Require(commandResult.Status == 200, "Command dispatch should succeed.");
    Require(commandResult.Body["profileId"]?.ToString() == profileId, "Command dispatch should target the matching live profile.");

    var redeemResult = service.DispatchRuntimeAction(new JsonObject
    {
        ["action"] = "redeem",
        ["profileId"] = profileId,
        ["viewerId"] = "viewer-b",
        ["viewerName"] = "Viewer B",
        ["rngSeed"] = 7
    });
    Require(redeemResult.Status == 200, "Redemption dispatch should succeed.");
    var secondInventory = store.ReadProfileData(profileId, DataKeys.Inventory)
        ?? throw new InvalidOperationException("Redemption dispatch should write inventory for the selected profile.");
    Require(secondInventory["viewer-b"] is JsonObject, "Redemption dispatch should create viewer inventory inside the selected profile.");

    // A native redemption must drive the OBS overlay: overlay-state.json is written to the target
    // profile's overlay folder.
    var overlayStatePath = Path.Combine(dataRoot, "profiles", profileId, "overlay", "overlay-state.json");
    Require(File.Exists(overlayStatePath), "Redemption dispatch should write overlay-state.json for the overlay.");
    var overlayState = JsonNode.Parse(File.ReadAllText(overlayStatePath)) as JsonObject
        ?? throw new InvalidOperationException("overlay-state.json should be a JSON object.");
    Require(overlayState["viewerName"]?.ToString() == "Viewer B", "Overlay state should carry the redeeming viewer's name.");
    Require(!string.IsNullOrWhiteSpace(overlayState["partName"]?.ToString()), "Overlay state should carry the pulled part name.");
    Require(overlayState["version"]?.GetValue<int>() == 1, "Overlay state should be schema version 1.");

    // A native !salvage mutates inventory (consumes duplicates); the dispatch must persist it —
    // previously the command path mutated in memory and dropped the change.
    var seeded = store.ReadProfileData(profileId, DataKeys.Inventory) ?? new JsonObject();
    seeded["viewer-c"] = new JsonObject
    {
        ["displayName"] = "Viewer C",
        ["components"] = new JsonObject { ["starter_alpha"] = 3 }
    };
    store.WriteProfileData(profileId, DataKeys.Inventory, seeded);

    var salvageResult = service.DispatchRuntimeAction(new JsonObject
    {
        ["action"] = "command",
        ["profileId"] = profileId,
        ["command"] = "salvage2",
        ["arg"] = "all",
        ["viewerId"] = "viewer-c",
        ["viewerName"] = "Viewer C"
    });
    Require(salvageResult.Status == 200, "Salvage command dispatch should succeed.");
    var afterSalvage = store.ReadProfileData(profileId, DataKeys.Inventory)
        ?? throw new InvalidOperationException("Inventory should exist after salvage.");
    var salvagedComponents = (afterSalvage["viewer-c"] as JsonObject)?["components"] as JsonObject;
    Require(salvagedComponents?["starter_alpha"]?.GetValue<int>() == 1,
        "Native salvage must persist the consumed duplicates (starter_alpha should drop from 3 to 1).");

    Console.WriteLine("Runtime dispatch: command + redemption resolve to the live profile, drive the overlay state, and persist inventory (incl. salvage).");
}

static void TestTwitchRewardPersistence(CircuitService service, IDataStore store)
{
    var map = TwitchRuntime.BuildRewardMap(store,
        (title, cost, prompt) => new CustomReward("reward-circuit-component", title, cost),
        _ => { });
    Require(map.TryGetValue("reward-circuit-component", out var profileId) && profileId == "default",
        "Twitch reward map should route the persisted reward id to the live profile.");

    var state = store.ReadProfileData("default", DataKeys.TwitchRewards)
        ?? throw new InvalidOperationException("Twitch reward state should be written to the live profile.");
    var reward = (JsonObject)((JsonObject)state["rewards"]!)["channelPoints"]!;
    Require(reward["rewardId"]?.ToString() == "reward-circuit-component", "Stored Twitch reward id should match the created reward.");
    Require(reward["title"]?.ToString() == "Circuit Component", "Stored Twitch reward title should match the profile redemption name.");

    var profiles = (JsonArray)service.GetProfiles()["profiles"]!;
    var defaultProfile = profiles.OfType<JsonObject>().First(p => p["id"]?.ToString() == "default");
    var surfaced = defaultProfile["twitchReward"] as JsonObject
        ?? throw new InvalidOperationException("Profiles API should surface the Twitch reward summary.");
    Require(surfaced["rewardId"]?.ToString() == "reward-circuit-component", "Profiles API should expose the stored reward id.");

    var updateCalls = new List<string>();
    var updated = TwitchRuntime.UpdateRewardForProfile(store, "default", "Updated Circuit Reward", 250,
        (rewardId, title, cost, prompt) =>
        {
            updateCalls.Add($"{rewardId}|{title}|{cost}");
            return new CustomReward(rewardId, title, cost, Manageable: true);
        }, _ => { });
    Require(updated.Title == "Updated Circuit Reward" && updated.Cost == 250, "Twitch reward edit should return the updated title and cost.");
    Require(updateCalls.SequenceEqual(new[] { "reward-circuit-component|Updated Circuit Reward|250" }), "Twitch reward edit should call the provider with the stored reward id, title, and cost.");
    var profileData = store.ReadProfileData("default", DataKeys.Profile)
        ?? throw new InvalidOperationException("Profile data should remain readable after Twitch reward edit.");
    Require(profileData["redemptionName"]?.ToString() == "Updated Circuit Reward", "Editing a managed Twitch reward should keep the profile redemption name in sync.");
    var updatedState = store.ReadProfileData("default", DataKeys.TwitchRewards)
        ?? throw new InvalidOperationException("Twitch reward state should remain readable after edit.");
    var updatedReward = (JsonObject)((JsonObject)updatedState["rewards"]!)["channelPoints"]!;
    Require(updatedReward["title"]?.ToString() == "Updated Circuit Reward", "Stored Twitch reward title should update after edit.");
    Require(updatedReward["cost"]?.GetValue<int>() == 250, "Stored Twitch reward cost should update after edit.");

    var deletedIds = new List<string>();
    var deleted = TwitchRuntime.DeleteRewardForProfile(store, "default", deletedIds.Add, _ => { });
    Require(deleted.Id == "reward-circuit-component", "Deleted Twitch reward should match the stored reward id.");
    Require(deletedIds.SequenceEqual(new[] { "reward-circuit-component" }), "Twitch reward delete should call the provider with the stored reward id.");
    var afterDelete = store.ReadProfileData("default", DataKeys.TwitchRewards)
        ?? throw new InvalidOperationException("Twitch reward state should remain readable after delete.");
    var afterRewards = (JsonObject)afterDelete["rewards"]!;
    Require(!afterRewards.ContainsKey("channelPoints"), "Deleting a Twitch reward should clear the stored channel-point mapping.");
    var profilesAfterDelete = (JsonArray)service.GetProfiles()["profiles"]!;
    var defaultAfterDelete = profilesAfterDelete.OfType<JsonObject>().First(p => p["id"]?.ToString() == "default");
    Require(defaultAfterDelete["twitchReward"] is null, "Profiles API should stop surfacing a deleted Twitch reward.");

    var attached = TwitchRuntime.AttachRewardForProfile(store, "default", new CustomReward("manual-reward", "Manual Reward", 250, Manageable: false), _ => { });
    Require(attached.Id == "manual-reward", "Attached Twitch reward should return the selected reward id.");
    var profilesAfterAttach = (JsonArray)service.GetProfiles()["profiles"]!;
    var defaultAfterAttach = profilesAfterAttach.OfType<JsonObject>().First(p => p["id"]?.ToString() == "default");
    var attachedSummary = defaultAfterAttach["twitchReward"] as JsonObject
        ?? throw new InvalidOperationException("Profiles API should surface an attached Twitch reward.");
    Require(attachedSummary["rewardId"]?.ToString() == "manual-reward", "Profiles API should expose the attached reward id.");
    Require(attachedSummary["manageable"]?.GetValue<bool>() == false, "Attached non-manageable rewards should be marked attach-only.");
    var attachedMap = TwitchRuntime.BuildRewardMap(store,
        (title, cost, prompt) => throw new InvalidOperationException("Stored attached rewards should not create a new reward."),
        _ => { });
    Require(attachedMap.TryGetValue("manual-reward", out var attachedProfileId) && attachedProfileId == "default",
        "Stored attached Twitch reward should route to the live profile.");
    var attachedRoutes = TwitchRuntime.BuildRewardRoutes(store,
        (title, cost, prompt) => throw new InvalidOperationException("Stored attached rewards should not create a new reward."),
        _ => { });
    Require(attachedRoutes.TryGetValue("manual-reward", out var attachedRoute) && attachedRoute.ProfileId == "default" && !attachedRoute.Manageable,
        "Native Twitch route map should preserve attach-only reward status so fulfillment is skipped.");

    var duplicateRewardProfile = "twitch-duplicate-" + Guid.NewGuid().ToString("N")[..8];
    store.CreateProfile(duplicateRewardProfile, "Duplicate Reward Profile");
    store.SetProfileActive(duplicateRewardProfile, true);
    try
    {
        RequireThrows<InvalidDataException>(() => TwitchRuntime.AttachRewardForProfile(store, duplicateRewardProfile, new CustomReward("manual-reward", "Manual Reward", 250, Manageable: false), _ => { }),
            "A live profile should not be allowed to attach a Twitch reward id already mapped to another live profile.");
    }
    finally
    {
        store.SetProfileActive(duplicateRewardProfile, false);
    }

    var deleteAttempts = new List<string>();
    RequireThrows<InvalidDataException>(() => TwitchRuntime.DeleteRewardForProfile(store, "default", deleteAttempts.Add, _ => { }),
        "Attach-only Twitch rewards should not be deleted through CircuitOS.");
    Require(deleteAttempts.Count == 0, "Attach-only Twitch reward delete should not call the provider.");

    Console.WriteLine("Twitch rewards: reward id persistence, edit, attach existing, profile summary exposure, and delete cleanup passed.");
}
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

    var duplicateVariantCollection = new JsonObject
    {
        ["variants"] = new JsonArray
        {
            new JsonObject { ["id"] = "shiny_a", ["label"] = "SHINY", ["chance"] = 1.0 },
            new JsonObject { ["id"] = "shiny_b", ["label"] = " shiny ", ["chance"] = 1.0 },
            new JsonObject { ["id"] = "large", ["label"] = "LARGE", ["chance"] = 1.0 }
        },
        ["parts"] = new JsonArray { new JsonObject { ["id"] = "dup_variant", ["name"] = "Variant Test" } }
    };
    var duplicateVariantOutcome = PullEngine.Roll(duplicateVariantCollection, 1.0, empty, 0, 0, new Random(2))
        ?? throw new InvalidOperationException("PullEngine returned null for duplicate variant labels.");
    Require(duplicateVariantOutcome.VariantLabels.SequenceEqual(new[] { "SHINY", "LARGE" }),
        "Variant labels should be trimmed, deduped case-insensitively, and still allow a second unique label.");
    Require(!duplicateVariantOutcome.DisplayPartName.Contains("SHINY SHINY", StringComparison.OrdinalIgnoreCase),
        "Variant display name should not repeat duplicate-looking labels.");

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

// Verifies the shared RedemptionEngine: weighted collection selection, event-window gating,
// and inventory application (new item, duplicate, completion detection, identical-pull streak).
// Deterministic via seeded RNGs.
static void TestRedemptionEngine()
{
    // Weighted collection selection — 90/10 split over two permanent collections.
    var twoCollections = new JsonObject
    {
        ["big"] = MakeCollection("Big", 90, "b1", "b2"),
        ["small"] = MakeCollection("Small", 10, "s1", "s2")
    };
    const int n = 100000;
    var rng = new Random(4242);
    var big = 0;
    for (var i = 0; i < n; i++)
        if (RedemptionEngine.SelectCollection(twoCollections, null, DateTimeOffset.UtcNow, rng).Key == "big") big++;
    var pBig = big * 100.0 / n;
    Require(Math.Abs(pBig - 90) < 1.5, $"Weighted collection selection should be ~90% big (got {pBig:F1}%).");

    // Event-window gating — a festival collection is selectable inside its window, excluded outside.
    var withEvent = new JsonObject
    {
        ["always"] = MakeCollection("Always", 50, "a1"),
        ["festival"] = MakeEvent("Festival", 50, true, "2026-06-01T00:00:00Z", "2026-06-30T00:00:00Z", "f1")
    };
    var inside = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    var outside = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
    var rngEvent = new Random(7);
    var sawFestival = false;
    for (var i = 0; i < 2000 && !sawFestival; i++)
        if (RedemptionEngine.SelectCollection(withEvent, null, inside, rngEvent).Key == "festival") sawFestival = true;
    Require(sawFestival, "Active event collection should be selectable inside its window.");
    for (var i = 0; i < 5000; i++)
        Require(RedemptionEngine.SelectCollection(withEvent, null, outside, rngEvent).Key == "always",
            "Event collection must be excluded outside its window.");

    // Inventory application — first pull of a part: quantity 1, not a duplicate, streak starts at 1.
    var solo = new JsonObject { ["collections"] = new JsonObject { ["solo"] = MakeCollection("Solo", 100, "x1", "x2") } };
    var inv1 = new JsonObject();
    var first = RedemptionEngine.ApplyRedemption(solo, null, inv1, "viewer1", "Viewer One", DateTimeOffset.UtcNow, new Random(1));
    Require(first.Quantity == 1 && !first.IsDuplicate, "First pull of a part should be quantity 1, not a duplicate.");
    Require(first.ConsecutivePullCount == 1, "First pull should start a streak at 1.");
    Require(((JsonObject)inv1["viewer1"]!)["components"] is JsonObject parts1 && parts1.Count == 1,
        "Inventory should record exactly one component after the first pull.");

    // Completion detection — viewer owns p1 of a 2-part set; dup protection forces the missing p2,
    // which completes the collection and flags newlyCompleted.
    var pair = new JsonObject { ["collections"] = new JsonObject { ["pair"] = MakeCollection("Pair", 100, "p1", "p2") } };
    var inv2 = new JsonObject
    {
        ["v2"] = new JsonObject
        {
            ["displayName"] = "V2",
            ["components"] = new JsonObject { ["p1"] = 1 },
            ["pullsSinceLastDup"] = 0
        }
    };
    var completion = RedemptionEngine.ApplyRedemption(pair, null, inv2, "v2", "V2", DateTimeOffset.UtcNow, new Random(3), dupProtectionTurns: 2);
    Require(completion.Pull.PartId == "p2", "Dup protection should force the only missing part (p2).");
    Require(completion.NewlyCompleted && completion.OwnedAfter == 2 && completion.TotalParts == 2,
        "Completing the set should flag newlyCompleted with ownedAfter == total.");

    // Duplicate + triple streak — pulling the only part three times reports quantity 3 and a streak of 3.
    var single = new JsonObject { ["collections"] = new JsonObject { ["one"] = MakeCollection("One", 100, "only") } };
    var inv3 = new JsonObject();
    var rngStreak = new Random(11);
    RedemptionResult third = null!;
    for (var i = 0; i < 3; i++)
        third = RedemptionEngine.ApplyRedemption(single, null, inv3, "v3", "V3", DateTimeOffset.UtcNow, rngStreak);
    Require(third.Quantity == 3 && third.IsDuplicate, "Third pull of the only part should be quantity 3 (a duplicate).");
    Require(third.ConsecutivePullCount == 3, "Three identical pulls should report a streak of 3.");

    Console.WriteLine($"RedemptionEngine: collection weighting {pBig:F1}% (target 90), event gating in/out, new/dup/completion/streak application all passed.");
}

// Verifies the shared CommandEngine: inventory/missing/duplicates/balance/collection/leaderboard
// output and the salvage write (consumes extras, credits the wallet, mutates inventory).
static void TestCommandEngine()
{
    var ctx = new CommandContext(
        GameName: "Circuit",
        ItemSingular: "component", ItemPlural: "components",
        CollectionSingular: "collection", RedemptionName: "Circuit Component", CurrencyName: "Scrap",
        CollectionCommand: "collection", SalvageCommand: "salvage",
        NoInventoryTemplate: "@{viewer} you don't have any {itemPlural} yet. Redeem {redemption} to start your {collectionSingular}.",
        BalanceTemplate: "@{viewer} {currency} balance: {balance}.",
        NoDuplicatesTemplate: "@{viewer} you don't have any duplicate {itemPlural} yet.",
        CollectionUsageTemplate: "@{viewer} usage: !{collectionCommand} <{collectionSingular}>",
        CollectionSummaryTemplate: "@{viewer} {collection}: {owned}/{total} | {status}{availability}",
        SalvageUsageTemplate: "@{viewer} usage: !{salvageCommand} <{collectionSingular}> or !{salvageCommand} all",
        NothingToSalvageTemplate: "@{viewer} you have no extra copies to salvage in {selection}.",
        SalvageSuccessTemplate: "@{viewer} salvaged {count} extra {itemWord} for {earned} {currency}. Balance: {balance}.");

    var catalog = new JsonObject
    {
        ["collections"] = new JsonObject
        {
            ["basic"] = CommandCollection("Basic Collection", 1, ("basic_a", "Alpha"), ("basic_b", "Beta"), ("basic_c", "Gamma")),
            ["power"] = CommandCollection("Power Collection", 2, ("power_a", "Cell"), ("power_b", "Fuse"))
        }
    };
    // v1: owns Alpha x2 (dup), Beta x1, Cell x1; wallet 5. v2: owns Alpha x1 only.
    JsonObject Inventory() => new()
    {
        ["v1"] = new JsonObject
        {
            ["displayName"] = "ViewerOne",
            ["components"] = new JsonObject { ["basic_a"] = 2, ["basic_b"] = 1, ["power_a"] = 1 },
            ["wallet"] = new JsonObject { ["scrap"] = 5 }
        },
        ["v2"] = new JsonObject
        {
            ["displayName"] = "ViewerTwo",
            ["components"] = new JsonObject { ["basic_a"] = 1 }
        }
    };
    var now = DateTimeOffset.UtcNow;

    bool AnyContains(IReadOnlyList<string> lines, string text) => lines.Any(l => l.Contains(text, StringComparison.Ordinal));

    var inventoryLines = CommandEngine.Inventory(catalog, Inventory(), ctx, "v1", "ViewerOne", now);
    Require(AnyContains(inventoryLines, "Basic 2/3") && AnyContains(inventoryLines, "Power 1/2"),
        "Inventory should report owned/total per collection.");

    var missingLines = CommandEngine.Missing(catalog, Inventory(), ctx, "v1", "ViewerOne", now);
    Require(AnyContains(missingLines, "Basic: Gamma") && AnyContains(missingLines, "Power: Fuse"),
        "Missing should list the unowned part names.");

    var dupLines = CommandEngine.Duplicates(catalog, Inventory(), ctx, "v1", "ViewerOne", now);
    Require(AnyContains(dupLines, "Basic Alpha x2"), "Duplicates should list the duplicated part with its count.");
    var noDupLines = CommandEngine.Duplicates(catalog, Inventory(), ctx, "v2", "ViewerTwo", now);
    Require(AnyContains(noDupLines, "don't have any duplicate"), "A viewer with no extras should get the no-duplicates message.");

    Require(CommandEngine.Balance(Inventory(), ctx, "v1", "ViewerOne") == "@ViewerOne Scrap balance: 5.",
        "Balance should read the wallet currency.");
    Require(CommandEngine.Balance(Inventory(), ctx, "ghost", "Ghost").Contains("don't have any components"),
        "Balance for an unknown viewer should return the no-inventory message.");

    var detail = CommandEngine.CollectionDetail(catalog, Inventory(), ctx, "v1", "ViewerOne", "basic", now);
    Require(AnyContains(detail, "Basic Collection: 2/3") && AnyContains(detail, "Owned: Alpha, Beta") && AnyContains(detail, "Missing: Gamma"),
        "Collection detail should summarize owned/missing/duplicates.");
    Require(CommandEngine.CollectionDetail(catalog, Inventory(), ctx, "v1", "ViewerOne", "", now)[0].Contains("usage:"),
        "Empty collection arg should return usage.");
    Require(CommandEngine.CollectionDetail(catalog, Inventory(), ctx, "v1", "ViewerOne", "nope", now)[0].Contains("unknown collection"),
        "Unknown collection should report availability.");

    var board = CommandEngine.Leaderboard(catalog, Inventory(), ctx, "v1", "ViewerOne", null, now);
    Require(AnyContains(board, "#1 ViewerOne 3/5"), "Leaderboard should rank the leading viewer by unique count.");

    // Salvage write — Alpha x2 → 1 extra at salvageValue 1 → +1 Scrap, balance 5→6, Alpha reduced to 1.
    var salvageInventory = Inventory();
    var salvage = CommandEngine.Salvage(catalog, salvageInventory, ctx, "v1", "ViewerOne", "basic");
    Require(salvage.Mutated && salvage.ConsumedComponents == 1 && salvage.EarnedCurrency == 1 && salvage.NewBalance == 6,
        "Salvage should consume one extra, earn currency, and credit the wallet.");
    Require(salvage.Message.Contains("salvaged 1 extra component for 1 Scrap. Balance: 6."), "Salvage success message should be formatted.");
    var v1Components = (JsonObject)((JsonObject)salvageInventory["v1"]!)["components"]!;
    Require(v1Components["basic_a"]!.GetValue<long>() == 1, "Salvaged part should be reduced to a single copy.");
    Require(((JsonObject)((JsonObject)salvageInventory["v1"]!)["wallet"]!)["scrap"]!.GetValue<long>() == 6, "Wallet should be credited.");

    Require(!CommandEngine.Salvage(catalog, Inventory(), ctx, "v1", "ViewerOne", "").Mutated, "Empty salvage arg should not mutate.");
    Require(CommandEngine.Salvage(catalog, Inventory(), ctx, "v1", "ViewerOne", "nope").Message.Contains("unknown collection"), "Unknown salvage target should be reported.");
    Require(CommandEngine.Salvage(catalog, Inventory(), ctx, "v2", "ViewerTwo", "power").Message.Contains("no extra copies to salvage"),
        "Salvaging a collection with no extras should report nothing to salvage.");

    Console.WriteLine("CommandEngine: inventory, missing, duplicates, balance, collection detail, leaderboard, and salvage (write) all passed.");
}

static JsonObject CommandCollection(string displayName, long salvageValue, params (string Id, string Name)[] parts)
{
    var partArray = new JsonArray();
    foreach (var (id, name) in parts) partArray.Add(new JsonObject { ["id"] = id, ["name"] = name });
    return new JsonObject { ["displayName"] = displayName, ["type"] = "permanent", ["salvageValue"] = salvageValue, ["parts"] = partArray };
}

static JsonObject MakeCollection(string name, double weight, params string[] partIds)
{
    var parts = new JsonArray();
    foreach (var id in partIds) parts.Add(new JsonObject { ["id"] = id, ["name"] = id.ToUpperInvariant() });
    return new JsonObject { ["displayName"] = name, ["type"] = "permanent", ["weight"] = weight, ["parts"] = parts };
}

static JsonObject MakeEvent(string name, double weight, bool enabled, string fromUtc, string untilUtc, params string[] partIds)
{
    var collection = MakeCollection(name, weight, partIds);
    collection["type"] = "event";
    collection["enabled"] = enabled;
    collection["activeFromUtc"] = fromUtc;
    collection["activeUntilUtc"] = untilUtc;
    return collection;
}

// ── Batch 2 (1.0.1) robustness regressions ───────────────────────────────────
// Lock in the redemption/date-handling hardening: (1) an enabled EVENT collection with a broken
// schedule self-excludes instead of throwing on every pull, (2) naive (no-'Z') timestamps are treated
// as UTC so the pull/chat gate and the availability display agree on one half-open boundary, and (3)
// the CircuitService config validator accepts exactly the windows the engines honor.

// (1) THE KEY FIX. RedemptionEngine.SelectCollection walks EVERY collection on each pull. Before Batch 2,
// IsCollectionActive THREW InvalidDataException on a missing/unparseable/reversed event schedule, so a
// single misconfigured event made every viewer's redemption throw. Now a bad event is simply skipped
// (mirroring the already-tolerant CommandEngine.IsEventActive), and a valid collection still pulls. The
// old throwing behavior can't be invoked here (the code is already fixed and the method is private), so
// the guard we lock in is the observable one: a malformed AND a reversed enabled event, alongside a
// valid permanent collection, never throw and never divert a pull away from the valid collection.
static void TestBadEventCollectionSelfExcludes()
{
    var catalog = new JsonObject
    {
        ["collections"] = new JsonObject
        {
            ["good"] = MakeCollection("Good", 100, "g1", "g2"),
            // Enabled event whose schedule is unparseable garbage.
            ["malformed"] = MakeEvent("Malformed", 50, true, "not-a-date", "also-bogus", "m1"),
            // Enabled event whose window is reversed (until <= from).
            ["reversed"] = MakeEvent("Reversed", 50, true, "2026-06-30T00:00:00Z", "2026-06-01T00:00:00Z", "r1")
        }
    };
    var collections = (JsonObject)catalog["collections"]!;
    var now = DateTimeOffset.Parse("2026-06-15T00:00:00Z");

    // SelectCollection must not throw, and can only ever land on the valid permanent collection.
    var rng = new Random(2024);
    for (var i = 0; i < 5000; i++)
        Require(RedemptionEngine.SelectCollection(collections, null, now, rng).Key == "good",
            "A malformed/reversed enabled event must self-exclude; only the valid collection should be selectable.");

    // Full ApplyRedemption pipeline: still doesn't throw, still pulls from the valid collection, still writes.
    var inventory = new JsonObject();
    var result = RedemptionEngine.ApplyRedemption(catalog, null, inventory, "viewer-a", "Viewer A", now, new Random(5));
    Require(result.CollectionKey == "good", "ApplyRedemption should pull from the valid collection despite the bad events.");
    Require(result.Pull.PartId is "g1" or "g2", "The pulled part must come from the valid collection.");
    Require(inventory["viewer-a"] is JsonObject, "The viewer's inventory must be written despite the bad events.");

    // Self-exclusion is exactly what keeps a pull alive: strip the valid collection, and the same two bad
    // events now surface the graceful "nothing to pull" InvalidDataException — never a schedule-parse crash.
    var onlyBad = new JsonObject
    {
        ["malformed"] = MakeEvent("Malformed", 50, true, "not-a-date", "also-bogus", "m1"),
        ["reversed"] = MakeEvent("Reversed", 50, true, "2026-06-30T00:00:00Z", "2026-06-01T00:00:00Z", "r1")
    };
    RequireThrows<InvalidDataException>(() => RedemptionEngine.SelectCollection(onlyBad, null, now, new Random(1)),
        "With only bad events, selection should fail gracefully (nothing to pull), not crash parsing a schedule.");

    Console.WriteLine("Bad event self-exclude: a malformed + a reversed enabled event no longer fail every pull; the valid collection still pulls.");
}

// (2) Naive (no zone suffix) timestamps are treated as UTC everywhere, so a window written without 'Z'
// resolves to the SAME instants as the 'Z' form, and the gate (IsEventActive, observed through Missing)
// agrees with the display (AvailabilityStatus, observed through CollectionDetail) at every boundary —
// no drift by the streamer's local offset. Also pins the half-open boundary: active on [from, until).
static void TestNaiveTimestampTreatedAsUtc()
{
    var ctx = EventTestContext();

    // Returns (does the gate treat the event as active?, the availability display string) for a window + now.
    (bool GateActive, string Availability) Probe(string from, string until, DateTimeOffset now)
    {
        var catalog = new JsonObject
        {
            ["collections"] = new JsonObject
            {
                ["always"] = MakeCollection("Always", 100, "a1"),
                ["festival"] = MakeEvent("Festival", 50, true, from, until, "f1")
            }
        };
        var inventory = new JsonObject { ["v"] = new JsonObject { ["displayName"] = "V", ["components"] = new JsonObject() } };
        // Missing lists ONLY active events, so a "Festival:" line means the gate considers it active.
        var missing = CommandEngine.Missing(catalog, inventory, ctx, "v", "V", now);
        var gateActive = missing.Any(l => l.Contains("Festival:", StringComparison.Ordinal));
        // CollectionDetail's summary line carries the AvailabilityStatus display string.
        var detail = CommandEngine.CollectionDetail(catalog, inventory, ctx, "v", "V", "festival", now);
        return (gateActive, detail[0]);
    }

    const string fromNaive = "2026-06-01T00:00:00", untilNaive = "2026-06-30T00:00:00";
    const string fromZ = "2026-06-01T00:00:00Z", untilZ = "2026-06-30T00:00:00Z";

    var before = DateTimeOffset.Parse("2026-05-15T00:00:00Z");
    var inside = DateTimeOffset.Parse("2026-06-15T00:00:00Z");
    var startBoundary = DateTimeOffset.Parse("2026-06-01T00:00:00Z"); // == from
    var endBoundary = DateTimeOffset.Parse("2026-06-30T00:00:00Z");   // == until

    // Naive and 'Z' windows must be indistinguishable at every probe point (naive == UTC).
    foreach (var now in new[] { before, startBoundary, inside, endBoundary })
    {
        var naive = Probe(fromNaive, untilNaive, now);
        var zed = Probe(fromZ, untilZ, now);
        Require(naive.GateActive == zed.GateActive && naive.Availability == zed.Availability,
            $"A naive window must resolve identically to the 'Z' window at {now:yyyy-MM-ddTHH:mm:ssZ}.");
    }

    // Gate <-> display agreement, and the exact half-open boundary (>= from && < until).
    var atStart = Probe(fromNaive, untilNaive, startBoundary);
    Require(atStart.GateActive && atStart.Availability.Contains("Event active until 2026-06-30"),
        "At now == from the event is ACTIVE (inclusive start) and the display agrees.");
    var atEnd = Probe(fromNaive, untilNaive, endBoundary);
    Require(!atEnd.GateActive && atEnd.Availability.Contains("Event ended 2026-06-30"),
        "At now == until the event is OVER (exclusive end / half-open) and the display agrees.");
    var whenInside = Probe(fromNaive, untilNaive, inside);
    Require(whenInside.GateActive && whenInside.Availability.Contains("Event active until 2026-06-30"),
        "Inside the window the gate is active and the display says 'active until'.");
    var whenBefore = Probe(fromNaive, untilNaive, before);
    Require(!whenBefore.GateActive && whenBefore.Availability.Contains("Event starts 2026-06-01"),
        "Before the window the gate is inactive and the display says 'starts'.");

    Console.WriteLine("Naive timestamps: naive windows resolve identically to 'Z' windows; the pull/chat gate and the availability display agree on one half-open UTC boundary.");
}

// (3) The CircuitService config validator must accept exactly the windows the engines honor: a naive
// (no-'Z') window the engine considers valid is ACCEPTED, and a reversed window (until <= from) is
// REJECTED with an event-specific error. Hermetic: its own temp data folder + store, like the Batch 1 tests.
static void TestEventWindowValidatorMatchesEngine()
{
    JsonObject Components(string from, string until) => new()
    {
        ["schemaVersion"] = 1,
        ["collections"] = new JsonObject
        {
            ["always"] = new JsonObject
            {
                ["displayName"] = "Always",
                ["type"] = "permanent",
                ["weight"] = 100,
                ["salvageValue"] = 1,
                ["parts"] = new JsonArray { new JsonObject { ["id"] = "always_a", ["name"] = "Alpha" } }
            },
            ["festival"] = new JsonObject
            {
                ["displayName"] = "Festival",
                ["type"] = "event",
                ["weight"] = 50,
                ["salvageValue"] = 2,
                ["enabled"] = true,
                ["activeFromUtc"] = from,
                ["activeUntilUtc"] = until,
                ["parts"] = new JsonArray { new JsonObject { ["id"] = "festival_a", ["name"] = "Fest Alpha" } }
            }
        }
    };
    JsonObject DisabledBoost() => new() { ["enabled"] = false, ["displayName"] = "Featured Boost", ["collectionMultipliers"] = new JsonObject() };

    var dir = FreshDataDir();
    try
    {
        var store = new LocalFileDataStore(dir);
        var service = new CircuitService(store);

        // (a) A naive-as-UTC window the engine honors must be ACCEPTED.
        var accepted = service.SaveConfiguration(new JsonObject
        {
            ["components"] = Components("2026-06-01T00:00:00", "2026-06-30T00:00:00"),
            ["boost"] = DisabledBoost()
        });
        Require(accepted.Status == 200,
            "The validator must accept a naive-as-UTC event window the engine considers valid: "
                + string.Join("; ", (accepted.Body["errors"] as JsonArray ?? new JsonArray()).Select(e => e?.ToString())));

        // Cross-check the engine agrees this same saved window is live mid-window.
        var collections = (JsonObject)(store.ReadProfileData(store.ActiveProfileId, DataKeys.Catalog)
            ?? throw new InvalidOperationException("Saved catalog should be readable."))["collections"]!;
        var mid = DateTimeOffset.Parse("2026-06-15T00:00:00Z");
        var sawFestival = false;
        var rng = new Random(3);
        for (var i = 0; i < 4000 && !sawFestival; i++)
            if (RedemptionEngine.SelectCollection(collections, null, mid, rng).Key == "festival") sawFestival = true;
        Require(sawFestival, "The engine must treat the accepted naive window as live mid-window (validator matches engine).");

        // (b) A reversed window (until <= from) must be REJECTED, naming the offending event.
        var rejected = service.SaveConfiguration(new JsonObject
        {
            ["components"] = Components("2026-06-30T00:00:00Z", "2026-06-01T00:00:00Z"),
            ["boost"] = DisabledBoost()
        });
        Require(rejected.Status != 200, "The validator must reject a reversed event window.");
        var errors = rejected.Body["errors"] as JsonArray ?? new JsonArray();
        Require(errors.Any(e => (e?.ToString() ?? "").Contains("festival", StringComparison.Ordinal)),
            "The reversed-window rejection should name the offending event collection.");

        Console.WriteLine("Event-window validator: accepts a naive-as-UTC window the engine honors, rejects a reversed window (validator matches engine).");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// A minimal, valid CommandContext for the event-window tests (only the templates these tests read matter).
static CommandContext EventTestContext() => new(
    GameName: "Circuit",
    ItemSingular: "component", ItemPlural: "components",
    CollectionSingular: "collection", RedemptionName: "Circuit Component", CurrencyName: "Scrap",
    CollectionCommand: "collection", SalvageCommand: "salvage",
    NoInventoryTemplate: "@{viewer} you don't have any {itemPlural} yet.",
    BalanceTemplate: "@{viewer} {currency} balance: {balance}.",
    NoDuplicatesTemplate: "@{viewer} you don't have any duplicate {itemPlural} yet.",
    CollectionUsageTemplate: "@{viewer} usage: !{collectionCommand} <{collectionSingular}>",
    CollectionSummaryTemplate: "@{viewer} {collection}: {owned}/{total} | {status}{availability}",
    SalvageUsageTemplate: "@{viewer} usage: !{salvageCommand} <{collectionSingular}> or !{salvageCommand} all",
    NothingToSalvageTemplate: "@{viewer} you have no extra copies to salvage in {selection}.",
    SalvageSuccessTemplate: "@{viewer} salvaged {count} extra {itemWord} for {earned} {currency}. Balance: {balance}.");

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

// Verifies the Batch 3 (1.0.1) security fix in TwitchTokens.TryLoad: a legacy PLAINTEXT token file is
// re-encrypted at rest immediately on load — the tokens still load with their original values, but the
// on-disk file becomes DPAPI-protected (protected == true, the plaintext tokens gone from the bytes) on
// THIS launch instead of waiting up to ~4h for the next save/refresh. And an already-encrypted file must
// NOT be rewritten on every subsequent load: DPAPI re-encryption would change the ciphertext, so a
// byte-for-byte identical file across a second TryLoad proves there is no per-launch write-churn.
// Hermetic — its own temp data folder, like the config-loader tests. Windows-only (DPAPI), as is the suite.
static void TestTwitchTokenReEncryptOnLoad()
{
    var dir = Path.Combine(Path.GetTempPath(), "CircuitOSTwitchTokens-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, TwitchTokens.FileName);
    const string accessPlain = "legacy-access-token-PLAINTEXT-abc123";
    const string refreshPlain = "legacy-refresh-token-PLAINTEXT-def456";
    var expiresAt = new DateTimeOffset(2027, 3, 4, 5, 6, 7, TimeSpan.Zero);
    try
    {
        // Write a LEGACY plaintext token file: the shape Save() produces, but with no "protected" flag
        // and readable access/refresh tokens (the pre-DPAPI on-disk format an in-place upgrade inherits).
        var legacy = new JsonObject
        {
            ["accessToken"] = accessPlain,
            ["refreshToken"] = refreshPlain,
            ["expiresAt"] = expiresAt.ToString("O"),
            ["userId"] = "user-legacy-1",
            ["login"] = "legacy_streamer",
            ["displayName"] = "Legacy Streamer"
        };
        File.WriteAllText(path, legacy.ToJsonString());
        Require(File.ReadAllText(path).Contains(accessPlain), "Sanity: the seeded legacy file should start as readable plaintext.");

        // (a) TryLoad returns the tokens with every field intact...
        var tokens = TwitchTokens.TryLoad(dir) ?? throw new InvalidOperationException("Legacy plaintext tokens should load.");
        Require(tokens.AccessToken == accessPlain, "The access token value must survive the legacy load.");
        Require(tokens.RefreshToken == refreshPlain, "The refresh token value must survive the legacy load.");
        Require(tokens.ExpiresAt == expiresAt, "expiresAt must survive the legacy load.");
        Require(tokens.Login == "legacy_streamer", "login must survive the legacy load.");
        Require(tokens.UserId == "user-legacy-1", "userId must survive the legacy load.");

        // (b) ...and the file on disk is now ENCRYPTED: protected == true and the plaintext tokens are gone.
        var afterLoad = File.ReadAllText(path);
        var reencrypted = JsonNode.Parse(afterLoad) as JsonObject
            ?? throw new InvalidOperationException("The re-encrypted token file should still be a JSON object.");
        Require(reencrypted["protected"]?.GetValue<bool>() == true, "The legacy file must be re-saved with protected == true on load.");
        Require(!afterLoad.Contains(accessPlain), "The plaintext access token must no longer appear in the file after re-encryption.");
        Require(!afterLoad.Contains(refreshPlain), "The plaintext refresh token must no longer appear in the file after re-encryption.");

        // (c) An already-encrypted file must NOT be rewritten on the next load (no per-launch write-churn).
        // If it re-saved, DPAPI's randomized ciphertext would change the bytes — so an identical hash proves it didn't.
        var hashBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var writeBefore = File.GetLastWriteTimeUtc(path);
        var reloaded = TwitchTokens.TryLoad(dir) ?? throw new InvalidOperationException("The re-encrypted tokens should still load.");
        Require(reloaded.AccessToken == accessPlain, "The re-encrypted token must still decrypt to the original access token.");
        Require(reloaded.RefreshToken == refreshPlain, "The re-encrypted token must still decrypt to the original refresh token.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hashBefore,
            "An already-encrypted token file must not be rewritten on load (identical bytes prove no re-encrypt churn).");
        Require(File.GetLastWriteTimeUtc(path) == writeBefore,
            "An already-encrypted token file's last-write time must not change on load.");

        Console.WriteLine("Twitch token re-encrypt: a legacy plaintext file loads intact and is encrypted at rest on load; an already-encrypted file is not rewritten (no churn).");
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

