using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed record RuntimeOptions(string DataPath, string UiPath, string OverlayPath, int Port, bool Headless)
{
    public static RuntimeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--headless" or "--no-browser" or "--check-appwrite" or "--appwrite-roundtrip" or "--push-to-appwrite" or "--appwrite-profiles" or "--appwrite-backups" or "--twitch-login" or "--twitch-reward" or "--twitch-listen" or "--cloud") { flags.Add(arg); continue; }
            if (arg.StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length) values[arg] = args[++index];
        }

        var basePath = Path.GetFullPath(AppContext.BaseDirectory);
        var uiPath = Path.GetFullPath(values.GetValueOrDefault("--ui", FindFolderContaining(
            "index.html",
            Path.Combine(basePath, "App"),
            basePath,
            Path.Combine(basePath, "..")) ?? Path.Combine(basePath, "App")));
        // An installed (Velopack) build ships no Data beside the exe: its writable data lives in a stable
        // per-user folder that SURVIVES updates, because the versioned program folder is replaced on every
        // update. Portable/ZIP still finds Data next to the exe; dev finds the repo data/ via candidates.
        var installedDataDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CircuitOS", "Data");
        var dataPath = Path.GetFullPath(values.GetValueOrDefault("--data", FindFolderContaining(
            "components.json",
            Path.Combine(basePath, "Data"),
            Path.Combine(basePath, "..", "Data"),
            Path.Combine(uiPath, "..", "Data"),
            Path.Combine(uiPath, "..", "..", "data")) ?? installedDataDefault));
        var overlayPath = Path.GetFullPath(values.GetValueOrDefault("--overlay", FindFolderContaining(
            "overlay.js",
            Path.Combine(basePath, "Overlay"),
            Path.Combine(basePath, "..", "Overlay"),
            Path.Combine(uiPath, "..", "Overlay"),
            Path.Combine(uiPath, "..", "..", "overlays", "lower-quarter"),
            Path.Combine(dataPath, "overlay")) ?? Path.Combine(dataPath, "overlay")));
        var port = int.TryParse(values.GetValueOrDefault("--port", "8787"), out var parsedPort) && parsedPort is > 0 and < 65536
            ? parsedPort
            : 8787;
        return new RuntimeOptions(dataPath, uiPath, overlayPath, port,
            flags.Contains("--headless") || flags.Contains("--no-browser"));
    }

    private static string? FindFolderContaining(string requiredFile, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, requiredFile))) return fullPath;
        }
        return null;
    }
}

internal static class Program
{
    // Session info surfaced in /api/health so the admin panel can show the data
    // backend (local vs cloud) and the logged-in Twitch identity.
    private static string _sessionMode = "local";
    private static TwitchTokens? _sessionTwitch;
    // Optional dedicated bot chat account (second login) — replies post as the bot when connected.
    private static TwitchTokens? _sessionTwitchBot;
    private static string _dataRoot = "";
    private static bool _headless;
    // Set when cloud mode was requested but couldn't start (fell back to local); shown in Settings.
    private static string? _cloudError;
    // In-flight inline (admin-panel) device logins: loginId -> the device code being polled. Lets the
    // UI show the code and poll /api/twitch/login/poll without blocking a request for the whole flow.
    // Bot marks a bot-account login (bot scopes, tokens land in TwitchTokens.BotFileName).
    private static readonly Dictionary<string, (TwitchOptions Opts, string DeviceCode, DateTimeOffset ExpiresAt, bool Bot)> _pendingDeviceLogins = new();
    private static readonly object _twitchRuntimeLock = new();
    private static CancellationTokenSource? _twitchRuntimeCancellation;
    private static Task? _twitchRuntimeTask;

    [STAThread]
    public static int Main(string[] args)
    {
        // MUST be the first statement: Velopack invokes the app with hook args during install/update/
        // uninstall and may exit the process early. Anything before this (arg parsing, windows, HTTP)
        // would break those hooks. No-op on a normal launch. See docs/updater-velopack-plan.md.
        Velopack.VelopackApp.Build().Run();

        var options = RuntimeOptions.Parse(args);

        // 0.7 diagnostic: verify the Appwrite connection + collection, then exit.
        // Does not start the app. Reads appwrite.local.json from the data root.
        if (args.Contains("--check-appwrite"))
            return CheckAppwrite(options.DataPath, options.Headless);

        // 0.7 diagnostic: round-trip a throwaway value through AppwriteDataStore
        // (write → read → verify → delete) to prove the cloud data layer works.
        if (args.Contains("--appwrite-roundtrip"))
            return RoundtripAppwrite(options.DataPath, options.Headless);

        // 0.7 migration: copy the local active profile's data files into Appwrite
        // (one row per data key), then read them back to verify. Local data untouched.
        if (args.Contains("--push-to-appwrite"))
            return PushToAppwrite(options.DataPath, options.Headless);

        // 0.7 Phase 2b: exercise cloud profile management (list/create/rename/delete)
        // against a throwaway test profile, cleaned up after.
        if (args.Contains("--appwrite-profiles"))
            return ProfilesAppwrite(options.DataPath, options.Headless);

        // 0.7 Phase 2b: verify the cloud backup recovery point (write, overwrite,
        // confirm the prior version is captured), against a throwaway key.
        if (args.Contains("--appwrite-backups"))
            return BackupsAppwrite(options.DataPath, options.Headless);

        // 0.7 Phase 3: Twitch OAuth login. Opens the browser, captures the redirect,
        // caches tokens + the user id (which becomes the cloud tenant).
        if (args.Contains("--twitch-login"))
            return TwitchLogin(options.DataPath, options.Headless);

        // 0.7 Phase 4 (native Twitch): create/update the channel-point reward via Helix and exit.
        if (args.Contains("--twitch-reward"))
            return TwitchReward(options.DataPath, options.Headless);

        // 0.7 Phase 4 (native Twitch): open the EventSub WebSocket and process live channel-point
        // redemptions until Ctrl+C. The zero-config native path. Run from a terminal.
        if (args.Contains("--twitch-listen"))
            return TwitchListen(options.DataPath);

        // First run of an installed build: the per-user data folder is empty, so seed it from the
        // StarterData bundled beside the exe (before the store is constructed, so its migration picks the
        // starter catalog up). No-op for dev/portable (they already resolved to a populated Data folder).
        SeedInstalledDataIfEmpty(options.DataPath, AppContext.BaseDirectory);

        // The local file store is always created: it provides the active profile id and the
        // local folder used to serve the OBS overlay (overlay statics/state stay local even
        // when game data lives in the cloud). With --cloud, game data is read/written through
        // AppwriteDataStore instead; the local store keeps serving the overlay path.
        var localStore = new LocalFileDataStore(options.DataPath);
        IDataStore store = localStore;
        // Cloud is requested by the --cloud flag OR the persisted Settings choice. If it can't be
        // brought up (no/invalid Appwrite config, or unreachable), fall back to local so the app still
        // starts — the reason is surfaced in /api/health so the Settings page can explain it.
        var wantCloud = args.Contains("--cloud") || AppSettings.CloudEnabled(options.DataPath);
        if (wantCloud)
        {
            try
            {
                var opts = AppwriteOptions.TryLoad(options.DataPath)
                    ?? throw new InvalidOperationException("Cloud mode is on but no Appwrite connection is configured. Add it under Settings.");
                var tenant = ResolveTenant(options.DataPath);
                if (!string.Equals(tenant, "local-dev", StringComparison.Ordinal))
                {
                    try { AppwriteDataStore.MigrateRowsToTenant(opts, "local-dev", tenant); }
                    catch (Exception ex) { Console.Error.WriteLine($"Tenant migration warning: {ex.Message}"); }
                }
                var cloudStore = new AppwriteDataStore(opts, tenant, localStore.ActiveProfileId);
                _ = cloudStore.Exists(DataKeys.Catalog); // connectivity probe — throws if unreachable
                store = cloudStore;
                _sessionMode = "cloud";
            }
            catch (Exception ex)
            {
                _cloudError = ex.Message;
                Console.Error.WriteLine($"Cloud mode unavailable — starting in local mode: {ex.Message}");
                store = localStore;
                _sessionMode = "local";
            }
        }
        // Pass the local store explicitly for overlay output: in cloud mode `store` is Appwrite and
        // can't write the local overlay-state.json that OBS reads, but the desktop host always has a
        // local store. This keeps native pulls driving the overlay in both modes.
        var service = new CircuitService(store, localStore);
        var overlayDataPath = localStore.DataPath;
        _sessionTwitch = TwitchTokens.TryLoad(options.DataPath);
        _sessionTwitchBot = TwitchTokens.TryLoad(options.DataPath, TwitchTokens.BotFileName);
        _dataRoot = options.DataPath;
        _headless = options.Headless;

        PublishOverlayStaticsForLiveProfiles(options.OverlayPath, overlayDataPath, store);

        if (!store.Exists(DataKeys.Catalog))
        {
            var where = store is ILocalDataStore local ? Path.Combine(local.DataPath, "components.json") : "the Appwrite profile_data table";
            if (options.Headless)
                Console.Error.WriteLine($"Catalog (components.json) was not found in: {where}");
            if (!options.Headless)
                MessageBox.Show($"Catalog was not found in:\n\n{where}", "CircuitOS",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            var port = ResolvePort(options.Port);
            if (options.Headless)
                Console.WriteLine($"Listening on http://127.0.0.1:{port}/");
            using var listener = new HttpListener();
            var url = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(url);
            listener.Start();

            using var cancellation = new CancellationTokenSource();
            var serverTask = RunServerAsync(listener, service, options.UiPath, options.OverlayPath, overlayDataPath, cancellation.Token);

            // Native Twitch: if logged in, listen for channel-point redemptions in the background
            // (no-op when Twitch isn't configured). Cancelled when the app exits.
            RefreshNativeTwitch(service, cancellation.Token);

            if (options.Headless)
            {
                serverTask.GetAwaiter().GetResult();
                return 0;
            }

            ApplicationConfiguration.Initialize();
            using var window = new CircuitWindow(url);
            Application.Run(window);

            StopNativeTwitch();
            cancellation.Cancel();
            listener.Stop();
            try { serverTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) when (cancellation.IsCancellationRequested) { }
            return 0;
        }
        catch (Exception exception)
        {
            if (options.Headless)
                Console.Error.WriteLine(exception);
            if (!options.Headless)
                MessageBox.Show(exception.Message, "CircuitOS could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ResolvePort(int preferredPort)
    {
        var candidatePort = preferredPort > 0 ? preferredPort : 8787;
        for (var port = candidatePort; port <= 65535; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException) when (port < 65535)
            {
                // Try the next port if the preferred one is already in use.
            }
        }

        throw new InvalidOperationException($"Unable to find a free loopback port from {candidatePort} to 65535.");
    }

    private static void RefreshNativeTwitch(CircuitService service, CancellationToken appCancel)
    {
        lock (_twitchRuntimeLock)
        {
            _twitchRuntimeCancellation?.Cancel();
            _twitchRuntimeCancellation?.Dispose();
            _twitchRuntimeCancellation = null;
            _twitchRuntimeTask = null;
            if (appCancel.IsCancellationRequested) return;

            var linked = CancellationTokenSource.CreateLinkedTokenSource(appCancel);
            var task = TwitchRuntime.TryStart(service.Store, service, _dataRoot, Console.WriteLine, linked.Token);
            if (task is null)
            {
                linked.Dispose();
                return;
            }
            _twitchRuntimeCancellation = linked;
            _twitchRuntimeTask = task;
        }
    }

    private static void StopNativeTwitch()
    {
        lock (_twitchRuntimeLock)
        {
            _twitchRuntimeCancellation?.Cancel();
            _twitchRuntimeCancellation?.Dispose();
            _twitchRuntimeCancellation = null;
            _twitchRuntimeTask = null;
        }
    }
    private static async Task RunServerAsync(
        HttpListener listener,
        CircuitService service,
        string uiPath,
        string overlayPath,
        string overlayDataPath,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch when (cancellationToken.IsCancellationRequested) { break; }
            _ = Task.Run(() => HandleRequestAsync(context, service, uiPath, overlayPath, overlayDataPath, cancellationToken), cancellationToken);
        }
    }

    // overlayDataPath is the local folder where the OBS overlay statics + state live; it
    // stays local even when game data is served from the cloud (--cloud).
    private static async Task HandleRequestAsync(HttpListenerContext context, CircuitService service, string uiPath, string overlayPath, string overlayDataPath, CancellationToken cancel)
    {
        try
        {
            var request = context.Request;
            // Defense-in-depth against DNS-rebinding: the server is loopback-only, but a malicious page
            // can rebind its hostname to 127.0.0.1 and reach it with a foreign Host header. Only accept
            // requests addressed to localhost/loopback so a rebound origin can't drive the local API.
            if (!IsAllowedHost(request))
            {
                await SendJsonAsync(context, 403, new { ok = false, errors = new[] { "Forbidden host." } });
                return;
            }
            // Defense against drive-by CSRF: a malicious webpage can fire "simple" cross-origin POSTs at
            // 127.0.0.1 without a CORS preflight — the response stays opaque to it, but the side effect
            // (overwrite profile, restore backup, drive Twitch) would still run. Browsers attach an Origin
            // header to those requests, so reject any Origin that isn't loopback. Requests with no Origin
            // (the WebView2 shell, curl, same-origin GETs) pass; the admin's own POSTs carry a loopback
            // Origin and pass.
            if (!IsAllowedOrigin(request))
            {
                await SendJsonAsync(context, 403, new { ok = false, errors = new[] { "Forbidden origin." } });
                return;
            }
            var path = request.Url?.AbsolutePath ?? "/";
            if (request.HttpMethod == "GET" && path == "/api/health")
                await SendJsonAsync(context, 200, new
                {
                    ok = true,
                    dataPath = overlayDataPath,
                    overlayFilePath = Path.Combine(overlayDataPath, "overlay", "index.html"),
                    profilesRoot = Path.Combine(_dataRoot, "profiles"),
                    runtime = ".NET",
                    version = "0.9.1",
                    mode = _sessionMode,
                    cloudError = _cloudError,
                    twitch = _sessionTwitch is null ? null : new { login = _sessionTwitch.Login, displayName = _sessionTwitch.DisplayName, userId = _sessionTwitch.UserId, expiresAt = _sessionTwitch.ExpiresAt },
                    twitchBot = _sessionTwitchBot is null ? null : new { login = _sessionTwitchBot.Login, displayName = _sessionTwitchBot.DisplayName, userId = _sessionTwitchBot.UserId }
                });
            else if (request.HttpMethod == "POST" && path == "/api/twitch/logout")
            {
                try { var tokenFile = Path.Combine(_dataRoot, TwitchTokens.FileName); if (File.Exists(tokenFile)) File.Delete(tokenFile); } catch { }
                _sessionTwitch = null;
                StopNativeTwitch();
                await SendJsonAsync(context, 200, new { ok = true });
            }
            else if (request.HttpMethod == "POST" && path == "/api/twitch/login")
            {
                // Runs the interactive OAuth flow and blocks until the user authorizes (or it times
                // out). With a bundled client id and no secret this is the zero-config Device Code
                // Flow: a desktop dialog shows the code + opens twitch.tv/activate. With a secret
                // present (self-host) it's the legacy loopback authorization-code flow.
                try
                {
                    var twitchOptions = TwitchOptions.Resolve(_dataRoot);
                    var tokens = twitchOptions.HasSecret
                        ? TwitchAuth.Login(twitchOptions, _dataRoot)
                        : TwitchAuth.LoginDeviceFlow(twitchOptions, _dataRoot, ShowDeviceCodePrompt, cancel);
                    _sessionTwitch = tokens;
                    RefreshNativeTwitch(service, cancel);
                    await SendJsonAsync(context, 200, new { ok = true, login = tokens.Login, displayName = tokens.DisplayName, userId = tokens.UserId });
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(context, 400, new { ok = false, error = ex.Message });
                }
            }
            else if (request.HttpMethod == "GET" && path == "/api/settings")
                await SendJsonAsync(context, 200, GetSettings());
            else if (request.HttpMethod == "POST" && path == "/api/settings/appwrite")
                await SendResultAsync(context, SaveAppwriteSettings(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/settings/appwrite/test")
                await SendResultAsync(context, TestAppwriteSettings());
            else if (request.HttpMethod == "POST" && path == "/api/settings/mode")
                await SendResultAsync(context, SetDataBackend(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/settings/open-folder")
                await SendResultAsync(context, OpenDataFolder());
            else if (request.HttpMethod == "POST" && path == "/api/settings/backup-retention")
                await SendResultAsync(context, SetBackupRetention(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/updates/check")
            {
                var status = await UpdateService.CheckAsync();
                await SendJsonAsync(context, 200, new { ok = true, managed = status.Managed, available = status.Available, latestVersion = status.LatestVersion, currentVersion = status.CurrentVersion, error = status.Error });
            }
            else if (request.HttpMethod == "POST" && path == "/api/updates/apply")
            {
                // The preview/headless host must never self-update (it's a dev/test process, not an install).
                if (_headless)
                    await SendJsonAsync(context, 200, new { ok = false, error = "Updates are disabled in this mode." });
                else
                {
                    var status = await UpdateService.ApplyAsync();
                    // On success the process is replaced and restarted, so control never returns here; a
                    // returned status means it declined (not managed / up to date) or hit an error.
                    await SendJsonAsync(context, 200, new { ok = status.Error is null, available = status.Available, latestVersion = status.LatestVersion, error = status.Error });
                }
            }
            else if (request.HttpMethod == "POST" && path == "/api/twitch/login/start")
                await SendResultAsync(context, StartDeviceLogin());
            else if (request.HttpMethod == "POST" && path == "/api/twitch/login/poll")
                await SendResultAsync(context, PollDeviceLogin(service, await ReadBodyAsync(request), cancel));
            else if (request.HttpMethod == "POST" && path == "/api/twitch/bot/login/start")
                await SendResultAsync(context, StartDeviceLogin(bot: true));
            else if (request.HttpMethod == "POST" && path == "/api/twitch/bot/logout")
            {
                // Disconnect the bot chat account: replies fall back to posting as the broadcaster.
                try { var botFile = Path.Combine(_dataRoot, TwitchTokens.BotFileName); if (File.Exists(botFile)) File.Delete(botFile); } catch { }
                _sessionTwitchBot = null;
                RefreshNativeTwitch(service, cancel);
                await SendJsonAsync(context, 200, new { ok = true });
            }
            else if (request.HttpMethod == "GET" && path == "/api/twitch/rewards")
                await SendResultAsync(context, ListTwitchRewards());
            else if (request.HttpMethod == "POST" && path == "/api/twitch/reward-sync")
            {
                var result = SyncTwitchReward(service, await ReadBodyAsync(request));
                await SendResultAsync(context, result);
                if (result.Status == 200) RefreshNativeTwitch(service, cancel);
            }
            else if (request.HttpMethod == "POST" && path == "/api/twitch/reward-delete")
            {
                var result = DeleteTwitchReward(service, await ReadBodyAsync(request));
                await SendResultAsync(context, result);
                if (result.Status == 200) RefreshNativeTwitch(service, cancel);
            }
            else if (request.HttpMethod == "POST" && path == "/api/twitch/reward-update")
            {
                var result = UpdateTwitchReward(service, await ReadBodyAsync(request));
                await SendResultAsync(context, result);
                if (result.Status == 200) RefreshNativeTwitch(service, cancel);
            }
            else if (request.HttpMethod == "GET" && path == "/api/config")
                await SendJsonAsync(context, 200, service.GetConfiguration());
            else if (request.HttpMethod == "GET" && path == "/api/profile")
                await SendJsonAsync(context, 200, service.GetSystemProfile());
            else if (request.HttpMethod == "GET" && path == "/api/overlay-config")
                await SendJsonAsync(context, 200, service.GetOverlayConfig());
            else if (request.HttpMethod == "GET" && path == "/api/analytics")
                await SendJsonAsync(context, 200, service.GetInventoryAnalytics());
            else if (request.HttpMethod == "GET" && path == "/api/roles")
                await SendJsonAsync(context, 200, service.GetDiscordRoleAwards());
            else if (request.HttpMethod == "GET" && path == "/api/backups")
                await SendJsonAsync(context, 200, service.GetBackupCenter());
            else if (request.HttpMethod == "GET" && path == "/api/profiles")
                await SendJsonAsync(context, 200, service.GetProfiles());
            else if (request.HttpMethod == "GET" && path == "/api/modules/export")
                await SendResultAsync(context, service.ExportModule());
            else if (request.HttpMethod == "POST" && path == "/api/modules/import")
                await SendResultAsync(context, service.ImportModule(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/collection-pack/export")
            {
                var body = await ReadBodyAsync(request);
                await SendResultAsync(context, service.ExportCollectionPack(JsonUtil.String(body, "collectionKey")));
            }
            else if (request.HttpMethod == "POST" && path == "/api/collection-pack/import")
            {
                var body = await ReadBodyAsync(request);
                await SendResultAsync(context, service.ImportCollectionPack(JsonUtil.Object(body, "pack") ?? new JsonObject(), JsonUtil.String(body, "name")));
            }
            else if (request.HttpMethod == "POST" && path == "/api/profile")
                await SendResultAsync(context, service.SaveSystemProfile(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/overlay-config")
            {
                var overlayConfigResult = service.SaveOverlayConfig(await ReadBodyAsync(request));
                await SendResultAsync(context, overlayConfigResult);
                if (overlayConfigResult.Status == 200) PublishOverlayConfig(overlayDataPath);
            }
            else if (request.HttpMethod == "POST" && path == "/api/first-run")
                await SendResultAsync(context, service.CompleteFirstRun(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/backups")
                await SendResultAsync(context, service.InvokeBackupOperation(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/roles")
                await SendResultAsync(context, service.UpdateDiscordRoleAwards(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/profiles")
            {
                var profileResult = service.InvokeProfileOperation(await ReadBodyAsync(request));
                await SendResultAsync(context, profileResult);
                if (profileResult.Status == 200)
                {
                    PublishOverlayStaticsForLiveProfiles(overlayPath, overlayDataPath, service.Store);
                    RefreshNativeTwitch(service, cancel);
                }
            }
            else if (request.HttpMethod == "POST" && path == "/api/runtime/action")
                await SendResultAsync(context, service.DispatchRuntimeAction(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/inventory/reset-viewer")
                await SendResultAsync(context, service.ResetViewer(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/inventory/remove-item")
                await SendResultAsync(context, service.RemoveInventoryItem(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/save")
                await SendResultAsync(context, service.SaveConfiguration(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/overlay-image")
            {
                // The declared Content-Type is ignored: SaveOverlayBackground sniffs the real image type
                // from the bytes, so a mislabeled payload can't be written as a background.
                var (bytes, _) = await ReadRawBodyAsync(request);
                var state = request.QueryString["state"] ?? "";
                await SendResultAsync(context, service.SaveOverlayBackground(bytes, state));
            }
            else if (request.HttpMethod == "GET" && path == "/overlay-bg")
                await SendOverlayBackgroundAsync(context, service.Store);
            else if (request.HttpMethod == "GET" && path == "/overlay-config.json")
                await SendOverlayConfigAsync(context, service.Store);
            else if (request.HttpMethod == "GET" && path.StartsWith("/overlay/", StringComparison.Ordinal))
                await SendOverlayFileAsync(context, overlayPath, overlayDataPath, path["/overlay/".Length..]);
            else
                await SendStaticAsync(context, uiPath, path);
        }
        catch (Exception exception)
        {
            await SendJsonAsync(context, 500, new { ok = false, errors = new[] { exception.Message } });
        }
        finally
        {
            context.Response.Close();
        }
    }

    // Accept only loopback Host headers. HttpListener already binds 127.0.0.1, but the Host header is
    // attacker-controlled (DNS rebinding), so we validate the hostname the request claims to address.
    private static bool IsAllowedHost(HttpListenerRequest request)
    {
        var host = request.UserHostName;
        if (string.IsNullOrEmpty(host)) return false;
        // Strip the port: "[::1]:8787" -> "[::1]", "127.0.0.1:8787" -> "127.0.0.1".
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            if (close > 0) host = host[1..close];
        }
        else
        {
            var colon = host.IndexOf(':');
            if (colon > 0) host = host[..colon];
        }
        return host is "127.0.0.1" or "localhost" or "::1";
    }

    // Companion to IsAllowedHost (see the CSRF note at the call site): if a browser attributed the
    // request to a web origin, it must be a loopback origin. "Origin: null" (sandboxed iframes,
    // file:// pages) is NOT loopback and is rejected — the OBS overlay never calls the API cross-origin.
    private static bool IsAllowedOrigin(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return uri.Host is "127.0.0.1" or "localhost" or "::1";
    }

    // Surfaces the Device Code Flow prompt by opening Twitch's activate page directly — the
    // verification_uri already has the code pre-filled, so the streamer just clicks Authorize while
    // the login request polls in the background. No dialog. Only if the browser fails to open (or
    // we're headless) do we fall back to showing the code to enter manually.
    private static void ShowDeviceCodePrompt(TwitchAuth.DeviceCodePrompt prompt)
    {
        if (!_headless)
        {
            try { Process.Start(new ProcessStartInfo(prompt.VerificationUri) { UseShellExecute = true }); return; }
            catch { /* fall through to the manual instructions below */ }
        }
        var text = $"Connect Twitch: go to {prompt.VerificationUri} and enter code {prompt.UserCode}.";
        if (_headless) Console.Out.WriteLine(text);
        else MessageBox.Show(text, "CircuitOS — Connect Twitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Settings page state: what's running now, the persisted backend choice, any cloud startup error,
    // and the (redacted) Appwrite connection status.
    private static object GetSettings() => new
    {
        ok = true,
        dataBackend = _sessionMode,
        cloudEnabled = AppSettings.CloudEnabled(_dataRoot),
        cloudError = _cloudError,
        dataRoot = _dataRoot,
        backupRetention = AppSettings.GetInt(_dataRoot, "backupRetention", LocalFileDataStore.DefaultBackupRetention),
        appwrite = AppwriteOptions.RedactedStatus(_dataRoot)
    };

    // Sets how many config backups to keep per file type (0 = keep all). Enforced on the next save.
    private static ServiceResult SetBackupRetention(JsonObject body)
    {
        try
        {
            var retention = (int)JsonUtil.Long(body, "retention");
            if (retention is < 0 or > 5000) throw new InvalidDataException("Backup retention must be between 0 and 5000 (0 = keep all).");
            AppSettings.Set(_dataRoot, "backupRetention", retention);
            return new ServiceResult(200, new JsonObject { ["ok"] = true, ["backupRetention"] = retention });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject { ["ok"] = false, ["errors"] = new JsonArray(ex.Message) });
        }
    }

    // Opens the data folder in the system file explorer (desktop app only).
    private static ServiceResult OpenDataFolder()
    {
        try
        {
            if (_headless) throw new InvalidOperationException("The data folder can only be opened from the desktop app.");
            if (!Directory.Exists(_dataRoot)) throw new DirectoryNotFoundException("Data folder was not found.");
            Process.Start(new ProcessStartInfo(_dataRoot) { UseShellExecute = true });
            return new ServiceResult(200, new JsonObject { ["ok"] = true });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject { ["ok"] = false, ["errors"] = new JsonArray(ex.Message) });
        }
    }

    private static ServiceResult SaveAppwriteSettings(JsonObject body)
    {
        try
        {
            AppwriteOptions.Save(_dataRoot,
                JsonUtil.String(body, "endpoint"),
                JsonUtil.String(body, "projectId"),
                JsonUtil.String(body, "apiKey"),
                JsonUtil.String(body, "databaseId"),
                JsonUtil.String(body, "collectionId"));
            return new ServiceResult(200, new JsonObject { ["ok"] = true, ["appwrite"] = AppwriteOptions.RedactedStatus(_dataRoot) });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject { ["ok"] = false, ["errors"] = new JsonArray(ex.Message) });
        }
    }

    private static ServiceResult TestAppwriteSettings()
    {
        var (ok, message) = TestAppwriteConnection(_dataRoot);
        return new ServiceResult(ok ? 200 : 400, new JsonObject { ["ok"] = ok, ["message"] = message });
    }

    // Persists the backend choice. Requires a configured Appwrite connection before allowing cloud.
    // restartRequired = the choice differs from what's running, so the app must relaunch to apply it.
    private static ServiceResult SetDataBackend(JsonObject body)
    {
        try
        {
            var backend = JsonUtil.String(body, "dataBackend").Trim().ToLowerInvariant();
            if (backend is not ("cloud" or "local"))
                throw new InvalidDataException("Choose either local or cloud.");
            if (backend == "cloud" && AppwriteOptions.RedactedStatus(_dataRoot)["configured"]?.GetValue<bool>() != true)
                throw new InvalidDataException("Add your Appwrite connection (endpoint, project, API key) before switching to cloud.");
            AppSettings.SetCloudEnabled(_dataRoot, backend == "cloud");
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["dataBackend"] = backend,
                ["restartRequired"] = !string.Equals(_sessionMode, backend, StringComparison.OrdinalIgnoreCase)
            });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject { ["ok"] = false, ["errors"] = new JsonArray(ex.Message) });
        }
    }

    // Connects to Appwrite with the saved config and confirms the configured table exists. Returns a
    // friendly (ok, message) for the Settings "Test connection" button. Never surfaces the API key.
    private static (bool Ok, string Message) TestAppwriteConnection(string dataRoot)
    {
        try
        {
            var opts = AppwriteOptions.TryLoad(dataRoot);
            if (opts is null) return (false, "No Appwrite connection is configured yet — fill in the endpoint, project, and API key first.");
            var client = new Appwrite.Client().SetEndpoint(opts.Endpoint).SetProject(opts.ProjectId).SetKey(opts.ApiKey);
            var tablesDb = new Appwrite.Services.TablesDB(client);
            var table = tablesDb.GetTable(opts.DatabaseId, opts.CollectionId).GetAwaiter().GetResult();
            return (true, $"Connected. Found table '{table.Name}' with {table.Columns.Count} column(s).");
        }
        catch (Appwrite.AppwriteException ex)
        {
            return (false, $"Appwrite rejected the connection (code {ex.Code}): {ex.Message}. 403 usually means the API key is missing scopes; 404 means the database or table id is wrong.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach Appwrite: {ex.Message}");
        }
    }

    // Inline device login, step 1: issue a device/user code for the admin panel to display. Returns
    // inline=false when a secret is configured (self-host) so the frontend falls back to the blocking
    // browser flow. The device code is held server-side and polled by loginId.
    private static ServiceResult StartDeviceLogin(bool bot = false)
    {
        try
        {
            var opts = TwitchOptions.Resolve(_dataRoot);
            if (opts.HasSecret && !bot)
                return new ServiceResult(200, new JsonObject { ["ok"] = true, ["inline"] = false });
            var request = TwitchAuth.RequestDeviceCode(opts, bot ? TwitchAuth.BotScopes : null);
            // Open the OS browser to the pre-filled activate page (reliable from the host, vs a
            // WebView2 window.open). The panel still shows the code and polls for completion.
            // For a BOT login, don't auto-open: the streamer is normally logged into their main
            // account in the default browser, and the whole point is authorizing a different account
            // (incognito/second browser). The panel shows the link + code instead.
            if (!_headless && !bot)
            {
                try { Process.Start(new ProcessStartInfo(request.VerificationUri) { UseShellExecute = true }); } catch { }
            }
            var loginId = Guid.NewGuid().ToString("N");
            lock (_pendingDeviceLogins)
            {
                foreach (var stale in _pendingDeviceLogins.Where(kv => kv.Value.ExpiresAt < DateTimeOffset.UtcNow).Select(kv => kv.Key).ToList())
                    _pendingDeviceLogins.Remove(stale);
                _pendingDeviceLogins[loginId] = (opts, request.DeviceCode, DateTimeOffset.UtcNow.AddSeconds(request.ExpiresInSeconds), bot);
            }
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["inline"] = true,
                ["loginId"] = loginId,
                ["userCode"] = request.UserCode,
                ["verificationUri"] = request.VerificationUri,
                ["expiresIn"] = request.ExpiresInSeconds,
                ["interval"] = request.IntervalSeconds
            });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject { ["ok"] = false, ["errors"] = new JsonArray(ex.Message) });
        }
    }

    // Inline device login, step 2: poll Twitch once for the given loginId. Returns status
    // pending / done / expired / error so the admin panel can drive its own polling loop.
    private static ServiceResult PollDeviceLogin(CircuitService service, JsonObject body, CancellationToken cancel)
    {
        var loginId = body["loginId"]?.ToString() ?? "";
        (TwitchOptions Opts, string DeviceCode, DateTimeOffset ExpiresAt, bool Bot) pending;
        lock (_pendingDeviceLogins)
        {
            if (!_pendingDeviceLogins.TryGetValue(loginId, out pending))
                return new ServiceResult(200, new JsonObject { ["ok"] = true, ["status"] = "expired" });
        }
        if (pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            lock (_pendingDeviceLogins) _pendingDeviceLogins.Remove(loginId);
            return new ServiceResult(200, new JsonObject { ["ok"] = true, ["status"] = "expired" });
        }
        try
        {
            var tokens = TwitchAuth.PollDeviceToken(pending.Opts, pending.DeviceCode, _dataRoot,
                pending.Bot ? TwitchAuth.BotScopes : null,
                pending.Bot ? TwitchTokens.BotFileName : TwitchTokens.FileName);
            if (tokens is null)
                return new ServiceResult(200, new JsonObject { ["ok"] = true, ["status"] = "pending" });
            lock (_pendingDeviceLogins) _pendingDeviceLogins.Remove(loginId);
            if (pending.Bot) _sessionTwitchBot = tokens; else _sessionTwitch = tokens;
            RefreshNativeTwitch(service, cancel);
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["status"] = "done",
                ["bot"] = pending.Bot,
                ["login"] = tokens.Login,
                ["displayName"] = tokens.DisplayName,
                ["userId"] = tokens.UserId
            });
        }
        catch (Exception ex)
        {
            lock (_pendingDeviceLogins) _pendingDeviceLogins.Remove(loginId);
            return new ServiceResult(400, new JsonObject { ["ok"] = false, ["status"] = "error", ["errors"] = new JsonArray(ex.Message) });
        }
    }

    private static ServiceResult ListTwitchRewards()
    {
        try
        {
            var options = TwitchOptions.Resolve(_dataRoot);
            var tokens = _sessionTwitch ?? TwitchTokens.TryLoad(_dataRoot)
                ?? throw new InvalidOperationException("Log in to Twitch before loading channel-point rewards.");
            var helix = new TwitchHelix(new TwitchSession(options, tokens, _dataRoot));
            var manageableIds = helix.ListManageableRewards().Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var rewards = new JsonArray(helix.ListRewards()
                .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .Select(r => RewardObject(r with { Manageable = manageableIds.Contains(r.Id) }))
                .ToArray());
            _sessionTwitch = TwitchTokens.TryLoad(_dataRoot) ?? tokens;
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["rewards"] = rewards
            });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject
            {
                ["ok"] = false,
                ["errors"] = new JsonArray(ex.Message)
            });
        }
    }

    private static JsonObject RewardObject(CustomReward reward) => new()
    {
        ["rewardId"] = reward.Id,
        ["title"] = reward.Title,
        ["cost"] = reward.Cost,
        ["manageable"] = reward.Manageable
    };
    private static ServiceResult SyncTwitchReward(CircuitService service, JsonObject body)
    {
        try
        {
            var profileId = body["profileId"]?.ToString();
            if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("Choose a live profile before syncing a Twitch reward.");
            var options = TwitchOptions.Resolve(_dataRoot);
            var tokens = _sessionTwitch ?? TwitchTokens.TryLoad(_dataRoot)
                ?? throw new InvalidOperationException("Log in to Twitch before syncing channel-point rewards.");
            var session = new TwitchSession(options, tokens, _dataRoot);
            var helix = new TwitchHelix(session);
            var selectedRewardId = body["rewardId"]?.ToString();
            CustomReward reward;
            if (string.IsNullOrWhiteSpace(selectedRewardId))
            {
                reward = TwitchRuntime.SyncRewardForProfile(service.Store, profileId, helix, Console.WriteLine);
            }
            else
            {
                var manageableIds = helix.ListManageableRewards().Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                var selected = helix.ListRewards().FirstOrDefault(r => string.Equals(r.Id, selectedRewardId, StringComparison.Ordinal))
                    ?? throw new InvalidDataException("The selected Twitch reward was not found. Refresh rewards and try again.");
                reward = TwitchRuntime.AttachRewardForProfile(service.Store, profileId, selected with { Manageable = manageableIds.Contains(selected.Id) }, Console.WriteLine);
            }
            _sessionTwitch = TwitchTokens.TryLoad(_dataRoot) ?? tokens;
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["profileId"] = profileId,
                ["reward"] = RewardObject(reward),
                ["profiles"] = service.GetProfiles()["profiles"]?.DeepClone()
            });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject
            {
                ["ok"] = false,
                ["errors"] = new JsonArray(ex.Message)
            });
        }
    }
    private static ServiceResult UpdateTwitchReward(CircuitService service, JsonObject body)
    {
        try
        {
            var profileId = body["profileId"]?.ToString();
            var title = JsonUtil.String(body, "title").Trim();
            var cost = (int)JsonUtil.Long(body, "cost");
            if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("Choose a profile before editing a Twitch reward.");
            var options = TwitchOptions.Resolve(_dataRoot);
            var tokens = _sessionTwitch ?? TwitchTokens.TryLoad(_dataRoot)
                ?? throw new InvalidOperationException("Log in to Twitch before editing channel-point rewards.");
            var session = new TwitchSession(options, tokens, _dataRoot);
            var helix = new TwitchHelix(session);
            var reward = TwitchRuntime.UpdateRewardForProfile(service.Store, profileId, title, cost, helix, Console.WriteLine);
            _sessionTwitch = TwitchTokens.TryLoad(_dataRoot) ?? tokens;
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["profileId"] = profileId,
                ["reward"] = RewardObject(reward),
                ["profile"] = service.GetSystemProfile()["profile"]?.DeepClone(),
                ["profiles"] = service.GetProfiles()["profiles"]?.DeepClone()
            });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject
            {
                ["ok"] = false,
                ["errors"] = new JsonArray(ex.Message)
            });
        }
    }
    private static ServiceResult DeleteTwitchReward(CircuitService service, JsonObject body)
    {
        try
        {
            var profileId = body["profileId"]?.ToString();
            if (string.IsNullOrWhiteSpace(profileId)) throw new InvalidDataException("Choose a profile before deleting a Twitch reward.");
            var options = TwitchOptions.Resolve(_dataRoot);
            var tokens = _sessionTwitch ?? TwitchTokens.TryLoad(_dataRoot)
                ?? throw new InvalidOperationException("Log in to Twitch before deleting channel-point rewards.");
            var session = new TwitchSession(options, tokens, _dataRoot);
            var helix = new TwitchHelix(session);
            var reward = TwitchRuntime.DeleteRewardForProfile(service.Store, profileId, helix, Console.WriteLine);
            _sessionTwitch = TwitchTokens.TryLoad(_dataRoot) ?? tokens;
            return new ServiceResult(200, new JsonObject
            {
                ["ok"] = true,
                ["profileId"] = profileId,
                ["deletedReward"] = RewardObject(reward),
                ["profiles"] = service.GetProfiles()["profiles"]?.DeepClone()
            });
        }
        catch (Exception ex)
        {
            return new ServiceResult(400, new JsonObject
            {
                ["ok"] = false,
                ["errors"] = new JsonArray(ex.Message)
            });
        }
    }
    private static async Task<JsonObject> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 is < 0 or > 1_048_576) throw new InvalidDataException("Request body is too large.");
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, leaveOpen: false);
        var text = await reader.ReadToEndAsync();
        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Request body must be a JSON object.");
    }

    private static async Task<(byte[] Bytes, string ContentType)> ReadRawBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 is < 0 or > 10_485_760) throw new InvalidDataException("Image too large (max 10 MB).");
        var bytes = new byte[request.ContentLength64];
        var read = 0;
        while (read < bytes.Length)
        {
            var chunk = await request.InputStream.ReadAsync(bytes.AsMemory(read));
            if (chunk == 0) break;
            read += chunk;
        }
        return (bytes, request.ContentType ?? "application/octet-stream");
    }

    private static Task SendResultAsync(HttpListenerContext context, ServiceResult result) =>
        SendJsonAsync(context, result.Status, result.Body);

    private static async Task SendJsonAsync(HttpListenerContext context, int status, object value)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, JsonUtil.CompactOptions);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await context.Response.OutputStream.WriteAsync(body);
    }

    private static async Task SendStaticAsync(HttpListenerContext context, string uiPath, string path)
    {
        var files = new Dictionary<string, (string Name, string ContentType)>(StringComparer.Ordinal)
        {
            ["/"] = ("index.html", "text/html; charset=utf-8"),
            ["/index.html"] = ("index.html", "text/html; charset=utf-8"),
            ["/styles.css"] = ("styles.css", "text/css; charset=utf-8"),
            ["/app.js"] = ("app.js", "application/javascript; charset=utf-8"),
            ["/circuitos-icon.png"] = ("circuitos-icon.png", "image/png")
        };
        if (!files.TryGetValue(path, out var entry))
        {
            context.Response.StatusCode = 404;
            return;
        }
        var filePath = Path.Combine(uiPath, entry.Name);
        var body = await File.ReadAllBytesAsync(filePath);
        context.Response.StatusCode = 200;
        context.Response.ContentType = entry.ContentType;
        context.Response.ContentLength64 = body.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Content-Security-Policy for the admin document: the panel loads only its own app.js (no inline
        // scripts), so 'self' script-src blocks any injected inline script — the XSS backstop if a
        // hand-edited profile/collection value ever reached the DOM unescaped. Inline styles are allowed
        // because live theming sets element.style; images allow data:/blob: for previews.
        if (entry.ContentType.StartsWith("text/html", StringComparison.Ordinal))
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'";
        await context.Response.OutputStream.WriteAsync(body);
    }

    private static async Task SendOverlayConfigAsync(HttpListenerContext context, IDataStore store)
    {
        var config = store.TryRead(DataKeys.OverlayConfig);
        if (config is null) { context.Response.StatusCode = 404; return; }
        var body = JsonSerializer.SerializeToUtf8Bytes(config, JsonUtil.IndentedOptions);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await context.Response.OutputStream.WriteAsync(body);
    }

    private static async Task SendOverlayBackgroundAsync(HttpListenerContext context, IDataStore store)
    {
        var background = store.FindBackground();
        if (background is null) { context.Response.StatusCode = 404; return; }
        var (filePath, contentType) = background.Value;
        var body = await File.ReadAllBytesAsync(filePath);
        context.Response.StatusCode = 200;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = body.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        await context.Response.OutputStream.WriteAsync(body);
    }

    // Copies the static overlay assets into the active profile's overlay folder so
    // OBS can point to it with a local file:// URL and fetch overlay-state.json from
    // the same directory. Called on startup and after every profile switch.
    // 0.7 diagnostic: load appwrite.local.json, connect, and confirm the configured
    // collection exists. Shows the result in a dialog (or stdout/stderr when headless).
    // Verifies endpoint + project id + API key + database/collection ids in one call.
    private static int CheckAppwrite(string dataRoot, bool headless)
    {
        string message;
        var ok = false;
        AppwriteOptions? opts = null;
        try
        {
            opts = AppwriteOptions.TryLoad(dataRoot);
            if (opts is null)
            {
                message = $"No Appwrite config found.\n\nExpected {AppwriteOptions.FileName} in:\n{dataRoot}\n\nSee docs/0.7-appwrite-dev-setup.md.";
            }
            else
            {
                var client = new Appwrite.Client()
                    .SetEndpoint(opts.Endpoint)
                    .SetProject(opts.ProjectId)
                    .SetKey(opts.ApiKey);
                // Appwrite 1.8+ uses TablesDB (Tables/Rows/Columns) — the modern name for
                // Collections/Documents/Attributes. The configured collectionId is the table id.
                var tablesDb = new Appwrite.Services.TablesDB(client);
                var table = tablesDb.GetTable(opts.DatabaseId, opts.CollectionId).GetAwaiter().GetResult();
                ok = true;
                message = $"Connected to Appwrite.\n\n{opts.Describe()}\n\nTable/collection '{table.Name}' found (id: {table.Id}) with {table.Columns.Count} column(s) and {table.Indexes.Count} index(es).";
            }
        }
        catch (Appwrite.AppwriteException ex)
        {
            message = $"Appwrite request failed (code {ex.Code}, type {ex.Type}):\n{ex.Message}\n\nUsing: {opts?.Describe()}\n\n"
                + "403 = the API key lacks scope OR is not the key with the scopes (check 'Last accessed' on the key, and that you clicked Update). "
                + "404 = the key works but the table/collection id doesn't exist. See docs/0.7-appwrite-dev-setup.md.";
        }
        catch (Exception ex)
        {
            message = $"Appwrite check failed:\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Appwrite check", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // 0.7 diagnostic: prove AppwriteDataStore can write+read+delete in the cloud,
    // using a throwaway tenant/profile. Mutates only a reserved test row, cleaned up.
    private static int RoundtripAppwrite(string dataRoot, bool headless)
    {
        string message;
        var ok = false;
        AppwriteOptions? opts = null;
        try
        {
            opts = AppwriteOptions.TryLoad(dataRoot);
            if (opts is null)
            {
                message = $"No Appwrite config found.\n\nExpected {AppwriteOptions.FileName} in:\n{dataRoot}\n\nSee docs/0.7-appwrite-dev-setup.md.";
            }
            else
            {
                var store = new AppwriteDataStore(opts, "roundtrip-user", "roundtrip-profile");
                var detail = store.RoundTripSelfTest();
                ok = true;
                message = $"Appwrite round-trip passed.\n\n{opts.Describe()}\n\n{detail}\n\nThe cloud data layer can read and write to your project.";
            }
        }
        catch (Appwrite.AppwriteException ex)
        {
            message = $"Round-trip failed (code {ex.Code}, type {ex.Type}):\n{ex.Message}\n\nUsing: {opts?.Describe()}\n\n"
                + "Likely a missing write scope (documents.write / rows.write) or a column mismatch with docs/0.7-appwrite-dev-setup.md.";
        }
        catch (Exception ex)
        {
            message = $"Round-trip failed:\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Appwrite round-trip", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // 0.7 migration: reads the local active profile's data files and writes each into
    // Appwrite (model A: one row per data key), then reads each back to verify. The
    // local files are only read, never modified. Tenant is a dev placeholder until auth.
    private static int PushToAppwrite(string dataRoot, bool headless)
    {
        var devTenant = ResolveTenant(dataRoot); // Twitch user id once logged in, else "local-dev"
        string message;
        var ok = false;
        AppwriteOptions? opts = null;
        try
        {
            opts = AppwriteOptions.TryLoad(dataRoot);
            if (opts is null)
            {
                message = $"No Appwrite config found.\n\nExpected {AppwriteOptions.FileName} in:\n{dataRoot}\n\nSee docs/0.7-appwrite-dev-setup.md.";
            }
            else
            {
                var local = new LocalFileDataStore(dataRoot);
                var cloud = new AppwriteDataStore(opts, devTenant, local.ActiveProfileId);
                var keys = new[]
                {
                    DataKeys.Catalog, DataKeys.Boost, DataKeys.Inventory,
                    DataKeys.Roles, DataKeys.Profile, DataKeys.OverlayConfig
                };
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                var pushed = new List<string>();
                var verified = new List<string>();
                foreach (var key in keys)
                {
                    var node = local.TryRead(key);
                    if (node is null) continue;
                    cloud.WriteAtomic(key, node, key, timestamp);
                    pushed.Add(key);
                    if (cloud.TryRead(key) is not null) verified.Add(key);
                }
                ok = pushed.Count > 0 && verified.Count == pushed.Count;
                message = $"Pushed {pushed.Count} file(s) to Appwrite, verified {verified.Count}.\n\n"
                    + $"Tenant '{devTenant}', profile '{local.ActiveProfileId}':\n  {(pushed.Count > 0 ? string.Join(", ", pushed) : "(nothing local to push)")}\n\n"
                    + $"{opts.Describe()}\n\nOpen the profile_data table in your Appwrite console — you should see these rows.";
            }
        }
        catch (Appwrite.AppwriteException ex)
        {
            message = $"Push failed (code {ex.Code}, type {ex.Type}):\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }
        catch (Exception ex)
        {
            message = $"Push failed:\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Push to Appwrite", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // 0.7 Phase 2b diagnostic: verify cloud profile CRUD (list, create, rename, delete)
    // using a throwaway test profile that is cleaned up afterward.
    private static int ProfilesAppwrite(string dataRoot, bool headless)
    {
        const string testId = "dev-test-profile";
        string message;
        var ok = false;
        AppwriteOptions? opts = null;
        try
        {
            opts = AppwriteOptions.TryLoad(dataRoot);
            if (opts is null)
            {
                message = $"No Appwrite config found.\n\nExpected {AppwriteOptions.FileName} in:\n{dataRoot}\n\nSee docs/0.7-appwrite-dev-setup.md.";
            }
            else
            {
                var local = new LocalFileDataStore(dataRoot);
                var store = new AppwriteDataStore(opts, ResolveTenant(dataRoot), local.ActiveProfileId);

                try { if (store.ListProfiles().Any(p => p.Id == testId)) store.DeleteProfile(testId); } catch { }

                var before = store.ListProfiles().Select(p => p.Name).ToList();
                store.CreateProfile(testId, "Dev Test");
                if (!store.ListProfiles().Any(p => p.Id == testId && p.Name == "Dev Test"))
                    throw new InvalidOperationException("Created profile was not listed with its name.");
                store.RenameProfile(testId, "Dev Test Renamed");
                if (!store.ListProfiles().Any(p => p.Id == testId && p.Name == "Dev Test Renamed"))
                    throw new InvalidOperationException("Rename did not take effect.");
                store.DeleteProfile(testId);
                if (store.ListProfiles().Any(p => p.Id == testId))
                    throw new InvalidOperationException("Deleted profile is still listed.");

                ok = true;
                message = $"Profile CRUD passed.\n\n{opts.Describe()}\n\n"
                    + $"Existing profiles: {(before.Count > 0 ? string.Join(", ", before) : "(none)")}\n"
                    + "Create, list, rename, delete all verified (test profile cleaned up).";
            }
        }
        catch (Appwrite.AppwriteException ex)
        {
            message = $"Profiles check failed (code {ex.Code}, type {ex.Type}):\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }
        catch (Exception ex)
        {
            message = $"Profiles check failed:\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Appwrite profiles", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // 0.7 Phase 2b diagnostic: verify the cloud backup recovery point round-trips
    // (write v1, overwrite v2, confirm backup holds v1), against a throwaway key.
    private static int BackupsAppwrite(string dataRoot, bool headless)
    {
        string message;
        var ok = false;
        AppwriteOptions? opts = null;
        try
        {
            opts = AppwriteOptions.TryLoad(dataRoot);
            if (opts is null)
            {
                message = $"No Appwrite config found.\n\nExpected {AppwriteOptions.FileName} in:\n{dataRoot}\n\nSee docs/0.7-appwrite-dev-setup.md.";
            }
            else
            {
                var local = new LocalFileDataStore(dataRoot);
                var store = new AppwriteDataStore(opts, ResolveTenant(dataRoot), local.ActiveProfileId);
                var detail = store.BackupSelfTest();
                ok = true;
                message = $"Cloud backup recovery point works.\n\n{opts.Describe()}\n\n{detail}\n\n"
                    + "Every cloud save now snapshots the prior version; the Backups view can restore it.";
            }
        }
        catch (Appwrite.AppwriteException ex)
        {
            message = $"Backups check failed (code {ex.Code}, type {ex.Type}):\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }
        catch (Exception ex)
        {
            message = $"Backups check failed:\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Appwrite backups", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // The cloud tenant id: the logged-in Twitch user id when present, else the
    // "local-dev" placeholder. Push and --cloud use this so they agree on the tenant.
    private static string ResolveTenant(string dataRoot) =>
        TwitchTokens.TryLoad(dataRoot)?.UserId ?? "local-dev";

    // 0.7 Phase 3: Twitch OAuth login. Opens the browser to Twitch, captures the
    // loopback redirect, exchanges the code, fetches identity, and caches tokens.
    private static int TwitchLogin(string dataRoot, bool headless)
    {
        string message;
        var ok = false;
        TwitchOptions? opts = null;
        try
        {
            // Resolve never returns null: a present twitch.local.json wins, else the bundled client id.
            opts = TwitchOptions.Resolve(dataRoot);
            TwitchTokens tokens;
            if (opts.HasSecret)
            {
                // Legacy loopback authorization-code flow (self-host / advanced: their own Twitch app + secret).
                tokens = TwitchAuth.Login(opts, dataRoot);
            }
            else
            {
                // Zero-config Device Code Flow: no secret, works with the bundled CircuitOS client id.
                void Prompt(TwitchAuth.DeviceCodePrompt p)
                {
                    var instr = $"To connect Twitch:\n\n1. Go to {p.VerificationUri}\n2. Enter this code: {p.UserCode}\n\nThis window finishes once you authorize (code expires in {p.ExpiresInSeconds / 60} min).";
                    if (headless) Console.Out.WriteLine(instr);
                    else { try { Process.Start(new ProcessStartInfo(p.VerificationUri) { UseShellExecute = true }); } catch { } MessageBox.Show(instr, "CircuitOS — Twitch login", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
                tokens = TwitchAuth.LoginDeviceFlow(opts, dataRoot, Prompt, CancellationToken.None);
            }
            ok = true;
            message = $"Logged in to Twitch.\n\nDisplay name: {tokens.DisplayName}\nLogin: {tokens.Login}\nUser id: {tokens.UserId}\n\n"
                + $"Tokens cached (encrypted) in {TwitchTokens.FileName}.";
        }
        catch (Exception ex)
        {
            message = $"Twitch login failed:\n{ex.Message}\n\nUsing: {opts?.Describe()}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Twitch login", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // 0.7 Phase 4 (native Twitch): create/update the channel-point reward on the logged-in
    // streamer's channel via Helix, titled from the active profile's redemption name. Proves the
    // Helix integration (auth + token + reward CRUD) end to end before EventSub is wired.
    private static int TwitchReward(string dataRoot, bool headless)
    {
        string message;
        var ok = false;
        try
        {
            var opts = TwitchOptions.Resolve(dataRoot);
            var tokens = TwitchTokens.TryLoad(dataRoot)
                ?? throw new InvalidOperationException("Not logged in to Twitch — run --twitch-login first.");
            var session = new TwitchSession(opts, tokens, dataRoot);
            var helix = new TwitchHelix(session);

            var profile = new LocalFileDataStore(dataRoot).TryRead(DataKeys.Profile);
            var title = JsonUtil.String(profile ?? new JsonObject(), "redemptionName");
            if (string.IsNullOrWhiteSpace(title)) title = "Circuit Component";

            var reward = helix.EnsureReward(title, 100, "Redeem to pull an item with CircuitOS.");
            ok = true;
            message = $"Channel-point reward ready on @{session.Login}'s channel.\n\n"
                + $"Title: {reward.Title}\nCost: {reward.Cost} points\nReward id: {reward.Id}\n\n"
                + "Open your Twitch channel's Channel Points — you should see this reward. The cost is a "
                + "placeholder (100) for now and will become configurable. Next slice: --twitch-listen for live redemptions.";
        }
        catch (Exception ex)
        {
            message = $"Reward setup failed:\n{ex.Message}";
        }

        if (headless)
        {
            if (ok) Console.Out.WriteLine(message); else Console.Error.WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "CircuitOS — Twitch reward", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }

    // 0.7 Phase 4 (native Twitch): the live redemption loop. Ensures each live profile's reward
    // exists, maps reward id -> profile, opens the EventSub WebSocket, and on each redemption runs
    // the shared dispatch (pull + inventory) then fulfils (or cancels/refunds on failure). Blocks
    // until Ctrl+C. Console mode — run from a terminal.
    private static int TwitchListen(string dataRoot)
    {
        try
        {
            var store = new LocalFileDataStore(dataRoot);
            var service = new CircuitService(store);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var task = TwitchRuntime.TryStart(store, service, dataRoot, Console.WriteLine, cts.Token);
            if (task is null)
            {
                Console.Error.WriteLine("Twitch isn't configured — run --twitch-login first.");
                return 1;
            }
            Console.WriteLine("CircuitOS native Twitch — press Ctrl+C to stop.");
            task.GetAwaiter().GetResult();
            Console.WriteLine("Stopped listening.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Listen failed: {ex.Message}");
            return 1;
        }
    }

    // Copies overlay statics and a normalized overlay-config.json into the active
    // profile's overlay folder so OBS local-file mode needs no cross-directory fetches.
    // First-run seeding for an installed build. The per-user data folder starts empty; copy the starter
    // catalog/config bundled beside the exe (StarterData/) into it so the app has a catalog to run the
    // first-run wizard against. Only seeds a genuinely empty root — an existing install (flat starter
    // files or a migrated profiles/ tree) is never touched. Dev/portable have no StarterData → no-op.
    private static void SeedInstalledDataIfEmpty(string dataPath, string exeDir)
    {
        try
        {
            if (File.Exists(Path.Combine(dataPath, "components.json")) || Directory.Exists(Path.Combine(dataPath, "profiles")))
                return;
            var starter = Path.Combine(exeDir, "StarterData");
            if (!File.Exists(Path.Combine(starter, "components.json"))) return;
            Directory.CreateDirectory(dataPath);
            foreach (var file in Directory.GetFiles(starter))
                File.Copy(file, Path.Combine(dataPath, Path.GetFileName(file)), overwrite: false);
        }
        catch { /* best-effort; the catalog check right after surfaces a genuine failure to the user */ }
    }

    // The OBS overlay is per-profile: each LIVE profile needs its own overlay/index.html so a streamer
    // can point a separate OBS browser source at each live game. Publishes to the active (editing)
    // profile as always, plus every other live profile whose local folder exists (a cloud-mode profile
    // id may have no local folder — skipped; the overlay is a local-file concern).
    private static void PublishOverlayStaticsForLiveProfiles(string overlayPath, string activeProfilePath, IDataStore store)
    {
        PublishOverlayStatics(overlayPath, activeProfilePath);
        try
        {
            var profilesRoot = Path.Combine(_dataRoot, "profiles");
            foreach (var profile in store.ListProfiles().Where(p => p.IsLive))
            {
                var folder = Path.Combine(profilesRoot, profile.Id);
                if (Directory.Exists(folder)
                    && !string.Equals(Path.GetFullPath(folder), Path.GetFullPath(activeProfilePath), StringComparison.OrdinalIgnoreCase))
                    PublishOverlayStatics(overlayPath, folder);
            }
        }
        catch { }
    }

    private static void PublishOverlayStatics(string overlayPath, string dataPath)
    {
        try
        {
            var targetDir = Path.Combine(dataPath, "overlay");
            Directory.CreateDirectory(targetDir);
            foreach (var fileName in new[] { "index.html", "overlay.js", "styles.css" })
            {
                var src = Path.Combine(overlayPath, fileName);
                if (File.Exists(src)) File.Copy(src, Path.Combine(targetDir, fileName), overwrite: true);
            }
            PublishOverlayConfig(dataPath, targetDir);
        }
        catch { }
    }

    // Writes a copy of overlay-config.json into the overlay folder, normalizing
    // any legacy "/overlay-bg" background URL to the actual bg filename on disk.
    private static void PublishOverlayConfig(string dataPath, string? targetDir = null)
    {
        try
        {
            targetDir ??= Path.Combine(dataPath, "overlay");
            Directory.CreateDirectory(targetDir);
            var configSrc = Path.Combine(dataPath, "overlay-config.json");
            if (!File.Exists(configSrc)) return;

            var text = File.ReadAllText(configSrc, System.Text.Encoding.UTF8);
            var node = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject;
            if (node == null) return;

            // Normalize legacy HTTP background URL to the relative filename
            if (node["appearance"] is System.Text.Json.Nodes.JsonObject appearance)
            {
                var bg = appearance["backgroundImage"]?.ToString() ?? "";
                if (bg.StartsWith("/overlay-bg", StringComparison.Ordinal))
                {
                    var bgNames = new[] { "bg.png", "bg.jpg", "bg.gif", "bg.webp" };
                    appearance["backgroundImage"] = bgNames.FirstOrDefault(n => File.Exists(Path.Combine(targetDir, n))) ?? "";
                }
            }

            File.WriteAllText(
                Path.Combine(targetDir, "overlay-config.json"),
                node.ToJsonString(),
                new System.Text.UTF8Encoding(false));
        }
        catch { }
    }

    // Matches only the global (bg.<ext>) and per-state (bg-rare/complete/duplicate.<ext>) overlay
    // background files, by exact stem — so a request can't traverse to arbitrary files.
    private static bool IsOverlayBackground(string fileName, out string contentType)
    {
        contentType = "";
        foreach (var (ext, mime) in new[] { (".png", "image/png"), (".jpg", "image/jpeg"), (".gif", "image/gif"), (".webp", "image/webp") })
        {
            if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
            var stem = fileName[..^ext.Length].ToLowerInvariant();
            if (stem is "bg" or "bg-rare" or "bg-complete" or "bg-duplicate") { contentType = mime; return true; }
        }
        return false;
    }

    private static async Task SendOverlayFileAsync(
        HttpListenerContext context, string overlayPath, string dataPath, string fileName)
    {
        var staticMimes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["index.html"] = "text/html; charset=utf-8",
            ["styles.css"] = "text/css; charset=utf-8",
            ["overlay.js"] = "application/javascript; charset=utf-8"
        };
        // Files served from DataPath/overlay/ — state/config written by CircuitOS, images uploaded via admin
        var dataMimes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["overlay-state.json"] = "application/json; charset=utf-8",
            ["overlay-config.json"] = "application/json; charset=utf-8"
        };

        string filePath;
        string contentType;
        if (dataMimes.TryGetValue(fileName, out contentType!) || IsOverlayBackground(fileName, out contentType))
            filePath = Path.Combine(dataPath, "overlay", fileName);
        else if (staticMimes.TryGetValue(fileName, out contentType!))
            filePath = Path.Combine(overlayPath, fileName);
        else
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (!File.Exists(filePath))
        {
            context.Response.StatusCode = 404;
            return;
        }
        var body = await File.ReadAllBytesAsync(filePath);
        context.Response.StatusCode = 200;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = body.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await context.Response.OutputStream.WriteAsync(body);
    }
}




