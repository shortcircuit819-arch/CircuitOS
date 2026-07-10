using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CircuitOS.Runtime;

internal sealed record ServiceResult(int Status, JsonObject Body);

internal sealed partial class CircuitService
{
    private static readonly string[] ProfileTextFields =
    [
        "gameName", "adminName", "brandKicker", "itemSingular", "itemPlural",
        "collectionSingular", "collectionPlural", "currencyName", "redemptionName"
    ];

    private static readonly string[] CommandFields =
        ["inventory", "missing", "duplicates", "leaderboard", "balance", "collection", "salvage"];

    private static readonly string[] ColorFields =
        ["background", "panel", "panelAlt", "line", "accent", "text", "muted"];

    // Curated base themes: the structural colors the app owns (background, panel, panelAlt, line, text,
    // muted). The accent is supplied separately by the streamer. Mirrors BASE_THEMES in tools/admin/app.js.
    // When a profile selects a theme, these become its effective `colors` so overlay + engine follow too.
    private static readonly Dictionary<string, string[]> BaseThemes = new(StringComparer.Ordinal)
    {
        ["midnight"] = ["#000d19", "#061a2b", "#092239", "#193a55", "#eef5fb", "#8295a8"],
        ["slate"]    = ["#0d1117", "#161b22", "#1c232c", "#2b3543", "#e6edf3", "#8b949e"],
        ["carbon"]   = ["#0a0a0c", "#141418", "#1d1d22", "#2f2f37", "#f0f0f2", "#9a9aa4"],
        ["ember"]    = ["#140f0d", "#1f1815", "#2a201b", "#3d2f27", "#f5ede7", "#a8968a"],
        ["grape"]    = ["#0e0a17", "#191130", "#241941", "#362a58", "#efeafb", "#9b8fbf"],
        ["daylight"] = ["#e9ebf0", "#ffffff", "#f3f4f7", "#d5d9e0", "#1b1e26", "#5b6472"],
    };
    private const string DefaultThemeId = "midnight";
    private const string DefaultAccent = "#ff1a24";

    // Design Mode (0.8 step 5): whitelisted CSS custom-property overrides layered over the theme at
    // runtime. Colors must be hex; --radius is 0–24px. Kept in sync with app.js DESIGN_* key lists.
    private static readonly string[] DesignColorKeys =
        ["--bg", "--panel", "--panel-2", "--line", "--text", "--muted", "--green", "--amber", "--blue", "--danger"];

    private static readonly HashSet<string> OptionalMessages = new(StringComparer.Ordinal)
    {
        "variantPull"
    };

    private static readonly Dictionary<string, string[]> MessagePlaceholders = new(StringComparer.Ordinal)
    {
        ["redeemSuccess"] = ["viewer", "item", "collection", "owned", "total", "duplicateText"],
        ["rarePull"] = ["rareLabel", "viewer", "item", "odds"],
        ["triplePull"] = ["viewer", "item", "odds"],
        ["collectionComplete"] = ["viewer", "collection"],
        ["noInventory"] = ["viewer", "itemPlural", "redemption", "collectionSingular"],
        ["balance"] = ["viewer", "currency", "balance"],
        ["noDuplicates"] = ["viewer", "itemPlural"],
        ["collectionUsage"] = ["viewer", "collectionCommand", "collectionSingular"],
        ["collectionSummary"] = ["viewer", "collection", "owned", "total", "status", "availability"],
        ["salvageUsage"] = ["viewer", "salvageCommand", "collectionSingular"],
        ["nothingToSalvage"] = ["viewer", "selection"],
        ["salvageSuccess"] = ["viewer", "count", "itemWord", "earned", "currency", "balance"],
        ["variantPull"] = ["variantLabels", "viewer", "item", "collection"]
    };

    // The portable data store (local file or Appwrite). _localStore is non-null only
    // when running on the local file store; the few filesystem-bound features
    // (local overlay template, backup folder display) fall back gracefully when it
    // is null. See docs/0.7-cloud-foundation.md.
    private readonly IDataStore _store;
    private readonly ILocalDataStore? _localStore;
    // Local store used to write the OBS overlay state file. Falls back to the data store when it
    // is itself local; in cloud mode the host passes the local store explicitly so the overlay
    // (which OBS reads from a local file) still updates on native pulls.
    private readonly ILocalDataStore? _overlayStore;

    public CircuitService(IDataStore store, ILocalDataStore? overlayStore = null)
    {
        _store = store;
        _localStore = store as ILocalDataStore;
        _overlayStore = overlayStore ?? _localStore;
    }

    public IDataStore Store => _store;

    // A real local path when running on the file store; a descriptive marker otherwise.
    private string DisplayDataPath => _localStore?.DataPath ?? $"appwrite://{_store.ActiveProfileId}";
    private string DisplayBackupPath => _localStore?.BackupPath ?? "(cloud — managed remotely)";

    private static JsonObject DefaultBoost() => new()
    {
        ["enabled"] = false,
        ["displayName"] = "Featured Boost",
        ["collectionMultipliers"] = new JsonObject()
    };

    private static JsonObject DefaultProfile() => new()
    {
        ["schemaVersion"] = 1,
        ["gameName"] = "Circuit Components",
        ["adminName"] = "CircuitOS Control Core",
        ["brandKicker"] = "CIRCUITOS",
        ["itemSingular"] = "component",
        ["itemPlural"] = "components",
        ["collectionSingular"] = "collection",
        ["collectionPlural"] = "collections",
        ["currencyName"] = "Scrap",
        ["redemptionName"] = "Circuit Component",
        ["commands"] = new JsonObject
        {
            ["inventory"] = "components", ["missing"] = "missing", ["duplicates"] = "dupes",
            ["leaderboard"] = "leaderboard", ["balance"] = "scrap",
            ["collection"] = "collection", ["salvage"] = "salvage"
        },
        ["messages"] = new JsonObject
        {
            ["redeemSuccess"] = "⚡ Scan complete: @{viewer} found {item} [{collection}]. Progress: {owned}/{total}.{duplicateText}",
            ["rarePull"] = "{rareLabel}: @{viewer} pulled {item}! Current odds: about 1 in {odds}.",
            ["triplePull"] = "TRIPLE MATCH: @{viewer} pulled {item} three times in a row! Sequence odds: about 1 in {odds}.",
            ["collectionComplete"] = "✅ COLLECTION COMPLETE: @{viewer} completed {collection}!",
            ["noInventory"] = "@{viewer} you don't have any {itemPlural} yet. Redeem {redemption} to start your {collectionSingular}.",
            ["balance"] = "@{viewer} {currency} balance: {balance}.",
            ["noDuplicates"] = "@{viewer} you don't have any duplicate {itemPlural} yet.",
            ["collectionUsage"] = "@{viewer} usage: !{collectionCommand} <{collectionSingular}>",
            ["collectionSummary"] = "@{viewer} {collection}: {owned}/{total} | {status}{availability}",
            ["salvageUsage"] = "@{viewer} usage: !{salvageCommand} <{collectionSingular}> or !{salvageCommand} all",
            ["nothingToSalvage"] = "@{viewer} you have no extra copies to salvage in {selection}.",
            ["salvageSuccess"] = "@{viewer} salvaged {count} extra {itemWord} for {earned} {currency}. Balance: {balance}.",
            ["variantPull"] = ""
        },
        ["redeemCooldownSeconds"] = 120,
        ["redeemDupProtectionTurns"] = 0,
        ["redemptionCost"] = 100,
        // Curated theme selection. `colors` is the effective palette this resolves to (Midnight + red),
        // kept in sync by NormalizeProfile so overlay + engine can keep reading `colors` directly.
        ["theme"] = DefaultThemeId,
        ["accent"] = DefaultAccent,
        ["designOverrides"] = new JsonObject(),
        ["colors"] = new JsonObject
        {
            ["background"] = "#000d19", ["panel"] = "#061a2b", ["panelAlt"] = "#092239",
            ["line"] = "#193a55", ["accent"] = "#ff1a24", ["text"] = "#eef5fb", ["muted"] = "#8295a8"
        }
    };

    private static JsonObject NormalizeProfile(JsonObject? incoming)
    {
        var normalized = DefaultProfile();
        if (incoming is null) return normalized;
        foreach (var field in ProfileTextFields)
            if (incoming[field] is not null) normalized[field] = JsonUtil.Clone(incoming[field]);
        foreach (var field in ColorFields)
            if (JsonUtil.Object(incoming, "colors")?[field] is { } color) ((JsonObject)normalized["colors"]!)[field] = JsonUtil.Clone(color);
        foreach (var field in CommandFields)
            if (JsonUtil.Object(incoming, "commands")?[field] is { } command) ((JsonObject)normalized["commands"]!)[field] = JsonUtil.Clone(command);
        foreach (var field in MessagePlaceholders.Keys)
            if (JsonUtil.Object(incoming, "messages")?[field] is { } message) ((JsonObject)normalized["messages"]!)[field] = JsonUtil.Clone(message);
        var cooldown = JsonUtil.Long(incoming, "redeemCooldownSeconds");
        if (cooldown >= 0 && cooldown <= 3600) normalized["redeemCooldownSeconds"] = cooldown;
        var dupProtection = JsonUtil.Long(incoming, "redeemDupProtectionTurns");
        if (dupProtection >= 0 && dupProtection <= 20) normalized["redeemDupProtectionTurns"] = dupProtection;
        var redemptionCost = JsonUtil.Long(incoming, "redemptionCost");
        if (redemptionCost >= 1 && redemptionCost <= 1_000_000) normalized["redemptionCost"] = redemptionCost;

        // Curated theming: `theme` (a base-theme id) + `accent` (one color) are the streamer's selection.
        // Start clean — the theme is only re-added when a known base is chosen; otherwise the profile is
        // "Custom" (running on its own `colors`, no theme).
        normalized.Remove("theme");
        var accent = JsonUtil.String(incoming, "accent");
        if (!IsHexColor(accent)) accent = JsonUtil.String(JsonUtil.Object(incoming, "colors"), "accent");
        if (IsHexColor(accent)) normalized["accent"] = accent;
        else accent = normalized["accent"]?.ToString() is { } fallback && IsHexColor(fallback) ? fallback : DefaultAccent;

        var themeId = JsonUtil.String(incoming, "theme");
        if (BaseThemes.TryGetValue(themeId, out var palette))
        {
            // A base theme wins: its structural colors + the chosen accent become the effective `colors`,
            // so the OBS overlay and engine (which read `colors`) follow the selected theme automatically.
            normalized["theme"] = themeId;
            normalized["accent"] = accent;
            normalized["colors"] = new JsonObject
            {
                ["background"] = palette[0], ["panel"] = palette[1], ["panelAlt"] = palette[2],
                ["line"] = palette[3], ["accent"] = accent, ["text"] = palette[4], ["muted"] = palette[5]
            };
        }

        // Design Mode overrides: keep only whitelisted keys with valid values (colors = hex, --radius =
        // 0–24px). Sanitize rather than reject, so a hand-edited or stale override can never block a save.
        var overrides = new JsonObject();
        if (JsonUtil.Object(incoming, "designOverrides") is { } incomingOverrides)
        {
            foreach (var key in DesignColorKeys)
            {
                var value = JsonUtil.String(incomingOverrides, key);
                if (IsHexColor(value)) overrides[key] = value;
            }
            var radius = JsonUtil.String(incomingOverrides, "--radius");
            if (Regex.IsMatch(radius, "^([0-9]|1[0-9]|2[0-4])px$")) overrides["--radius"] = radius;
        }
        normalized["designOverrides"] = overrides;
        return normalized;
    }

    private static bool IsHexColor(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");

    private static List<string> ValidateProfile(JsonObject? profile)
    {
        var errors = new List<string>();
        if (profile is null) return ["System profile is required."];
        if (JsonUtil.Long(profile, "schemaVersion") != 1) errors.Add("System profile schemaVersion must be 1.");
        foreach (var field in ProfileTextFields)
        {
            var value = JsonUtil.String(profile, field);
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) errors.Add($"Profile field '{field}' must contain 1 to 80 characters.");
        }

        var commands = JsonUtil.Object(profile, "commands");
        if (commands is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in CommandFields)
            {
                var value = JsonUtil.String(commands, field);
                if (!Regex.IsMatch(value, "^[a-z0-9][a-z0-9_-]{0,30}$")) errors.Add($"Profile command '{field}' must use 1 to 31 lowercase letters, numbers, underscores, or hyphens.");
                else if (!seen.Add(value)) errors.Add($"Profile command '{value}' is duplicated.");
            }
        }

        var messages = JsonUtil.Object(profile, "messages");
        if (messages is not null)
        {
            foreach (var (field, allowed) in MessagePlaceholders)
            {
                var template = JsonUtil.String(messages, field);
                if (OptionalMessages.Contains(field) && string.IsNullOrEmpty(template)) continue;
                if (string.IsNullOrWhiteSpace(template) || template.Length > 450)
                {
                    errors.Add($"Message template '{field}' must contain 1 to 450 characters.");
                    continue;
                }
                foreach (Match match in Regex.Matches(template, "\\{([a-zA-Z][a-zA-Z0-9]*)\\}"))
                    if (!allowed.Contains(match.Groups[1].Value, StringComparer.Ordinal)) errors.Add($"Message template '{field}' uses unsupported placeholder '{match.Value}'.");
                var withoutTokens = Regex.Replace(template, "\\{[a-zA-Z][a-zA-Z0-9]*\\}", "");
                if (withoutTokens.Contains('{') || withoutTokens.Contains('}')) errors.Add($"Message template '{field}' contains an invalid placeholder brace.");
            }
        }

        var colors = JsonUtil.Object(profile, "colors");
        if (colors is null) errors.Add("System profile colors are required.");
        else foreach (var field in ColorFields)
            if (!Regex.IsMatch(JsonUtil.String(colors, field), "^#[0-9a-fA-F]{6}$")) errors.Add($"Profile color '{field}' must be a six-digit hex color.");
        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    public JsonObject GetSystemProfile()
    {
        var profile = NormalizeProfile(_store.TryRead(DataKeys.Profile));
        return new JsonObject
        {
            ["profile"] = profile,
            ["isConfigured"] = _store.Exists(DataKeys.Profile),
            ["validationErrors"] = ToJsonArray(ValidateProfile(profile)),
            ["dataPath"] = DisplayDataPath
        };
    }

    public ServiceResult SaveSystemProfile(JsonObject requested)
    {
        var profile = NormalizeProfile(requested);
        var errors = ValidateProfile(profile);
        // Guard chat-command collisions only when the edited profile is itself live; drafts save freely.
        if (IsProfileLive(_store.ActiveProfileId)) errors.AddRange(LiveProfileCollisions(profile, _store.ActiveProfileId));
        if (errors.Count > 0) return Error(errors);
        var backup = _store.WriteAtomic(DataKeys.Profile, profile, "system-profile", Timestamp());
        return Ok(new JsonObject
        {
            ["ok"] = true, ["savedAtUtc"] = DateTime.UtcNow.ToString("O"), ["backup"] = backup
        });
    }

    // Per-viewer redeem cooldown, keyed "profileId:viewerId" -> last redeem time. In-memory (resets
    // on restart) — short cooldowns don't need persistence.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _lastRedeem = new();

    public ServiceResult DispatchRuntimeAction(JsonObject request)
    {
        var action = JsonUtil.String(request, "action");
        if (string.IsNullOrWhiteSpace(action)) return Error(["Runtime action is required."]);

        var profileId = ResolveRuntimeProfileId(request);
        if (string.IsNullOrWhiteSpace(profileId)) return Error(["No live profile is available to run this action."]);

        var profile = _store.ReadProfileData(profileId, DataKeys.Profile);
        if (profile is null) return Error([$"Profile '{profileId}' does not have a system profile."]);

        var commands = JsonUtil.Object(profile, "commands") ?? new JsonObject();
        var inventory = _store.ReadProfileData(profileId, DataKeys.Inventory) ?? new JsonObject();
        var catalog = _store.ReadProfileData(profileId, DataKeys.Catalog);
        var boost = _store.ReadProfileData(profileId, DataKeys.Boost) ?? new JsonObject();
        if (catalog is null) return Error([$"Profile '{profileId}' does not have a catalog."]);

        var messages = JsonUtil.Object(profile, "messages") ?? new JsonObject();
        var ctx = new CommandContext(
            JsonUtil.String(profile, "gameName"),
            JsonUtil.String(profile, "itemSingular"),
            JsonUtil.String(profile, "itemPlural"),
            JsonUtil.String(profile, "collectionSingular"),
            JsonUtil.String(profile, "redemptionName"),
            JsonUtil.String(profile, "currencyName"),
            JsonUtil.String(commands, "collection"),
            JsonUtil.String(commands, "salvage"),
            JsonUtil.String(messages, "noInventory"),
            JsonUtil.String(messages, "balance"),
            JsonUtil.String(messages, "noDuplicates"),
            JsonUtil.String(messages, "collectionUsage"),
            JsonUtil.String(messages, "collectionSummary"),
            JsonUtil.String(messages, "salvageUsage"),
            JsonUtil.String(messages, "nothingToSalvage"),
            JsonUtil.String(messages, "salvageSuccess"));

        var viewerId = JsonUtil.String(request, "viewerId");
        var viewerName = JsonUtil.String(request, "viewerName");
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(action, "command", StringComparison.OrdinalIgnoreCase))
        {
            var commandName = JsonUtil.String(request, "command");
            if (string.IsNullOrWhiteSpace(commandName)) return Error(["Command name is required."]);
            var targetCommand = ResolveCommandField(commands, commandName);
            if (string.IsNullOrWhiteSpace(targetCommand)) return Error([$"Command '{commandName}' was not found in profile '{profileId}'."]);

            IReadOnlyList<string> result;
            if (targetCommand == "salvage")
            {
                // Salvage mutates inventory (consumes duplicates, credits currency); persist when it
                // actually changed — otherwise the native !salvage silently lost the write.
                var salvage = CommandEngine.Salvage(catalog, inventory, ctx, viewerId, viewerName, JsonUtil.String(request, "arg"));
                if (salvage.Mutated) WriteProfileData(profileId, DataKeys.Inventory, inventory);
                result = [salvage.Message];
            }
            else
            {
                result = targetCommand switch
                {
                    "inventory" => CommandEngine.Inventory(catalog, inventory, ctx, viewerId, viewerName, now),
                    "missing" => CommandEngine.Missing(catalog, inventory, ctx, viewerId, viewerName, now),
                    "duplicates" => CommandEngine.Duplicates(catalog, inventory, ctx, viewerId, viewerName, now),
                    "balance" => [CommandEngine.Balance(inventory, ctx, viewerId, viewerName)],
                    "collection" => CommandEngine.CollectionDetail(catalog, inventory, ctx, viewerId, viewerName, JsonUtil.String(request, "arg"), now),
                    "leaderboard" => CommandEngine.Leaderboard(catalog, inventory, ctx, viewerId, viewerName, JsonUtil.String(request, "arg"), now),
                    _ => ["Unsupported command."]
                };
            }
            return Ok(new JsonObject
            {
                ["ok"] = true,
                ["action"] = action,
                ["profileId"] = profileId,
                ["profileName"] = JsonUtil.String(profile, "gameName"),
                ["command"] = commandName,
                ["messages"] = new JsonArray(result.Select(m => JsonValue.Create(m)).ToArray())
            });
        }

        if (string.Equals(action, "redeem", StringComparison.OrdinalIgnoreCase))
        {
            // Per-viewer cooldown from the profile (redeemCooldownSeconds). Returns 429 so the caller
            // can refund the points; recorded only after a successful pull.
            var cooldownSeconds = JsonUtil.Long(profile, "redeemCooldownSeconds");
            var cooldownKey = $"{profileId}:{viewerId}";
            if (cooldownSeconds > 0 && !string.IsNullOrWhiteSpace(viewerId) && _lastRedeem.TryGetValue(cooldownKey, out var last))
            {
                var remaining = (int)Math.Ceiling(cooldownSeconds - (now - last).TotalSeconds);
                if (remaining > 0)
                    return Error([$"@{viewerName} {JsonUtil.String(profile, "redemptionName")} is on cooldown — {remaining}s left."], 429);
            }

            // Only use a fixed seed when one is explicitly supplied (deterministic tests). A missing
            // seed must be random — reading it as 0 and seeding Random(0) made every live pull identical.
            var rng = request["rngSeed"] is JsonValue seedNode
                && long.TryParse(seedNode.ToString(), out var seed) && seed is >= 0 and <= int.MaxValue
                ? new Random((int)seed)
                : new Random();
            // Dup protection from the profile (redeemDupProtectionTurns), not the request.
            var dupProtectionTurns = JsonUtil.Long(profile, "redeemDupProtectionTurns");
            var redemption = RedemptionEngine.ApplyRedemption(
                catalog,
                boost is not null ? boost : null,
                inventory,
                viewerId,
                viewerName,
                now,
                rng,
                dupProtectionTurns > 0 ? (int)dupProtectionTurns : 0);
            WriteProfileData(profileId, DataKeys.Inventory, inventory);
            WriteOverlayState(profileId, redemption, viewerName, now);
            if (cooldownSeconds > 0 && !string.IsNullOrWhiteSpace(viewerId)) _lastRedeem[cooldownKey] = now;
            var announcements = BuildRedeemAnnouncements(messages, redemption, viewerName);
            return Ok(new JsonObject
            {
                ["ok"] = true,
                ["action"] = action,
                ["profileId"] = profileId,
                ["profileName"] = JsonUtil.String(profile, "gameName"),
                ["collectionKey"] = redemption.CollectionKey,
                ["collectionName"] = redemption.CollectionName,
                ["partId"] = redemption.Pull.PartId,
                ["partName"] = redemption.Pull.DisplayPartName,
                ["quantity"] = redemption.Quantity,
                ["newlyCompleted"] = redemption.NewlyCompleted,
                ["consecutivePullCount"] = redemption.ConsecutivePullCount,
                ["messages"] = new JsonArray(announcements.Select(m => JsonValue.Create(m)).ToArray())
            });
        }

        return Error([$"Unsupported runtime action '{action}'."]);
    }

    // Formats the pull result into chat announcement line(s) using the profile's message templates
    // (success, rare, triple, complete, variant). Each template is skipped when blank, so streamers
    // can turn any line off.
    private static List<string> BuildRedeemAnnouncements(JsonObject messages, RedemptionResult result, string viewerName)
    {
        var lines = new List<string>();
        var item = result.Pull.DisplayPartName;
        var duplicateText = result.Quantity > 1 ? $" Duplicate count: x{result.Quantity}." : "";
        if (!string.IsNullOrWhiteSpace(result.ActiveBoostName)) duplicateText += $" Featured boost: {result.ActiveBoostName}.";

        var success = JsonUtil.String(messages, "redeemSuccess");
        if (!string.IsNullOrWhiteSpace(success))
            lines.Add(FormatTemplate(success, ("viewer", viewerName), ("item", item), ("collection", result.CollectionName),
                ("owned", result.OwnedAfter.ToString()), ("total", result.TotalParts.ToString()), ("duplicateText", duplicateText)));

        if (!string.IsNullOrWhiteSpace(result.RareLabel) && JsonUtil.String(messages, "rarePull") is { Length: > 0 } rare)
            lines.Add(FormatTemplate(rare, ("rareLabel", result.RareLabel), ("viewer", viewerName), ("item", item),
                ("odds", OneInOdds(result.Pull.Probability))));

        if (result.ConsecutivePullCount == 3 && JsonUtil.String(messages, "triplePull") is { Length: > 0 } triple)
            lines.Add(FormatTemplate(triple, ("viewer", viewerName), ("item", item), ("odds", OneInOdds(result.StreakSequenceProbability))));

        if (result.NewlyCompleted && JsonUtil.String(messages, "collectionComplete") is { Length: > 0 } complete)
            lines.Add(FormatTemplate(complete, ("viewer", viewerName), ("collection", result.CollectionName)));

        if (result.Pull.VariantLabels.Count > 0 && JsonUtil.String(messages, "variantPull") is { Length: > 0 } variant)
            lines.Add(FormatTemplate(variant, ("variantLabels", string.Join(" ", result.Pull.VariantLabels)),
                ("viewer", viewerName), ("item", result.Pull.PartName), ("collection", result.CollectionName)));

        return lines;
    }

    private static string FormatTemplate(string template, params (string Key, string Value)[] values)
    {
        var result = template;
        foreach (var (key, value) in values) result = result.Replace("{" + key + "}", value ?? "");
        return result;
    }

    private static string OneInOdds(double probability)
        => probability <= 0 ? "unknown" : Math.Max(1, Math.Round(1.0 / probability)).ToString("N0");

    public ServiceResult CompleteFirstRun(JsonObject request)
    {
        if (_store.Exists(DataKeys.Profile)) return Error(["First-run setup has already been completed."], 409);
        var profile = NormalizeProfile(request["profile"] as JsonObject);
        var configuration = request["configuration"] as JsonObject;
        var components = configuration?["components"] as JsonObject;
        var boost = configuration?["boost"] as JsonObject;
        var errors = ValidateProfile(profile);
        errors.AddRange(ValidateConfiguration(components, boost));
        errors = errors.Distinct(StringComparer.Ordinal).ToList();
        if (errors.Count > 0) return Error(errors);

        var configurationResult = SaveConfiguration(configuration!);
        if (configurationResult.Status != 200) return configurationResult;
        // First-run creates a draft/editing profile. Command collisions are enforced when a
        // profile goes live, not while initializing a new profile from the wizard.
        var profileBackup = _store.WriteAtomic(DataKeys.Profile, profile, "system-profile", Timestamp());
        var profileResult = Ok(new JsonObject
        {
            ["ok"] = true,
            ["savedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["backup"] = profileBackup
        });
        return Ok(new JsonObject
        {
            ["ok"] = true,
            ["completedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["configuration"] = JsonUtil.Clone(configurationResult.Body),
            ["profile"] = JsonUtil.Clone(profileResult.Body)
        });
    }

    private static List<string> ValidateConfiguration(JsonObject? components, JsonObject? boost)
    {
        var errors = new List<string>();
        var collections = JsonUtil.Object(components, "collections");
        if (collections is null) return ["components.json needs a top-level collections object."];
        if (collections.Count == 0) return ["At least one collection is required."];
        var collectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, node) in collections)
        {
            var collection = node as JsonObject;
            if (!Regex.IsMatch(key, "^[a-z0-9][a-z0-9_]*$")) errors.Add($"Collection key '{key}' must use lowercase letters, numbers, and underscores.");
            if (!collectionKeys.Add(key)) errors.Add($"Duplicate collection key: {key}");
            if (collection is null) { errors.Add($"Collection '{key}' must be a JSON object."); continue; }
            if (string.IsNullOrWhiteSpace(JsonUtil.String(collection, "displayName"))) errors.Add($"Collection '{key}' needs a displayName.");
            var type = JsonUtil.String(collection, "type", "permanent");
            if (type is not ("permanent" or "event")) errors.Add($"Collection '{key}' type must be permanent or event.");
            if (!TryNumber(collection["weight"], out var weight) || weight < 0) errors.Add($"Collection '{key}' weight must be zero or greater.");
            if (!TryInteger(collection["salvageValue"], out var salvage) || salvage <= 0) errors.Add($"Collection '{key}' salvageValue must be a positive integer.");
            if (type == "event")
            {
                if (!TryBool(collection["enabled"], out _)) errors.Add($"Event '{key}' enabled must be true or false.");
                if (!DateTimeOffset.TryParse(JsonUtil.String(collection, "activeFromUtc"), out var start) ||
                    !DateTimeOffset.TryParse(JsonUtil.String(collection, "activeUntilUtc"), out var end) || end <= start)
                    errors.Add($"Event '{key}' needs a valid UTC start before its UTC end.");
            }
            var tiers = JsonUtil.Array(collection, "tiers");
            var validTierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tiers is not null && tiers.Count > 0)
            {
                var tierIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tierNode in tiers)
                {
                    var tier = tierNode as JsonObject;
                    var tid = JsonUtil.String(tier, "id");
                    if (!Regex.IsMatch(tid, "^[a-z0-9][a-z0-9_]*$")) errors.Add($"Tier ID '{tid}' in '{key}' is invalid.");
                    else if (!tierIdSet.Add(tid)) errors.Add($"Tier ID '{tid}' is duplicated in collection '{key}'.");
                    else validTierIds.Add(tid);
                    if (string.IsNullOrWhiteSpace(JsonUtil.String(tier, "label"))) errors.Add($"Tier '{tid}' in '{key}' needs a label.");
                    if (!TryNumber(tier?["weight"], out var tw) || tw <= 0) errors.Add($"Tier '{tid}' in '{key}' weight must be greater than zero.");
                }
            }

            var parts = JsonUtil.Array(collection, "parts");
            if (parts is null || parts.Count == 0) errors.Add($"Collection '{key}' needs at least one component.");
            foreach (var partNode in parts ?? [])
            {
                var part = partNode as JsonObject;
                var id = JsonUtil.String(part, "id");
                if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9_]*$")) errors.Add($"Component ID '{id}' in '{key}' is invalid.");
                else if (!componentIds.Add(id)) errors.Add($"Component ID '{id}' is duplicated in the catalog.");
                if (string.IsNullOrWhiteSpace(JsonUtil.String(part, "name"))) errors.Add($"Component '{id}' in '{key}' needs a name.");
                if (validTierIds.Count > 0)
                {
                    var partTier = JsonUtil.String(part, "tier");
                    if (string.IsNullOrWhiteSpace(partTier)) errors.Add($"Component '{id}' in '{key}' must be assigned to a tier.");
                    else if (!validTierIds.Contains(partTier)) errors.Add($"Component '{id}' in '{key}' references unknown tier '{partTier}'.");
                }
            }

            var variants = JsonUtil.Array(collection, "variants");
            if (variants is not null)
            {
                var variantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var variantNode in variants)
                {
                    var variant = variantNode as JsonObject;
                    var vid = JsonUtil.String(variant, "id");
                    if (!Regex.IsMatch(vid, "^[a-z0-9][a-z0-9_]*$")) errors.Add($"Variant ID '{vid}' in '{key}' is invalid.");
                    else if (!variantIds.Add(vid)) errors.Add($"Variant ID '{vid}' is duplicated in collection '{key}'.");
                    if (string.IsNullOrWhiteSpace(JsonUtil.String(variant, "label"))) errors.Add($"Variant '{vid}' in '{key}' needs a label.");
                    if (!TryNumber(variant?["chance"], out var chance) || chance <= 0 || chance >= 1)
                        errors.Add($"Variant '{vid}' in '{key}' chance must be greater than 0 and less than 1.");
                }
            }
        }

        if (boost is null) return [.. errors, "Featured boost configuration is missing."];
        if (!TryBool(boost["enabled"], out var enabled)) errors.Add("Boost enabled must be true or false.");
        if (enabled && string.IsNullOrWhiteSpace(JsonUtil.String(boost, "displayName"))) errors.Add("An enabled boost needs a displayName.");
        var multipliers = JsonUtil.Object(boost, "collectionMultipliers");
        if (enabled && (multipliers is null || multipliers.Count == 0)) errors.Add("An enabled boost needs at least one multiplier.");
        foreach (var (key, value) in multipliers ?? [])
        {
            if (!collectionKeys.Contains(key)) errors.Add($"Boost references unknown collection '{key}'.");
            if (!TryNumber(value, out var multiplier) || multiplier <= 0) errors.Add($"Boost multiplier for '{key}' must be greater than zero.");
        }
        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    public JsonObject GetConfiguration()
    {
        var components = _store.ReadRequired(DataKeys.Catalog);
        var boost = _store.TryRead(DataKeys.Boost) ?? DefaultBoost();
        return new JsonObject
        {
            ["components"] = components,
            ["boost"] = boost,
            ["validationErrors"] = ToJsonArray(ValidateConfiguration(components, boost)),
            ["dataPath"] = DisplayDataPath,
            ["loadedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    public ServiceResult SaveConfiguration(JsonObject request)
    {
        var components = request["components"] as JsonObject;
        var boost = request["boost"] as JsonObject;
        var errors = ValidateConfiguration(components, boost);
        if (errors.Count > 0) return Error(errors);
        var timestamp = Timestamp();
        var componentBackup = _store.WriteAtomic(DataKeys.Catalog, components!, "components", timestamp);
        var boostBackup = _store.WriteAtomic(DataKeys.Boost, boost!, "featured-boost", timestamp);
        var backups = new JsonArray();
        if (!string.IsNullOrWhiteSpace(componentBackup)) backups.Add(componentBackup);
        if (!string.IsNullOrWhiteSpace(boostBackup)) backups.Add(boostBackup);
        return Ok(new JsonObject
        {
            ["ok"] = true, ["savedAtUtc"] = DateTime.UtcNow.ToString("O"), ["backups"] = backups
        });
    }

    private static bool TryNumber(JsonNode? node, out double value) =>
        double.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private static bool TryInteger(JsonNode? node, out long value) =>
        long.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    private static bool TryBool(JsonNode? node, out bool value) => bool.TryParse(node?.ToString(), out value);
    private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }
    private string? ResolveRuntimeProfileId(JsonObject request)
    {
        var explicitId = JsonUtil.String(request, "profileId");
        if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId;

        var action = JsonUtil.String(request, "action");
        if (string.Equals(action, "command", StringComparison.OrdinalIgnoreCase))
        {
            var commandName = JsonUtil.String(request, "command");
            if (!string.IsNullOrWhiteSpace(commandName))
            {
                foreach (var profile in _store.ListProfiles().Where(p => p.IsLive))
                {
                    var data = _store.ReadProfileData(profile.Id, DataKeys.Profile);
                    if (data is null) continue;
                    var commands = JsonUtil.Object(data, "commands") ?? new JsonObject();
                    if (ResolveCommandField(commands, commandName) is not null) return profile.Id;
                }
            }
        }

        return _store.ListProfiles().FirstOrDefault(p => p.IsLive)?.Id;
    }

    private static string? ResolveCommandField(JsonObject commands, string commandName)
    {
        foreach (var field in CommandFields)
            if (string.Equals(JsonUtil.String(commands, field), commandName, StringComparison.OrdinalIgnoreCase))
                return field;
        return null;
    }

    private void WriteProfileData(string profileId, string key, JsonNode value)
    {
        _store.WriteProfileData(profileId, key, value);
    }

    // Writes the OBS overlay state for a native pull, so overlay.js can render it. Display data:
    // best-effort, never throws into the redemption path (a failed overlay write must not fail a pull).
    private void WriteOverlayState(string profileId, RedemptionResult redemption, string viewerName, DateTimeOffset now)
    {
        if (_overlayStore is null) return;
        try
        {
            const string isoMs = "yyyy-MM-ddTHH:mm:ss.fffZ";
            var variantLabels = new JsonArray(redemption.Pull.VariantLabels.Select(v => JsonValue.Create(v)).ToArray());
            var state = new JsonObject
            {
                ["version"] = 1,
                ["updatedAtUtc"] = now.UtcDateTime.ToString(isoMs),
                ["visibleUntilUtc"] = now.UtcDateTime.AddSeconds(20).ToString(isoMs),
                ["viewerName"] = viewerName,
                ["partName"] = redemption.Pull.DisplayPartName,
                ["collectionKey"] = redemption.CollectionKey,
                ["collectionName"] = redemption.CollectionName,
                ["ownedCount"] = redemption.OwnedAfter,
                ["totalCount"] = redemption.TotalParts,
                ["quantity"] = redemption.Quantity,
                ["isDuplicate"] = redemption.IsDuplicate,
                ["newlyCompleted"] = redemption.NewlyCompleted,
                ["rareLabel"] = redemption.RareLabel ?? "",
                ["featuredBoost"] = redemption.ActiveBoostName ?? "",
                ["variantLabels"] = variantLabels,
                ["tierLabel"] = redemption.Pull.TierLabel ?? ""
            };
            _overlayStore.WriteOverlayState(profileId, state);
        }
        catch
        {
            // Overlay state is disposable display data; never let it break a recorded pull.
        }
    }

    private static ServiceResult Ok(JsonObject body) => new(200, body);
    private static ServiceResult Error(IEnumerable<string> errors, int status = 400) => new(status, new JsonObject
    {
        ["ok"] = false, ["errors"] = ToJsonArray(errors)
    });
}

