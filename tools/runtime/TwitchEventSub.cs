using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed record RedemptionEvent(string RewardId, string RewardTitle, string RedemptionId, string UserId, string UserName);
internal sealed record ChatMessage(string UserId, string UserName, string Text);

// Twitch EventSub over WebSocket — the native, hosting-free intake. Connects outbound to
// wss://eventsub.wss.twitch.tv/ws, and on the session_welcome creates a channel-point redemption
// subscription bound to that session. Notifications are parsed into RedemptionEvents and handed to
// the caller (which dispatches the pull and fulfils/cancels the redemption). Handles keepalive and
// session_reconnect; reconnects with a short backoff on transient errors.
internal sealed class TwitchEventSub
{
    private const string DefaultUrl = "wss://eventsub.wss.twitch.tv/ws";

    private readonly TwitchSession _session;
    private readonly TwitchHelix _helix;
    private readonly Action<RedemptionEvent> _onRedemption;
    private readonly Action<ChatMessage>? _onChat;
    private readonly Action<string> _log;

    // Twitch may replay a notification; the spec requires dedup by metadata.message_id. We keep the
    // recently-seen ids (with arrival time) and drop repeats so a replay can't double-process a pull.
    private readonly Dictionary<string, DateTime> _seenMessageIds = new(StringComparer.Ordinal);
    // Twitch closes the socket if it sends nothing for keepalive_timeout_seconds. If the connection
    // half-dies (no FIN), ReceiveAsync would block forever; we time out reads at keepalive + grace and
    // force a reconnect. Default to Twitch's 10s until session_welcome tells us the real value.
    private int _keepaliveSeconds = 10;
    private const int KeepaliveGraceSeconds = 5;

    public TwitchEventSub(TwitchSession session, TwitchHelix helix, Action<RedemptionEvent> onRedemption, Action<ChatMessage>? onChat, Action<string> log)
    {
        _session = session;
        _helix = helix;
        _onRedemption = onRedemption;
        _onChat = onChat;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancel)
    {
        var url = DefaultUrl;
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                url = await ConnectAndListenAsync(url, cancel) ?? DefaultUrl;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"EventSub connection dropped: {ex.Message}. Reconnecting in 3s…");
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancel); } catch (OperationCanceledException) { break; }
                url = DefaultUrl;
            }
        }
    }

    // Connects, handles messages until the socket closes or a reconnect is requested.
    // Returns a reconnect URL when Twitch sends session_reconnect, otherwise null. Throws
    // TimeoutException if no message arrives within the keepalive window so RunAsync reconnects.
    private async Task<string?> ConnectAndListenAsync(string url, CancellationToken cancel)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), cancel);

        var buffer = new byte[16 * 1024];
        var builder = new StringBuilder();
        while (!cancel.IsCancellationRequested)
        {
            builder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await ReceiveWithKeepaliveAsync(socket, buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancel); } catch { }
                    return null;
                }
                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            if (JsonNode.Parse(builder.ToString()) is not JsonObject message) continue;
            var reconnectUrl = HandleMessage(message);
            if (reconnectUrl is not null) return reconnectUrl;
        }
        return null;
    }

    // A single ReceiveAsync bounded by the keepalive window. If nothing (event OR keepalive) arrives
    // in time, the connection is presumed dead: abort the socket and throw so RunAsync reconnects.
    private async Task<WebSocketReceiveResult> ReceiveWithKeepaliveAsync(ClientWebSocket socket, byte[] buffer, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(_keepaliveSeconds + KeepaliveGraceSeconds));
        try
        {
            return await socket.ReceiveAsync(buffer, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            try { socket.Abort(); } catch { }
            throw new TimeoutException($"No EventSub message within {_keepaliveSeconds + KeepaliveGraceSeconds}s (keepalive missed).");
        }
    }

    private string? HandleMessage(JsonObject message)
    {
        var metadata = message["metadata"] as JsonObject;
        var type = metadata?["message_type"]?.ToString();
        var payload = message["payload"] as JsonObject;

        // Drop replays: Twitch can redeliver a message; a repeated message_id means we already saw it.
        var messageId = metadata?["message_id"]?.ToString();
        if (!string.IsNullOrEmpty(messageId) && !RememberMessageId(messageId!))
            return null;

        switch (type)
        {
            case "session_welcome":
                var session = payload?["session"] as JsonObject;
                if (session?["keepalive_timeout_seconds"]?.GetValue<int>() is int ka && ka > 0) _keepaliveSeconds = ka;
                var sessionId = session?["id"]?.ToString();
                if (!string.IsNullOrEmpty(sessionId)) Subscribe(sessionId!);
                break;
            case "session_reconnect":
                return (payload?["session"] as JsonObject)?["reconnect_url"]?.ToString();
            case "revocation":
                _log("EventSub subscription was revoked by Twitch (token/scope change?). Re-login may be needed.");
                break;
            case "notification":
                HandleNotification(metadata?["subscription_type"]?.ToString(), payload);
                break;
            // session_keepalive: nothing to do; the read succeeding is itself the liveness signal.
        }
        return null;
    }

    // Records a message id; returns false if it was already seen (a replay). Prunes ids older than
    // 10 minutes so the set stays bounded over a long stream.
    private bool RememberMessageId(string messageId)
    {
        var now = DateTime.UtcNow;
        if (_seenMessageIds.Count > 256)
        {
            var cutoff = now.AddMinutes(-10);
            foreach (var stale in _seenMessageIds.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                _seenMessageIds.Remove(stale);
        }
        if (_seenMessageIds.ContainsKey(messageId)) return false;
        _seenMessageIds[messageId] = now;
        return true;
    }

    private void Subscribe(string sessionId)
    {
        try
        {
            _helix.CreateEventSubSubscription(
                "channel.channel_points_custom_reward_redemption.add", "1",
                new JsonObject { ["broadcaster_user_id"] = _session.UserId },
                sessionId);
            _log($"Connected — listening for channel-point redemptions on @{_session.Login}.");
        }
        catch (Exception ex)
        {
            _log($"Failed to create the redemption subscription: {ex.Message}");
        }

        if (_onChat is not null)
        {
            try
            {
                _helix.CreateEventSubSubscription(
                    "channel.chat.message", "1",
                    new JsonObject { ["broadcaster_user_id"] = _session.UserId, ["user_id"] = _session.UserId },
                    sessionId);
                _log("Listening for chat commands too.");
            }
            catch (Exception ex)
            {
                _log($"Chat commands unavailable — re-login to grant chat scopes. ({ex.Message})");
            }
        }
    }

    private void HandleNotification(string? subscriptionType, JsonObject? payload)
    {
        if (payload?["event"] is not JsonObject ev) return;
        if (subscriptionType == "channel.chat.message")
        {
            var text = (ev["message"] as JsonObject)?["text"]?.ToString() ?? "";
            _onChat?.Invoke(new ChatMessage(
                ev["chatter_user_id"]?.ToString() ?? "",
                ev["chatter_user_name"]?.ToString() ?? ev["chatter_user_login"]?.ToString() ?? "",
                text));
            return;
        }
        var reward = ev["reward"] as JsonObject;
        _onRedemption(new RedemptionEvent(
            reward?["id"]?.ToString() ?? "",
            reward?["title"]?.ToString() ?? "",
            ev["id"]?.ToString() ?? "",
            ev["user_id"]?.ToString() ?? "",
            ev["user_name"]?.ToString() ?? ev["user_login"]?.ToString() ?? ""));
    }
}
