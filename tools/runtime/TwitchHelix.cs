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
    private readonly string _fileName;
    private TwitchTokens _tokens;

    // fileName routes refreshed tokens back to the right store — the broadcaster login by default,
    // TwitchTokens.BotFileName for the optional bot chat account.
    public TwitchSession(TwitchOptions options, TwitchTokens tokens, string dataRoot, string fileName = TwitchTokens.FileName)
    {
        _options = options;
        _tokens = tokens;
        _dataRoot = dataRoot;
        _fileName = fileName;
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

    public void Refresh() => _tokens = TwitchAuth.Refresh(_options, _tokens, _dataRoot, _fileName);
}

internal sealed record CustomReward(string Id, string Title, int Cost, bool Manageable = true);

// Thin authenticated wrapper over the Twitch Helix REST API. Adds the Bearer + Client-Id headers,
// refreshes once on a 401, and surfaces clear errors. Used for channel-point reward management and
// redemption fulfilment in the native Twitch path.
internal sealed class TwitchHelix
{
    private static readonly HttpClient Http = new();
    private readonly TwitchSession _session;
    private readonly TwitchSession? _botSession;

    // botSession is the optional dedicated bot chat account: when present, chat messages are SENT as
    // the bot (sender_id + bot token); everything else (rewards, redemptions, EventSub) stays on the
    // broadcaster session. See docs/feature-requests-analysis.md §1.
    public TwitchHelix(TwitchSession session, TwitchSession? botSession = null)
    {
        _session = session;
        _botSession = botSession;
    }

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
            return ParseRewards(updated, manageable: true).FirstOrDefault() ?? existing with { Cost = cost, Manageable = true };
        }
        var created = Send(HttpMethod.Post, RewardsUrl, new JsonObject
        {
            ["title"] = title,
            ["cost"] = cost,
            ["prompt"] = prompt,
            ["is_enabled"] = true,
            ["is_user_input_required"] = false
        });
        return ParseRewards(created, manageable: true).FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch returned no reward after creation.");
    }

    // Updates a reward created by this app. Twitch only allows editing manageable rewards.
    public CustomReward UpdateReward(string rewardId, string title, int cost, string prompt)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) throw new InvalidDataException("A Twitch reward id is required.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("A Twitch reward title is required.");
        if (cost < 1) throw new InvalidDataException("Twitch reward cost must be at least 1 point.");
        var updated = Send(HttpMethod.Patch, RewardsUrl + $"&id={Uri.EscapeDataString(rewardId)}", new JsonObject
        {
            ["title"] = title.Trim(),
            ["cost"] = cost,
            ["prompt"] = prompt,
            ["is_enabled"] = true,
            ["is_user_input_required"] = false
        });
        return ParseRewards(updated, manageable: true).FirstOrDefault()
            ?? new CustomReward(rewardId, title.Trim(), cost, Manageable: true);
    }
    // Rewards visible to the broadcaster token. Some may not be manageable by this app.
    public List<CustomReward> ListRewards()
        => ParseRewards(Send(HttpMethod.Get, RewardsUrl, null), manageable: false);

    // Rewards this client_id created (only those are manageable + fulfillable by us).
    public List<CustomReward> ListManageableRewards()
        => ParseRewards(Send(HttpMethod.Get, RewardsUrl + "&only_manageable_rewards=true", null), manageable: true);

    // Deletes a reward created by this app. Twitch only allows deleting manageable rewards.
    public void DeleteReward(string rewardId)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) throw new InvalidDataException("A Twitch reward id is required.");
        Send(HttpMethod.Delete, RewardsUrl + $"&id={Uri.EscapeDataString(rewardId)}", null);
    }

    // Marks a redemption FULFILLED (pull succeeded) or CANCELED (refunds the points).
    public void UpdateRedemptionStatus(string rewardId, string redemptionId, bool fulfilled)
    {
        var url = "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions"
            + $"?broadcaster_id={_session.UserId}&reward_id={rewardId}&id={redemptionId}";
        Send(HttpMethod.Patch, url, new JsonObject { ["status"] = fulfilled ? "FULFILLED" : "CANCELED" });
    }

    // Sends a chat message to the broadcaster's channel. With a bot account connected the message
    // posts AS the bot (sender_id = bot, bot token — Twitch requires the sender's own token);
    // otherwise it posts as the broadcaster, exactly as before.
    public void SendChatMessage(string text)
    {
        var sender = _botSession ?? _session;
        Send(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages", new JsonObject
        {
            ["broadcaster_id"] = _session.UserId,
            ["sender_id"] = sender.UserId,
            ["message"] = text
        }, session: sender);
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

    private static List<CustomReward> ParseRewards(JsonObject response, bool manageable)
    {
        var rewards = new List<CustomReward>();
        if (response["data"] is not JsonArray data) return rewards;
        foreach (var node in data)
        {
            if (node is not JsonObject reward) continue;
            var id = reward["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;
            var cost = int.TryParse(reward["cost"]?.ToString(), out var c) ? c : 0;
            rewards.Add(new CustomReward(id, reward["title"]?.ToString() ?? "", cost, manageable));
        }
        return rewards;
    }

    private JsonObject Send(HttpMethod method, string url, JsonObject? body, bool retried = false, TwitchSession? session = null)
    {
        var auth = session ?? _session;
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Add("Client-Id", auth.ClientId);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(JsonUtil.IndentedOptions), Encoding.UTF8, "application/json");

        using var response = Http.Send(request);
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (response.StatusCode == HttpStatusCode.Unauthorized && !retried)
        {
            auth.Refresh();
            return Send(method, url, body, retried: true, session: auth);
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twitch Helix {method} {Trim(url)} → {(int)response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject ?? new JsonObject();
    }

    // Drop the query string (it carries the broadcaster id) from error messages.
    private static string Trim(string url) => url.Split('?')[0];
}



