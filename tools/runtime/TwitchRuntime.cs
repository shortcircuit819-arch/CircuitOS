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
                    redemption => HandleRedemption(redemption, rewardToProfile, service, helix, log), log);
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
        }
        catch (Exception ex)
        {
            log($"  -> error handling redemption: {ex.Message}");
        }
    }

    private static string ResultErrors(ServiceResult result)
        => result.Body?["errors"] is JsonArray errors ? string.Join("; ", errors.Select(e => e?.ToString())) : "";
}
