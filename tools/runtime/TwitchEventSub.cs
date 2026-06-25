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
    // Returns a reconnect URL when Twitch sends session_reconnect, otherwise null.
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
                result = await socket.ReceiveAsync(buffer, cancel);
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

    private string? HandleMessage(JsonObject message)
    {
        var type = message["metadata"]?["message_type"]?.ToString();
        var payload = message["payload"] as JsonObject;
        switch (type)
        {
            case "session_welcome":
                var sessionId = (payload?["session"] as JsonObject)?["id"]?.ToString();
                if (!string.IsNullOrEmpty(sessionId)) Subscribe(sessionId!);
                break;
            case "session_reconnect":
                return (payload?["session"] as JsonObject)?["reconnect_url"]?.ToString();
            case "revocation":
                _log("EventSub subscription was revoked by Twitch (token/scope change?). Re-login may be needed.");
                break;
            case "notification":
                HandleNotification(message["metadata"]?["subscription_type"]?.ToString(), payload);
                break;
            // session_keepalive: nothing to do; the socket staying open is the signal.
        }
        return null;
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
