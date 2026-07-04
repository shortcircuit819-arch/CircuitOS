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
                ["circuitosVersion"] = "0.7.3",
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

    public ServiceResult ExportCollectionPack(string collectionKey)
    {
        collectionKey = (collectionKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(collectionKey)) return Error(["Choose a collection to share."]);

        var catalog = _store.TryRead(DataKeys.Catalog);
        if (catalog is null) return Error(["No catalog found in the active profile. Complete first-run setup before sharing."]);
        var collections = JsonUtil.Object(catalog, "collections");
        if (collections?[collectionKey] is not JsonObject collection)
            return Error([$"Collection '{collectionKey}' was not found. Save the catalog first if you just added it."]);

        var profile = _store.TryRead(DataKeys.Profile) ?? DefaultProfile();
        var gameName = JsonUtil.String(profile, "gameName").Trim();
        if (string.IsNullOrWhiteSpace(gameName)) gameName = "Shared Collection";
        var collectionName = JsonUtil.String(collection, "displayName").Trim();
        if (string.IsNullOrWhiteSpace(collectionName)) collectionName = collectionKey;

        // Carry the flavor, strip the skin: colors/brandKicker/adminName come from whoever imports it.
        var packProfile = (JsonObject)profile.DeepClone();
        packProfile.Remove("colors");
        packProfile.Remove("brandKicker");
        packProfile.Remove("adminName");

        return Ok(new JsonObject
        {
            ["manifest"] = new JsonObject
            {
                ["format"] = "circuitcollection",
                ["version"] = "1",
                ["name"] = gameName,
                ["collectionKey"] = collectionKey,
                ["collectionName"] = collectionName,
                ["circuitosVersion"] = "0.7.3",
                ["exportedAt"] = DateTimeOffset.UtcNow.ToString("O")
            },
            ["collectionKey"] = collectionKey,
            ["collection"] = collection.DeepClone(),
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

        if (pack["collection"] is not JsonObject collection)
            return Error(["Collection pack is missing its collection data."]);

        var collectionKey = JsonUtil.String(pack, "collectionKey").Trim();
        if (string.IsNullOrWhiteSpace(collectionKey)) collectionKey = JsonUtil.String(manifest, "collectionKey").Trim();
        if (string.IsNullOrWhiteSpace(collectionKey)) collectionKey = "shared";

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
            if (current["colors"] is { } colors) merged["colors"] = colors.DeepClone();
            if (current["brandKicker"] is { } brandKicker) merged["brandKicker"] = brandKicker.DeepClone();
            if (current["adminName"] is { } adminName) merged["adminName"] = adminName.DeepClone();
        }
        var newProfile = NormalizeProfile(merged);

        var newCatalog = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["collections"] = new JsonObject { [collectionKey] = collection.DeepClone() }
        };

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
