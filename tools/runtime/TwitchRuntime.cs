using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// The native Twitch redemption listener, shared by the running app (background task) and the
// --twitch-listen diagnostic (blocking). Ensures each live profile's channel-point reward exists,
// maps reward id -> profile, opens the EventSub WebSocket, and on each redemption runs the shared
// pull dispatch then fulfils (or cancels/refunds on failure). Hosting-free.
internal static class TwitchRuntime
{
    // Starts the listener on a background task. Returns null when Twitch isn't configured (no
    // credentials/login), so the app simply runs without it. The task ends when `cancel` fires.
    public static Task? TryStart(IDataStore store, CircuitService service, string dataRoot, Action<string> log, CancellationToken cancel)
    {
        var options = TwitchOptions.TryLoad(dataRoot);
        var tokens = TwitchTokens.TryLoad(dataRoot);
        if (options is null || tokens is null) return null;

        return Task.Run(async () =>
        {
            try
            {
                var session = new TwitchSession(options, tokens, dataRoot);
                var helix = new TwitchHelix(session);
                var rewardToProfile = BuildRewardMap(store, helix, log);
                if (rewardToProfile.Count == 0)
                {
                    log("Native Twitch idle: take a profile live (with a redemption name) to enable redemptions.");
                    return;
                }
                var eventSub = new TwitchEventSub(session, helix,
                    redemption => HandleRedemption(redemption, rewardToProfile, service, helix, log),
                    chat => HandleChat(chat, service, helix, log),
                    log);
                await eventSub.RunAsync(cancel);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log($"Native Twitch error: {ex.Message}"); }
        }, cancel);
    }

    // Idempotently ensures each live profile's reward and returns reward id -> profile id.
    public static Dictionary<string, string> BuildRewardMap(IDataStore store, TwitchHelix helix, Action<string> log)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in store.ListProfiles().Where(p => p.IsLive))
        {
            var data = store.ReadProfileData(profile.Id, DataKeys.Profile);
            var title = JsonUtil.String(data ?? new JsonObject(), "redemptionName");
            if (string.IsNullOrWhiteSpace(title)) continue;
            try
            {
                var reward = helix.EnsureReward(title, 100, "Redeem to pull an item with CircuitOS.");
                map[reward.Id] = profile.Id;
                log($"Reward '{reward.Title}' ({reward.Id}) -> profile '{profile.Id}'.");
            }
            catch (Exception ex)
            {
                log($"Could not set up the reward for profile '{profile.Id}': {ex.Message}");
            }
        }
        return map;
    }

    public static void HandleRedemption(RedemptionEvent redemption, IReadOnlyDictionary<string, string> rewardToProfile,
        CircuitService service, TwitchHelix helix, Action<string> log)
    {
        if (!rewardToProfile.TryGetValue(redemption.RewardId, out var profileId))
        {
            log($"(ignored redemption for unmanaged reward '{redemption.RewardTitle}')");
            return;
        }
        log($"Redemption: {redemption.UserName} -> '{redemption.RewardTitle}'  [profile {profileId}]");
        try
        {
            var result = service.DispatchRuntimeAction(new JsonObject
            {
                ["action"] = "redeem",
                ["profileId"] = profileId,
                ["viewerId"] = redemption.UserId,
                ["viewerName"] = redemption.UserName
            });
            var fulfilled = result.Status == 200;
            helix.UpdateRedemptionStatus(redemption.RewardId, redemption.RedemptionId, fulfilled);
            log(fulfilled
                ? "  -> FULFILLED (pull recorded, inventory saved)."
                : $"  -> CANCELED (refunded): {ResultErrors(result)}");

            // Announce the pull in chat (needs user:write:chat — same re-login as chat commands).
            if (fulfilled && result.Body?["messages"] is JsonArray lines)
            {
                foreach (var line in lines)
                {
                    var announcement = line?.ToString();
                    if (string.IsNullOrWhiteSpace(announcement)) continue;
                    try { helix.SendChatMessage(announcement!); }
                    catch (Exception ex) { log($"  (chat announcement skipped — re-login for chat scopes? {ex.Message})"); break; }
                }
            }

            // On cooldown (429) the points were refunded by the cancel above; tell the viewer in chat.
            if (!fulfilled && result.Status == 429 && result.Body?["errors"] is JsonArray cooldownErrors && cooldownErrors.Count > 0)
            {
                try
                {
                    var notice = cooldownErrors[0]?.ToString();
                    if (!string.IsNullOrWhiteSpace(notice)) helix.SendChatMessage(notice!);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            log($"  -> error handling redemption: {ex.Message}");
        }
    }

    // A chat message starting with '!' is treated as a command: resolves the live profile that owns
    // the word (via the shared dispatch) and sends each reply line back to chat. Non-commands and
    // words no live profile owns are silently ignored.
    public static void HandleChat(ChatMessage message, CircuitService service, TwitchHelix helix, Action<string> log)
    {
        var text = message.Text.TrimStart();
        if (text.Length < 2 || text[0] != '!') return;
        var parts = text[1..].Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        try
        {
            var result = service.DispatchRuntimeAction(new JsonObject
            {
                ["action"] = "command",
                ["command"] = parts[0],
                ["arg"] = parts.Length > 1 ? parts[1] : "",
                ["viewerId"] = message.UserId,
                ["viewerName"] = message.UserName
            });
            if (result.Status != 200) return;                       // not one of our commands → ignore
            if (result.Body?["messages"] is not JsonArray lines) return;
            foreach (var line in lines)
            {
                var reply = line?.ToString();
                if (!string.IsNullOrWhiteSpace(reply)) helix.SendChatMessage(reply!);
            }
        }
        catch (Exception ex)
        {
            log($"Chat command error: {ex.Message}");
        }
    }

    private static string ResultErrors(ServiceResult result)
        => result.Body?["errors"] is JsonArray errors ? string.Join("; ", errors.Select(e => e?.ToString())) : "";
}
