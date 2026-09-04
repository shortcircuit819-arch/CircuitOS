using System.Net.WebSockets;
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
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connect;
    private readonly TimeSpan _retryDelay;

    // Twitch may replay a notification; the spec requires dedup by metadata.message_id. We keep the
    // recently-seen ids (with arrival time) and drop repeats so a replay can't double-process a pull.
    private readonly Dictionary<string, DateTime> _seenMessageIds = new(StringComparer.Ordinal);
    // Twitch closes the socket if it sends nothing for keepalive_timeout_seconds. If the connection
    // half-dies (no FIN), ReceiveAsync would block forever; we time out reads at keepalive + grace and
    // force a reconnect. Default to Twitch's 10s until session_welcome tells us the real value.
    private int _keepaliveSeconds = 10;
    private const int KeepaliveGraceSeconds = 5;

    public TwitchEventSub(TwitchSession session, TwitchHelix helix, Action<RedemptionEvent> onRedemption, Action<ChatMessage>? onChat, Action<string> log,
        Func<Uri, CancellationToken, Task<WebSocket>>? connect = null, TimeSpan? retryDelay = null)
    {
        _session = session;
        _helix = helix;
        _onRedemption = onRedemption;
        _onChat = onChat;
        _log = log;
        _connect = connect ?? ConnectSocketAsync;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
    }

    private static async Task<WebSocket> ConnectSocketAsync(Uri uri, CancellationToken cancel)
    {
        var socket = new ClientWebSocket();
        try { await socket.ConnectAsync(uri, cancel); return socket; }
        catch { socket.Dispose(); throw; }
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
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"EventSub connection dropped: {ex.Message}. Reconnecting in 3s…");
                try { await Task.Delay(_retryDelay, cancel); } catch (OperationCanceledException) { break; }
                url = DefaultUrl;
            }
        }
    }

    // A requested handover keeps receiving on the old socket until the replacement welcome.
    // Both readers are network-only; notification callbacks remain serialized on this loop.
    private async Task<string?> ConnectAndListenAsync(string url, CancellationToken cancel)
    {
        var socket = await _connect(new Uri(url), cancel);
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(socket, cancel);
                if (message is null) return null;
                var reconnectUrl = HandleMessage(message);
                if (reconnectUrl is null) continue;

                using var oldReadCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                using var replacementCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                replacementCancel.CancelAfter(TimeSpan.FromSeconds(30));
                var replacement = ConnectReplacementAsync(reconnectUrl, replacementCancel.Token);
                Task<JsonObject?>? oldRead = null;
                var oldClosed = false;
                try
                {
                    while (!replacement.IsCompleted)
                    {
                        oldRead = ReadMessageAsync(socket, oldReadCancel.Token);
                        await Task.WhenAny(oldRead, replacement);
                        if (replacement.IsCompleted) break;
                        var oldMessage = await oldRead;
                        oldRead = null;
                        if (oldMessage is null) { oldClosed = true; break; }
                        HandleMessage(oldMessage);
                    }
                    var (next, welcome) = await replacement;
                    // A fast welcome does not mean the old socket's receive buffer is empty.
                    // Complete its close handshake while draining notifications already in flight.
                    // Canceling ReceiveAsync here would abort the socket and discard those pulls.
                    oldReadCancel.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        if (socket.State is not (WebSocketState.Closed or WebSocketState.Aborted))
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Reconnect complete", oldReadCancel.Token);
                        while (!oldClosed)
                        {
                            var oldMessage = await (oldRead ?? ReadMessageAsync(socket, oldReadCancel.Token));
                            oldRead = null;
                            if (oldMessage is null) break;
                            HandleMessage(oldMessage);
                        }
                    }
                    catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                    {
                        _log("Old EventSub connection did not finish its close handshake; replacement is ready.");
                    }
                    socket.Dispose();
                    socket = next;
                    HandleMessage(welcome, subscribe: false); // subscriptions were transferred by Twitch
                }
                catch
                {
                    // Observe/clean up an in-flight replacement if the old connection failed first.
                    oldReadCancel.Cancel();
                    if (oldRead is not null) { try { await oldRead; } catch { } }
                    replacementCancel.Cancel();
                    try { var pending = await replacement; pending.Socket.Dispose(); } catch { }
                    throw;
                }
            }
            return null;
        }
        finally { socket.Dispose(); }
    }

    private async Task<(WebSocket Socket, JsonObject Welcome)> ConnectReplacementAsync(string url, CancellationToken cancel)
    {
        var socket = await _connect(new Uri(url), cancel);
        try
        {
            var welcome = await ReadMessageAsync(socket, cancel);
            if (welcome?["metadata"]?["message_type"]?.ToString() != "session_welcome")
                throw new InvalidDataException("Twitch replacement connection did not send a welcome message.");
            return (socket, welcome);
        }
        catch { socket.Dispose(); throw; }
    }

    private async Task<JsonObject?> ReadMessageAsync(WebSocket socket, CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ReceiveWithKeepaliveAsync(socket, buffer, cancel);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        // Decode only after assembling the message: a UTF-8 character may cross frame boundaries.
        return JsonNode.Parse(message.ToArray()) as JsonObject
            ?? throw new InvalidDataException("Twitch WebSocket message must be a JSON object.");
    }
    // A single ReceiveAsync bounded by the keepalive window. If nothing (event OR keepalive) arrives
    // in time, the connection is presumed dead: abort the socket and throw so RunAsync reconnects.
    private async Task<WebSocketReceiveResult> ReceiveWithKeepaliveAsync(WebSocket socket, byte[] buffer, CancellationToken cancel)
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

    private string? HandleMessage(JsonObject message, bool subscribe = true)
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
                if (subscribe && !string.IsNullOrEmpty(sessionId)) Subscribe(sessionId!);
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
            throw; // mandatory: reconnect/retry instead of remaining alive with chat only
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
