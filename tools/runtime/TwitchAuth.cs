using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Cached Twitch tokens + identity, persisted to <dataRoot>/twitch-tokens.local.json
// (gitignored). Plaintext for dev; Windows DPAPI encryption is a hardening follow-up.
internal sealed record TwitchTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string UserId,
    string Login,
    string DisplayName)
{
    public const string FileName = "twitch-tokens.local.json";

    public static TwitchTokens? TryLoad(string dataRoot)
    {
        var path = Path.Combine(dataRoot, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (json is null) return null;
            var userId = json["userId"]?.ToString();
            if (string.IsNullOrWhiteSpace(userId)) return null;
            return new TwitchTokens(
                json["accessToken"]?.ToString() ?? "",
                json["refreshToken"]?.ToString() ?? "",
                DateTimeOffset.TryParse(json["expiresAt"]?.ToString(), out var dt) ? dt : DateTimeOffset.MinValue,
                userId!,
                json["login"]?.ToString() ?? "",
                json["displayName"]?.ToString() ?? "");
        }
        catch { return null; }
    }

    public void Save(string dataRoot)
    {
        var json = new JsonObject
        {
            ["accessToken"] = AccessToken,
            ["refreshToken"] = RefreshToken,
            ["expiresAt"] = ExpiresAt.ToString("O"),
            ["userId"] = UserId,
            ["login"] = Login,
            ["displayName"] = DisplayName
        };
        File.WriteAllText(Path.Combine(dataRoot, FileName), json.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
    }
}

// Desktop Twitch OAuth: authorization-code flow with a loopback redirect. Opens the
// user's browser, captures the redirect on a local HttpListener, exchanges the code,
// and fetches the user's identity from Helix. See docs/0.7-twitch-auth-setup.md.
internal static class TwitchAuth
{
    private static readonly string[] Scopes =
    [
        "channel:read:redemptions", "channel:manage:redemptions",
        "user:read:chat", "user:write:chat"   // chat commands + replies (slice 3)
    ];
    private static readonly HttpClient Http = new();

    public static TwitchTokens Login(TwitchOptions opts, string dataRoot)
    {
        var state = Guid.NewGuid().ToString("N");
        var prefix = opts.RedirectUri.EndsWith('/') ? opts.RedirectUri : opts.RedirectUri + "/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var authorizeUrl = "https://id.twitch.tv/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(opts.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(opts.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(string.Join(' ', Scopes))}"
            + $"&state={state}";
        OpenBrowser(authorizeUrl);

        var contextTask = listener.GetContextAsync();
        if (!contextTask.Wait(TimeSpan.FromMinutes(3)))
        {
            listener.Stop();
            throw new TimeoutException("Timed out waiting for Twitch authorization (3 minutes).");
        }
        var context = contextTask.Result;
        var query = ParseQuery(context.Request.Url?.Query ?? "");
        RespondHtml(context, "CircuitOS — Twitch login complete. You can close this tab and return to CircuitOS.");
        listener.Stop();

        if (query.TryGetValue("error", out var error))
            throw new InvalidOperationException($"Twitch authorization was denied: {error} {query.GetValueOrDefault("error_description")}");
        if (query.GetValueOrDefault("state") != state)
            throw new InvalidOperationException("OAuth state mismatch — login aborted for safety.");
        var code = query.GetValueOrDefault("code")
            ?? throw new InvalidOperationException("Twitch did not return an authorization code.");

        var token = PostForm("https://id.twitch.tv/oauth2/token", new Dictionary<string, string>
        {
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = opts.RedirectUri
        });
        var accessToken = token["access_token"]?.ToString()
            ?? throw new InvalidOperationException("No access_token in Twitch token response.");
        var refreshToken = token["refresh_token"]?.ToString() ?? "";
        var expiresIn = int.TryParse(token["expires_in"]?.ToString(), out var e) ? e : 3600;

        var identity = FetchIdentity(opts.ClientId, accessToken);
        var tokens = new TwitchTokens(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            identity.UserId, identity.Login, identity.DisplayName);
        tokens.Save(dataRoot);
        return tokens;
    }

    // Exchanges the stored refresh token for a fresh access token (Twitch access tokens last
    // ~4h, so a long stream needs this). Returns updated tokens and re-saves them. Throws if
    // the refresh token is no longer valid (the user must log in again).
    public static TwitchTokens Refresh(TwitchOptions opts, TwitchTokens current, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(current.RefreshToken))
            throw new InvalidOperationException("No Twitch refresh token on file — please log in again.");
        var token = PostForm("https://id.twitch.tv/oauth2/token", new Dictionary<string, string>
        {
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken
        });
        var accessToken = token["access_token"]?.ToString()
            ?? throw new InvalidOperationException("No access_token in Twitch refresh response.");
        var refreshToken = token["refresh_token"]?.ToString() ?? current.RefreshToken;
        var expiresIn = int.TryParse(token["expires_in"]?.ToString(), out var e) ? e : 3600;
        var tokens = current with
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
        };
        tokens.Save(dataRoot);
        return tokens;
    }

    private static (string UserId, string Login, string DisplayName) FetchIdentity(string clientId, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", clientId);
        using var response = Http.Send(request);
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twitch /helix/users returned {(int)response.StatusCode}: {text}");
        var user = (JsonNode.Parse(text) as JsonObject)?["data"] as JsonArray;
        var first = user?.FirstOrDefault() as JsonObject
            ?? throw new InvalidOperationException("Twitch /helix/users returned no user.");
        return (first["id"]?.ToString() ?? throw new InvalidOperationException("Twitch user has no id."),
                first["login"]?.ToString() ?? "",
                first["display_name"]?.ToString() ?? "");
    }

    private static JsonObject PostForm(string url, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = Http.PostAsync(url, content).GetAwaiter().GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twitch token endpoint returned {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException("Twitch token response was not a JSON object.");
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* if launch fails, the URL is still in the error path for manual paste */ }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return result;
    }

    private static void RespondHtml(HttpListenerContext context, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"<html><body style='font-family:sans-serif;background:#0b0f14;color:#e6edf3;padding:48px'>" +
            $"<h2>{message}</h2></body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }
}
