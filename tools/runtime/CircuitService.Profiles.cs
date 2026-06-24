using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CircuitOS.Runtime;

internal sealed partial class CircuitService
{
    public JsonObject GetProfiles()
    {
        var profiles = _store.ListProfiles();
        return new JsonObject
        {
            ["ok"] = true,
            ["activeProfileId"] = _store.ActiveProfileId,
            ["profiles"] = new JsonArray(profiles.Select(p => ProfileObject(p)).ToArray())
        };
    }

    public ServiceResult InvokeProfileOperation(JsonObject request)
    {
        var operation = JsonUtil.String(request, "operation");
        switch (operation)
        {
            case "create":
            {
                var name = JsonUtil.String(request, "name").Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
                    return Error(["Profile name must be 1 to 80 characters."]);
                var id = GenerateProfileId(name);
                try { _store.CreateProfile(id, name); }
                catch (Exception ex) { return Error([ex.Message]); }
                return Ok(new JsonObject { ["ok"] = true, ["id"] = id, ["name"] = name });
            }

            case "switch":
            {
                var id = JsonUtil.String(request, "id");
                if (string.IsNullOrWhiteSpace(id)) return Error(["Profile id is required."]);
                try { _store.SwitchProfile(id); }
                catch (Exception ex) { return Error([ex.Message]); }
                return Ok(new JsonObject
                {
                    ["ok"] = true,
                    ["activeProfileId"] = _store.ActiveProfileId,
                    ["dataPath"] = DisplayDataPath
                });
            }

            case "rename":
            {
                var id = JsonUtil.String(request, "id");
                var name = JsonUtil.String(request, "name").Trim();
                if (string.IsNullOrWhiteSpace(id)) return Error(["Profile id is required."]);
                if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
                    return Error(["Profile name must be 1 to 80 characters."]);
                try { _store.RenameProfile(id, name); }
                catch (Exception ex) { return Error([ex.Message]); }
                return Ok(new JsonObject { ["ok"] = true, ["id"] = id, ["name"] = name });
            }

            case "delete":
            {
                var id = JsonUtil.String(request, "id");
                if (string.IsNullOrWhiteSpace(id)) return Error(["Profile id is required."]);
                try { _store.DeleteProfile(id); }
                catch (Exception ex) { return Error([ex.Message]); }
                return Ok(new JsonObject { ["ok"] = true, ["deletedId"] = id });
            }

            default:
                return Error(["Unknown profile operation."]);
        }
    }

    private string GenerateProfileId(string name)
    {
        var existing = _store.ListProfiles().Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slug = Regex.Replace(name.ToLowerInvariant().Trim(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "profile";
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        var id = slug;
        var counter = 2;
        while (existing.Contains(id)) { id = $"{slug}-{counter++}"; }
        return id;
    }

    private static JsonObject ProfileObject(ProfileInfo p) => new()
    {
        ["id"] = p.Id,
        ["name"] = p.Name,
        ["createdAt"] = p.CreatedAt.ToString("O"),
        ["isActive"] = p.IsActive
    };
}
