using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// The native Twitch redemption listener, shared by the running app (background task) and the
// --twitch-listen diagnostic (blocking). Ensures each live profile's channel-point reward exists,
// maps reward id -> profile, opens the EventSub WebSocket, and on each redemption runs the shared
// pull dispatch then fulfils (or cancels/refunds on failure). Hosting-free.
internal static class TwitchRuntime
{
    // Per-viewer chat-command cooldown. Chat commands are driven by chat volume (unbounded), and each
    // reply is a Helix send subject to Twitch's ~20-msg/30s limit — a busy chat spamming !inventory
    // would drop messages. We ignore a viewer's commands within this window. EventSub delivers chat
    // serially on one socket, so a plain dictionary is safe (no concurrent access).
    private static readonly Dictionary<string, DateTime> _lastChatCommand = new(StringComparer.Ordinal);
    private const int ChatCommandCooldownSeconds = 3;

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
                var rewardRoutes = BuildRewardRoutes(store, (title, cost, prompt) => helix.EnsureReward(title, cost, prompt), log);
                if (rewardRoutes.Count == 0)
                {
                    log("Native Twitch idle: take a profile live (with a redemption name) to enable redemptions.");
                    return;
                }
                var eventSub = new TwitchEventSub(session, helix,
                    redemption => HandleRedemption(redemption, rewardRoutes, service, helix, log),
                    chat => HandleChat(chat, service, helix, log),
                    log);
                await eventSub.RunAsync(cancel);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log($"Native Twitch error: {ex.Message}"); }
        }, cancel);
    }

    internal sealed record RewardRoute(string ProfileId, bool Manageable);

    // Idempotently ensures each live profile's reward and returns reward id -> profile id.
    public static Dictionary<string, string> BuildRewardMap(IDataStore store, TwitchHelix helix, Action<string> log) =>
        BuildRewardMap(store, (title, cost, prompt) => helix.EnsureReward(title, cost, prompt), log);

    internal static Dictionary<string, string> BuildRewardMap(IDataStore store, Func<string, int, string, CustomReward> ensureReward, Action<string> log) =>
        BuildRewardRoutes(store, ensureReward, log).ToDictionary(kv => kv.Key, kv => kv.Value.ProfileId, StringComparer.Ordinal);

    internal static Dictionary<string, RewardRoute> BuildRewardRoutes(IDataStore store, Func<string, int, string, CustomReward> ensureReward, Action<string> log)
    {
        var map = new Dictionary<string, RewardRoute>(StringComparer.Ordinal);
        foreach (var profile in store.ListProfiles().Where(p => p.IsLive))
        {
            try
            {
                if (TryGetStoredReward(store, profile.Id) is { } stored)
                {
                    if (!AddRewardMapping(map, stored, profile.Id, log)) continue;
                    log($"Reward '{stored.Title}' ({stored.Id}) -> profile '{profile.Id}' (stored mapping). ");
                    continue;
                }
                var reward = SyncRewardForProfile(store, profile.Id, ensureReward, log);
                AddRewardMapping(map, reward, profile.Id, log);
            }
            catch (Exception ex)
            {
                log($"Could not set up the reward for profile '{profile.Id}': {ex.Message}");
            }
        }
        return map;
    }

    public static CustomReward SyncRewardForProfile(IDataStore store, string profileId, TwitchHelix helix, Action<string> log) =>
        SyncRewardForProfile(store, profileId, (title, cost, prompt) => helix.EnsureReward(title, cost, prompt), log);

    internal static CustomReward SyncRewardForProfile(IDataStore store, string profileId, Func<string, int, string, CustomReward> ensureReward, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("A live profile is required before syncing a Twitch reward.");
        var profile = store.ListProfiles().FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
        if (profile is null) throw new InvalidDataException($"Profile '{profileId}' was not found.");
        if (!profile.IsLive) throw new InvalidDataException($"Profile '{profile.Name}' must be marked Live before syncing its Twitch reward.");

        var data = store.ReadProfileData(profile.Id, DataKeys.Profile) ?? new JsonObject();
        var title = JsonUtil.String(data, "redemptionName");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException($"Profile '{profile.Name}' needs a redemption name before syncing Twitch.");

        // Reward cost comes from the profile (redemptionCost); default 100 when unset/out of range.
        var configuredCost = JsonUtil.Long(data, "redemptionCost");
        var cost = configuredCost is >= 1 and <= 1_000_000 ? (int)configuredCost : 100;
        const string prompt = "Redeem to pull an item with CircuitOS.";
        var reward = ensureReward(title, cost, prompt);
        EnsureRewardIsNotMappedToAnotherLiveProfile(store, profile.Id, reward);
        PersistRewardMapping(store, profile.Id, reward, cost);
        log($"Reward '{reward.Title}' ({reward.Id}) -> profile '{profile.Id}'.");
        return reward;
    }

    public static CustomReward AttachRewardForProfile(IDataStore store, string profileId, CustomReward reward, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("Choose a live profile before attaching a Twitch reward.");
        if (string.IsNullOrWhiteSpace(reward.Id)) throw new InvalidDataException("Choose a Twitch reward before attaching it.");
        var profile = store.ListProfiles().FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
        if (profile is null) throw new InvalidDataException($"Profile '{profileId}' was not found.");
        if (!profile.IsLive) throw new InvalidDataException($"Profile '{profile.Name}' must be marked Live before attaching its Twitch reward.");

        EnsureRewardIsNotMappedToAnotherLiveProfile(store, profile.Id, reward);
        PersistRewardMapping(store, profile.Id, reward, reward.Cost);
        log($"Attached Twitch reward '{reward.Title}' ({reward.Id}) -> profile '{profile.Id}'.");
        return reward;
    }
    public static CustomReward UpdateRewardForProfile(IDataStore store, string profileId, string title, int cost, TwitchHelix helix, Action<string> log) =>
        UpdateRewardForProfile(store, profileId, title, cost, (rewardId, nextTitle, nextCost, prompt) => helix.UpdateReward(rewardId, nextTitle, nextCost, prompt), log);

    internal static CustomReward UpdateRewardForProfile(IDataStore store, string profileId, string title, int cost, Func<string, string, int, string, CustomReward> updateReward, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("Choose a profile before editing a Twitch reward.");
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("Twitch reward title cannot be empty.");
        if (cost < 1) throw new InvalidDataException("Twitch reward cost must be at least 1 point.");
        var profile = store.ListProfiles().FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
        if (profile is null) throw new InvalidDataException($"Profile '{profileId}' was not found.");

        var stored = TryGetStoredReward(store, profile.Id)
            ?? throw new InvalidDataException($"Profile '{profile.Name}' does not have a synced Twitch reward to edit.");
        if (!stored.Manageable) throw new InvalidDataException($"Profile '{profile.Name}' is attached to a Twitch reward CircuitOS cannot edit. Edit it in Twitch or sync a CircuitOS-managed reward.");

        const string prompt = "Redeem to pull an item with CircuitOS.";
        var updated = updateReward(stored.Id, title, cost, prompt) with { Manageable = true };
        PersistRewardMapping(store, profile.Id, updated, cost);
        var profileData = store.ReadProfileData(profile.Id, DataKeys.Profile) ?? new JsonObject();
        profileData["redemptionName"] = updated.Title;
        store.WriteProfileData(profile.Id, DataKeys.Profile, profileData);
        log($"Updated Twitch reward '{updated.Title}' ({updated.Id}) for profile '{profile.Id}'.");
        return updated;
    }

    private static bool AddRewardMapping(Dictionary<string, RewardRoute> map, CustomReward reward, string profileId, Action<string> log)
    {
        if (map.TryGetValue(reward.Id, out var existing) && !string.Equals(existing.ProfileId, profileId, StringComparison.Ordinal))
        {
            log($"Reward '{reward.Title}' ({reward.Id}) is already mapped to profile '{existing.ProfileId}'; skipping duplicate mapping for '{profileId}'.");
            return false;
        }
        map[reward.Id] = new RewardRoute(profileId, reward.Manageable);
        return true;
    }

    private static void EnsureRewardIsNotMappedToAnotherLiveProfile(IDataStore store, string selfProfileId, CustomReward reward)
    {
        foreach (var other in store.ListProfiles().Where(p => p.IsLive && !string.Equals(p.Id, selfProfileId, StringComparison.Ordinal)))
        {
            var stored = TryGetStoredReward(store, other.Id);
            if (stored is null) continue;
            if (string.Equals(stored.Id, reward.Id, StringComparison.Ordinal))
                throw new InvalidDataException($"Twitch reward '{reward.Title}' is already attached to live profile '{other.Name}'. Choose a different reward before syncing.");
        }
    }
    private static CustomReward? TryGetStoredReward(IDataStore store, string profileId)
    {
        var state = store.ReadProfileData(profileId, DataKeys.TwitchRewards);
        var rewards = JsonUtil.Object(state, "rewards");
        var channelPoints = JsonUtil.Object(rewards, "channelPoints");
        var rewardId = JsonUtil.String(channelPoints, "rewardId");
        if (string.IsNullOrWhiteSpace(rewardId)) return null;
        var manageable = JsonUtil.Bool(channelPoints, "manageable", true);
        return new CustomReward(rewardId, JsonUtil.String(channelPoints, "title"), (int)JsonUtil.Long(channelPoints, "cost"), manageable);
    }

    private static void PersistRewardMapping(IDataStore store, string profileId, CustomReward reward, int fallbackCost)
    {
        var state = store.ReadProfileData(profileId, DataKeys.TwitchRewards) ?? new JsonObject();
        state["schemaVersion"] = 1;
        state["updatedAtUtc"] = DateTime.UtcNow.ToString("O");
        var rewards = JsonUtil.Object(state, "rewards") ?? new JsonObject();
        state["rewards"] = rewards;
        rewards["channelPoints"] = new JsonObject
        {
            ["profileId"] = profileId,
            ["rewardId"] = reward.Id,
            ["title"] = reward.Title,
            ["cost"] = reward.Cost > 0 ? reward.Cost : fallbackCost,
            ["manageable"] = reward.Manageable,
            ["syncedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
        store.WriteProfileData(profileId, DataKeys.TwitchRewards, state);
    }
    public static CustomReward DeleteRewardForProfile(IDataStore store, string profileId, TwitchHelix helix, Action<string> log) =>
        DeleteRewardForProfile(store, profileId, rewardId => helix.DeleteReward(rewardId), log);

    internal static CustomReward DeleteRewardForProfile(IDataStore store, string profileId, Action<string> deleteReward, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("Choose a profile before deleting a Twitch reward.");
        var profile = store.ListProfiles().FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));
        if (profile is null) throw new InvalidDataException($"Profile '{profileId}' was not found.");

        var state = store.ReadProfileData(profile.Id, DataKeys.TwitchRewards)
            ?? throw new InvalidDataException($"Profile '{profile.Name}' does not have a synced Twitch reward to delete.");
        var rewards = JsonUtil.Object(state, "rewards") ?? new JsonObject();
        var channelPoints = JsonUtil.Object(rewards, "channelPoints")
            ?? throw new InvalidDataException($"Profile '{profile.Name}' does not have a synced Twitch reward to delete.");
        var rewardId = JsonUtil.String(channelPoints, "rewardId");
        if (string.IsNullOrWhiteSpace(rewardId)) throw new InvalidDataException($"Profile '{profile.Name}' does not have a synced Twitch reward id.");

        if (!JsonUtil.Bool(channelPoints, "manageable", true)) throw new InvalidDataException($"Profile '{profile.Name}' is attached to a Twitch reward CircuitOS cannot delete. Remove it in Twitch or detach it by syncing another reward.");
        var deleted = new CustomReward(rewardId, JsonUtil.String(channelPoints, "title"), JsonUtil.Long(channelPoints, "cost") is var c ? (int)c : 0, Manageable: true);
        deleteReward(rewardId);
        rewards.Remove("channelPoints");
        state["updatedAtUtc"] = DateTime.UtcNow.ToString("O");
        store.WriteProfileData(profile.Id, DataKeys.TwitchRewards, state);
        log($"Deleted Twitch reward '{deleted.Title}' ({deleted.Id}) for profile '{profile.Id}'.");
        return deleted;
    }

    public static void HandleRedemption(RedemptionEvent redemption, IReadOnlyDictionary<string, RewardRoute> rewardRoutes,
        CircuitService service, TwitchHelix helix, Action<string> log)
    {
        if (!rewardRoutes.TryGetValue(redemption.RewardId, out var route))
        {
            log($"(ignored redemption for unmanaged reward '{redemption.RewardTitle}')");
            return;
        }
        var profileId = route.ProfileId;
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
            if (route.Manageable)
            {
                helix.UpdateRedemptionStatus(redemption.RewardId, redemption.RedemptionId, fulfilled);
                log(fulfilled
                    ? "  -> FULFILLED (pull recorded, inventory saved)."
                    : $"  -> CANCELED (refunded): {ResultErrors(result)}");
            }
            else
            {
                log(fulfilled
                    ? "  -> RECORDED (attach-only reward; Twitch fulfillment skipped)."
                    : $"  -> FAILED (attach-only reward; Twitch refund unavailable): {ResultErrors(result)}");
            }

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

        // Throttle per viewer so chat spam can't blow Twitch's chat send rate limit. Checked before
        // we send, recorded only once we actually reply — random non-commands don't burn the cooldown.
        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(message.UserId)
            && _lastChatCommand.TryGetValue(message.UserId, out var last)
            && (now - last).TotalSeconds < ChatCommandCooldownSeconds)
            return;

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
            RecordChatCommand(message.UserId, now);
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

    private static void RecordChatCommand(string userId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        _lastChatCommand[userId] = now;
        if (_lastChatCommand.Count > 512)
        {
            var cutoff = now.AddMinutes(-5);
            foreach (var stale in _lastChatCommand.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                _lastChatCommand.Remove(stale);
        }
    }

    private static string ResultErrors(ServiceResult result)
        => result.Body?["errors"] is JsonArray errors ? string.Join("; ", errors.Select(e => e?.ToString())) : "";
}





