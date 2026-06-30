using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Twitch OAuth app credentials for the desktop login flow (Phase 3). Loaded from
// <dataRoot>/twitch.local.json (gitignored). The client secret is the user's own
// Twitch app secret, kept local; it is never logged. See docs/0.7-twitch-auth-setup.md.
internal sealed record TwitchOptions(string ClientId, string ClientSecret, string RedirectUri)
{
    public const string FileName = "twitch.local.json";
    public const string DefaultRedirectUri = "http://localhost:8765";

    // The bundled CircuitOS Twitch application client id. clientId is PUBLIC by design (it appears in
    // every OAuth request), so shipping it is safe — unlike a client secret, which must never be
    // distributed. With this set, end users log in via the Device Code Flow with no twitch.local.json
    // and no per-streamer Twitch app. The Twitch app MUST be registered with Client Type = Public for
    // the device flow to work. twitch.local.json (if present) overrides this for self-hosters.
    public const string DefaultClientId = "rs7hti26ty98in6ltdjd8rb980wjjb";

    // Device flow needs no secret. When no secret is available we must use the device flow; the
    // legacy loopback authorization-code flow is only used when a secret is present (advanced/self-host).
    public bool HasSecret => !string.IsNullOrWhiteSpace(ClientSecret);

    // Returns usable options without ever failing on a missing file: a present twitch.local.json wins
    // (clientId required, secret optional), otherwise the bundled clientId is used. Throws only if no
    // client id is available anywhere (bundled default not set AND no file) — a build/config gap.
    public static TwitchOptions Resolve(string dataRoot)
    {
        var path = Path.Combine(dataRoot, FileName);
        if (File.Exists(path))
        {
            JsonObject json;
            try
            {
                json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidDataException($"{FileName} must contain a JSON object.");
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) { throw new InvalidDataException($"{FileName} is not valid JSON: {ex.Message}"); }

            var fileClientId = json["clientId"]?.ToString()?.Trim() ?? "";
            var fileSecret = json["clientSecret"]?.ToString()?.Trim() ?? "";
            var fileRedirect = json["redirectUri"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(fileClientId)) fileClientId = DefaultClientId;
            if (string.IsNullOrWhiteSpace(fileClientId))
                throw new InvalidDataException($"{FileName} has no clientId and no bundled default is set. See docs/0.7-twitch-auth-setup.md.");
            return new TwitchOptions(fileClientId, fileSecret,
                string.IsNullOrWhiteSpace(fileRedirect) ? DefaultRedirectUri : fileRedirect!);
        }

        if (string.IsNullOrWhiteSpace(DefaultClientId))
            throw new InvalidDataException($"Twitch login isn't configured: no {FileName} and no bundled client id. See docs/0.7-twitch-auth-setup.md.");
        return new TwitchOptions(DefaultClientId, "", DefaultRedirectUri);
    }

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
