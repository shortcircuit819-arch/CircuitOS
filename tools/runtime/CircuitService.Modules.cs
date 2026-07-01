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
                ["circuitosVersion"] = "0.7.0.1",
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
}
