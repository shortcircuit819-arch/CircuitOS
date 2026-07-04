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
            ["profiles"] = new JsonArray(profiles.Select(p => ProfileObject(p, _store.ReadProfileData(p.Id, DataKeys.TwitchRewards))).ToArray())
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

            case "activate":
            {
                var id = JsonUtil.String(request, "id");
                if (string.IsNullOrWhiteSpace(id)) return Error(["Profile id is required."]);
                // A profile can only go live if its commands don't collide with another live profile.
                var target = _store.ReadProfileData(id, DataKeys.Profile);
                List<string> collisions = target is null ? [] : LiveProfileCollisions(target, id);
                if (collisions.Count > 0) return Error(collisions);
                try { _store.SetProfileActive(id, true); }
                catch (Exception ex) { return Error([ex.Message]); }
                return Ok(new JsonObject { ["ok"] = true, ["id"] = id, ["isLive"] = true });
            }

            case "deactivate":
            {
                var id = JsonUtil.String(request, "id");
                if (string.IsNullOrWhiteSpace(id)) return Error(["Profile id is required."]);
                try { _store.SetProfileActive(id, false); }
                catch (Exception ex) { return Error([ex.Message]); }
                return Ok(new JsonObject { ["ok"] = true, ["id"] = id, ["isLive"] = false });
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

    // Makes the DISPLAY name unique among existing profiles by appending " (2)", " (3)", … .
    // GenerateProfileId already makes the id unique; this keeps the visible name unambiguous so an
    // import can never produce two identically-labelled profiles (the duplicate-twin footgun).
    private string UniqueProfileName(string name)
    {
        var existing = _store.ListProfiles().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name)) return name;
        for (var n = 2; n < 1000; n++)
        {
            var suffix = $" ({n})";
            var baseName = name.Length + suffix.Length > 80 ? name[..(80 - suffix.Length)].TrimEnd() : name;
            var candidate = baseName + suffix;
            if (!existing.Contains(candidate)) return candidate;
        }
        return name;
    }

    private bool IsProfileLive(string id) => _store.ListProfiles().Any(p => p.Id == id && p.IsLive);

    private List<string> LiveProfileCollisions(JsonObject profile, string selfProfileId)
    {
        var errors = CommandCollisions(profile, selfProfileId);
        errors.AddRange(RedemptionCollisions(profile, selfProfileId));
        return errors;
    }

    // Returns one error per incoming command word that another LIVE profile already uses, so
    // two simultaneously-active games can't both own (e.g.) !inventory. Excludes selfProfileId.
    private List<string> CommandCollisions(JsonObject profile, string selfProfileId)
    {
        var errors = new List<string>();
        var commands = JsonUtil.Object(profile, "commands");
        if (commands is null) return errors;

        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // word → owning profile name
        foreach (var other in _store.ListProfiles())
        {
            if (!other.IsLive || other.Id == selfProfileId) continue;
            var otherCommands = _store.ReadProfileData(other.Id, DataKeys.Profile) is { } op ? JsonUtil.Object(op, "commands") : null;
            if (otherCommands is null) continue;
            foreach (var field in CommandFields)
            {
                var word = JsonUtil.String(otherCommands, field);
                if (!string.IsNullOrWhiteSpace(word)) taken[word] = other.Name;
            }
        }
        if (taken.Count == 0) return errors;

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in CommandFields)
        {
            var word = JsonUtil.String(commands, field);
            if (!string.IsNullOrWhiteSpace(word) && taken.TryGetValue(word, out var owner) && reported.Add(word))
                errors.Add($"Command '!{word}' is already used by the active profile '{owner}'. Rename it before saving.");
        }
        return errors;
    }

    // The native Twitch path maps channel-point reward id -> profile. If two live profiles share
    // the same redemption title, Twitch reward sync can collapse them onto the same reward id.
    private List<string> RedemptionCollisions(JsonObject profile, string selfProfileId)
    {
        var errors = new List<string>();
        var title = JsonUtil.String(profile, "redemptionName").Trim();
        if (string.IsNullOrWhiteSpace(title)) return errors;

        foreach (var other in _store.ListProfiles())
        {
            if (!other.IsLive || other.Id == selfProfileId) continue;
            var otherProfile = _store.ReadProfileData(other.Id, DataKeys.Profile);
            var otherTitle = JsonUtil.String(otherProfile, "redemptionName").Trim();
            if (string.Equals(title, otherTitle, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Redemption '{title}' is already used by the active profile '{other.Name}'. Rename it before saving or going live.");
                break;
            }
        }
        return errors;
    }

    private static JsonObject ProfileObject(ProfileInfo p, JsonObject? twitchRewards = null)
    {
        var profile = new JsonObject
        {
            ["id"] = p.Id,
            ["name"] = p.Name,
            ["createdAt"] = p.CreatedAt.ToString("O"),
            ["isActive"] = p.IsActive,
            ["isLive"] = p.IsLive
        };
        var rewards = JsonUtil.Object(twitchRewards, "rewards");
        if (JsonUtil.Object(rewards, "channelPoints") is { } channelPoints)
        {
            profile["twitchReward"] = JsonUtil.Clone(channelPoints);
        }
        return profile;
    }
}
