using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Twitch OAuth app credentials for the desktop login flow (Phase 3). Loaded from
// <dataRoot>/twitch.local.json (gitignored). The client secret is the user's own
// Twitch app secret, kept local; it is never logged. See docs/0.7-twitch-auth-setup.md.
internal sealed record TwitchOptions(string ClientId, string ClientSecret, string RedirectUri)
{
    public const string FileName = "twitch.local.json";
    public const string DefaultRedirectUri = "http://localhost:8765";

    public static TwitchOptions? TryLoad(string dataRoot)
    {
        var path = Path.Combine(dataRoot, FileName);
        if (!File.Exists(path)) return null;

        JsonObject json;
        try
        {
            json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException($"{FileName} must contain a JSON object.");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) { throw new InvalidDataException($"{FileName} is not valid JSON: {ex.Message}"); }

        var clientId = json["clientId"]?.ToString()?.Trim() ?? "";
        var clientSecret = json["clientSecret"]?.ToString()?.Trim() ?? "";
        var redirect = json["redirectUri"]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(redirect)) redirect = DefaultRedirectUri;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(clientId)) missing.Add("clientId");
        if (string.IsNullOrWhiteSpace(clientSecret)) missing.Add("clientSecret");
        if (missing.Count > 0)
            throw new InvalidDataException($"{FileName} is missing required field(s): {string.Join(", ", missing)}. See docs/0.7-twitch-auth-setup.md.");

        return new TwitchOptions(clientId, clientSecret, redirect!);
    }

    // Safe-to-log summary — the client secret is reduced to a length.
    public string Describe() =>
        $"clientId={ClientId}, redirect={RedirectUri}, " +
        $"clientSecret={(string.IsNullOrEmpty(ClientSecret) ? "MISSING" : $"set ({ClientSecret.Length} chars)")}";
}
