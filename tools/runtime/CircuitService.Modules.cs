using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed partial class CircuitService
{
    public ServiceResult ExportModule()
    {
        var catalog = _store.TryRead(DataKeys.Catalog);
        if (catalog is null) return Error(["No catalog found in the active profile. Complete first-run setup before exporting."]);

        var profile = _store.TryRead(DataKeys.Profile);
        var boost = _store.TryRead(DataKeys.Boost);
        var roles = _store.TryRead(DataKeys.Roles);
        var overlayConfig = _store.TryRead(DataKeys.OverlayConfig);

        var profileName = profile?["gameName"]?.ToString()?.Trim() ?? "Profile";

        var module = new JsonObject
        {
            ["manifest"] = new JsonObject
            {
                ["format"] = "circuitmodule",
                ["version"] = "1",
                ["name"] = profileName,
                ["circuitosVersion"] = "0.9.0",
                ["exportedAt"] = DateTimeOffset.UtcNow.ToString("O")
            },
            ["catalog"] = JsonNode.Parse(catalog.ToJsonString())!
        };
        if (profile is not null) module["profile"] = JsonNode.Parse(profile.ToJsonString())!;
        if (boost is not null) module["boost"] = JsonNode.Parse(boost.ToJsonString())!;
        if (roles is not null) module["roles"] = JsonNode.Parse(roles.ToJsonString())!;
        if (overlayConfig is not null) module["overlayConfig"] = JsonNode.Parse(overlayConfig.ToJsonString())!;

        return Ok(module);
    }

    public ServiceResult ImportModule(JsonObject module)
    {
        var manifest = module["manifest"] as JsonObject;
        if (manifest is null || manifest["format"]?.ToString() != "circuitmodule")
            return Error(["Not a valid .circuitmodule file."]);

        var version = manifest["version"]?.ToString();
        if (version != "1") return Error([$"Unsupported module version \"{version}\"."]);

        if (module["catalog"] is not JsonObject catalog)
            return Error(["Module is missing required catalog data."]);

        // Validate before creating anything: a corrupt or hand-edited module must not produce a broken
        // profile. A genuine CircuitOS export already passed this on save, so it round-trips cleanly.
        var catalogErrors = ValidateConfiguration(catalog, module["boost"] as JsonObject ?? DefaultBoost());
        if (catalogErrors.Count > 0)
        {
            catalogErrors.Insert(0, "This module's catalog is invalid — nothing was imported.");
            return Error(catalogErrors);
        }

        var name = manifest["name"]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) name = "Imported Profile";
        if (name.Length > 80) name = name[..80];
        name = UniqueProfileName(name);

        var id = GenerateProfileId(name);
        try { _store.CreateProfile(id, name); }
        catch (Exception ex) { return Error([ex.Message]); }

        var files = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            [DataKeys.Catalog] = catalog
        };
        if (module["profile"] is JsonObject profile) files[DataKeys.Profile] = profile;
        if (module["boost"] is JsonObject boost) files[DataKeys.Boost] = boost;
        if (module["roles"] is JsonObject roles) files[DataKeys.Roles] = roles;
        if (module["overlayConfig"] is JsonObject overlayConfig) files[DataKeys.OverlayConfig] = overlayConfig;

        try { _store.ImportProfileData(id, files); }
        catch (Exception ex)
        {
            try { _store.DeleteProfile(id); } catch { }
            return Error([$"Failed to write module data: {ex.Message}"]);
        }

        return Ok(new JsonObject { ["ok"] = true, ["id"] = id, ["name"] = name });
    }

    // ── Collection packs (.circuitcollection) ──────────────────────────────────────────────────
    // A pack is ONE collection plus its gameplay flavor (terminology, commands, messages, tuning) —
    // but never the theme. On import it becomes a new profile that adopts the importer's own colors,
    // brand, and overlay, so a shared collection "wears the importer's skin". Contrast with a
    // .circuitmodule, which is a whole profile (every collection + the sharer's branding).

    // collectionKey = a specific key to share one collection, or "" / "*" to share ALL permanent
    // collections at once. Event collections are stream-specific (their date windows mean nothing to a
    // recipient), so they never travel — not singly, not in "share all".
    public ServiceResult ExportCollectionPack(string collectionKey)
    {
        collectionKey = (collectionKey ?? "").Trim();
        var shareAll = collectionKey is "" or "*";

        var catalog = _store.TryRead(DataKeys.Catalog);
        if (catalog is null) return Error(["No catalog found in the active profile. Complete first-run setup before sharing."]);
        var collections = JsonUtil.Object(catalog, "collections");
        if (collections is null || collections.Count == 0) return Error(["There are no collections to share."]);

        var shared = new JsonObject();
        string? singleName = null;
        if (shareAll)
        {
            foreach (var (key, node) in collections)
                if (node is JsonObject c && !string.Equals(JsonUtil.String(c, "type"), "event", StringComparison.OrdinalIgnoreCase))
                    shared[key] = c.DeepClone();
            if (shared.Count == 0) return Error(["There are no permanent collections to share — event collections stay on your channel."]);
        }
        else
        {
            if (collections[collectionKey] is not JsonObject collection)
                return Error([$"Collection '{collectionKey}' was not found. Save the catalog first if you just added it."]);
            if (string.Equals(JsonUtil.String(collection, "type"), "event", StringComparison.OrdinalIgnoreCase))
                return Error(["Event collections are stream-specific and can't be shared."]);
            shared[collectionKey] = collection.DeepClone();
            singleName = JsonUtil.String(collection, "displayName").Trim();
            if (string.IsNullOrWhiteSpace(singleName)) singleName = collectionKey;
        }

        var profile = _store.TryRead(DataKeys.Profile) ?? DefaultProfile();
        var gameName = JsonUtil.String(profile, "gameName").Trim();
        if (string.IsNullOrWhiteSpace(gameName)) gameName = "Shared Collection";

        // Carry the flavor, strip the skin: theme/accent/colors/brandKicker/adminName come from
        // whoever imports it.
        var packProfile = (JsonObject)profile.DeepClone();
        packProfile.Remove("theme");
        packProfile.Remove("accent");
        packProfile.Remove("colors");
        packProfile.Remove("brandKicker");
        packProfile.Remove("adminName");

        var manifest = new JsonObject
        {
            ["format"] = "circuitcollection",
            ["version"] = "1",
            ["name"] = gameName,
            ["collectionCount"] = shared.Count,
            ["circuitosVersion"] = "0.9.0",
            ["exportedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        if (!shareAll)
        {
            manifest["collectionKey"] = collectionKey;
            manifest["collectionName"] = singleName;
        }

        return Ok(new JsonObject
        {
            ["manifest"] = manifest,
            ["collections"] = shared,
            ["profile"] = packProfile
        });
    }

    public ServiceResult ImportCollectionPack(JsonObject pack, string? requestedName)
    {
        var manifest = pack["manifest"] as JsonObject;
        if (manifest is null || manifest["format"]?.ToString() != "circuitcollection")
            return Error(["Not a valid .circuitcollection file."]);
        if (manifest["version"]?.ToString() != "1")
            return Error([$"Unsupported collection pack version \"{manifest["version"]?.ToString()}\"."]);

        var packCollections = pack["collections"] as JsonObject;
        if (packCollections is null && pack["collection"] is JsonObject legacySingle)
        {
            // Backward-compatible with the first pack shape (single "collection" + "collectionKey").
            var legacyKey = JsonUtil.String(pack, "collectionKey").Trim();
            if (string.IsNullOrWhiteSpace(legacyKey)) legacyKey = "shared";
            packCollections = new JsonObject { [legacyKey] = legacySingle.DeepClone() };
        }
        if (packCollections is null || packCollections.Count == 0)
            return Error(["Collection pack is missing its collection data."]);

        var name = (requestedName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = JsonUtil.String(manifest, "name").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Imported Collection";
        if (name.Length > 80) name = name[..80];
        name = UniqueProfileName(name);

        // New profile = pack flavor + the importer's own skin (colors/brand + overlay).
        var merged = pack["profile"] is JsonObject pp ? (JsonObject)pp.DeepClone() : new JsonObject();
        merged["gameName"] = name;
        var current = _store.TryRead(DataKeys.Profile);
        if (current is not null)
        {
            if (current["theme"] is { } theme) merged["theme"] = theme.DeepClone();
            if (current["accent"] is { } accent) merged["accent"] = accent.DeepClone();
            if (current["colors"] is { } colors) merged["colors"] = colors.DeepClone();
            if (current["brandKicker"] is { } brandKicker) merged["brandKicker"] = brandKicker.DeepClone();
            if (current["adminName"] is { } adminName) merged["adminName"] = adminName.DeepClone();
        }
        var newProfile = NormalizeProfile(merged);

        var importedCollections = new JsonObject();
        foreach (var (key, node) in packCollections)
            if (node is JsonObject c) importedCollections[key] = c.DeepClone();
        var newCatalog = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["collections"] = importedCollections
        };

        // Validate before creating anything, so a corrupt pack can't leave a broken profile behind.
        var packErrors = ValidateConfiguration(newCatalog, DefaultBoost());
        if (packErrors.Count > 0)
        {
            packErrors.Insert(0, "This collection pack is invalid — nothing was imported.");
            return Error(packErrors);
        }

        var id = GenerateProfileId(name);
        try { _store.CreateProfile(id, name); }
        catch (Exception ex) { return Error([ex.Message]); }

        var files = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            [DataKeys.Catalog] = newCatalog,
            [DataKeys.Profile] = newProfile
        };
        var overlayConfig = _store.TryRead(DataKeys.OverlayConfig);
        if (overlayConfig is not null) files[DataKeys.OverlayConfig] = overlayConfig.DeepClone();

        try { _store.ImportProfileData(id, files); }
        catch (Exception ex)
        {
            try { _store.DeleteProfile(id); } catch { }
            return Error([$"Failed to write collection pack: {ex.Message}"]);
        }

        return Ok(new JsonObject { ["ok"] = true, ["id"] = id, ["name"] = name });
    }
}
