using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Holds the current Twitch tokens and hands out a valid access token, refreshing a few minutes
// before expiry (and persisting the new tokens). Shared by the Helix client and the EventSub
// WebSocket so a long stream never runs on a stale token.
internal sealed class TwitchSession
{
    private readonly TwitchOptions _options;
    private readonly string _dataRoot;
    private TwitchTokens _tokens;

    public TwitchSession(TwitchOptions options, TwitchTokens tokens, string dataRoot)
    {
        _options = options;
        _tokens = tokens;
        _dataRoot = dataRoot;
    }

    public string UserId => _tokens.UserId;
    public string Login => _tokens.Login;
    public string ClientId => _options.ClientId;

    // A valid bearer token — refreshes if within 5 minutes of expiry.
    public string AccessToken
    {
        get
        {
            if (DateTimeOffset.UtcNow >= _tokens.ExpiresAt - TimeSpan.FromMinutes(5)) Refresh();
            return _tokens.AccessToken;
        }
    }

    public void Refresh() => _tokens = TwitchAuth.Refresh(_options, _tokens, _dataRoot);
}

internal sealed record CustomReward(string Id, string Title, int Cost);

// Thin authenticated wrapper over the Twitch Helix REST API. Adds the Bearer + Client-Id headers,
// refreshes once on a 401, and surfaces clear errors. Used for channel-point reward management and
// redemption fulfilment in the native Twitch path.
internal sealed class TwitchHelix
{
    private static readonly HttpClient Http = new();
    private readonly TwitchSession _session;

    public TwitchHelix(TwitchSession session) => _session = session;

    private string RewardsUrl => $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={_session.UserId}";

    // Creates the channel-point reward if absent, or keeps its cost/enabled state in sync if a reward
    // with the same title already exists. Idempotent — safe to call on every go-live.
    public CustomReward EnsureReward(string title, int cost, string prompt)
    {
        var existing = ListManageableRewards().FirstOrDefault(r => string.Equals(r.Title, title, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Cost == cost) return existing;
            var updated = Send(HttpMethod.Patch, RewardsUrl + $"&id={existing.Id}",
                new JsonObject { ["cost"] = cost, ["is_enabled"] = true });
            return ParseRewards(updated).FirstOrDefault() ?? existing with { Cost = cost };
        }
        var created = Send(HttpMethod.Post, RewardsUrl, new JsonObject
        {
            ["title"] = title,
            ["cost"] = cost,
            ["prompt"] = prompt,
            ["is_enabled"] = true,
            ["is_user_input_required"] = false
        });
        return ParseRewards(created).FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch returned no reward after creation.");
    }

    // Rewards this client_id created (only those are manageable + fulfillable by us).
    public List<CustomReward> ListManageableRewards()
        => ParseRewards(Send(HttpMethod.Get, RewardsUrl + "&only_manageable_rewards=true", null));

    // Marks a redemption FULFILLED (pull succeeded) or CANCELED (refunds the points).
    public void UpdateRedemptionStatus(string rewardId, string redemptionId, bool fulfilled)
    {
        var url = "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions"
            + $"?broadcaster_id={_session.UserId}&reward_id={rewardId}&id={redemptionId}";
        Send(HttpMethod.Patch, url, new JsonObject { ["status"] = fulfilled ? "FULFILLED" : "CANCELED" });
    }

    // Registers an EventSub subscription bound to an open WebSocket session (no public endpoint
    // needed). Used to subscribe to channel-point redemptions on the WebSocket transport.
    public void CreateEventSubSubscription(string type, string version, JsonObject condition, string sessionId)
    {
        Send(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions", new JsonObject
        {
            ["type"] = type,
            ["version"] = version,
            ["condition"] = condition,
            ["transport"] = new JsonObject { ["method"] = "websocket", ["session_id"] = sessionId }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static List<CustomReward> ParseRewards(JsonObject response)
    {
        var rewards = new List<CustomReward>();
        if (response["data"] is not JsonArray data) return rewards;
        foreach (var node in data)
        {
            if (node is not JsonObject reward) continue;
            var id = reward["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;
            var cost = int.TryParse(reward["cost"]?.ToString(), out var c) ? c : 0;
            rewards.Add(new CustomReward(id, reward["title"]?.ToString() ?? "", cost));
        }
        return rewards;
    }

    private JsonObject Send(HttpMethod method, string url, JsonObject? body, bool retried = false)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        request.Headers.Add("Client-Id", _session.ClientId);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(JsonUtil.IndentedOptions), Encoding.UTF8, "application/json");

        using var response = Http.Send(request);
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (response.StatusCode == HttpStatusCode.Unauthorized && !retried)
        {
            _session.Refresh();
            return Send(method, url, body, retried: true);
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twitch Helix {method} {Trim(url)} → {(int)response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject ?? new JsonObject();
    }

    // Drop the query string (it carries the broadcaster id) from error messages.
    private static string Trim(string url) => url.Split('?')[0];
}
