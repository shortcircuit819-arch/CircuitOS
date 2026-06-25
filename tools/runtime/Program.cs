using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed record RuntimeOptions(string DataPath, string UiPath, string ActionPath, string OverlayPath, int Port, bool Headless)
{
    public static RuntimeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--headless" or "--no-browser" or "--check-appwrite" or "--appwrite-roundtrip" or "--push-to-appwrite" or "--appwrite-profiles" or "--appwrite-backups" or "--twitch-login" or "--cloud") { flags.Add(arg); continue; }
            if (arg.StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length) values[arg] = args[++index];
        }

        var basePath = Path.GetFullPath(AppContext.BaseDirectory);
        var uiPath = Path.GetFullPath(values.GetValueOrDefault("--ui", FindFolderContaining(
            "index.html",
            Path.Combine(basePath, "App"),
            basePath,
            Path.Combine(basePath, "..")) ?? Path.Combine(basePath, "App")));
        var dataPath = Path.GetFullPath(values.GetValueOrDefault("--data", FindFolderContaining(
            "components.json",
            Path.Combine(basePath, "Data"),
            Path.Combine(basePath, "..", "Data"),
            Path.Combine(uiPath, "..", "Data"),
            Path.Combine(uiPath, "..", "..", "data")) ?? Path.Combine(basePath, "Data")));
        var actionPath = Path.GetFullPath(values.GetValueOrDefault("--actions", FindFolderContaining(
            "StreamerbotReedeem.txt",
            Path.Combine(basePath, "Streamerbot Actions"),
            Path.Combine(basePath, "..", "Streamerbot Actions"),
            Path.Combine(uiPath, "..", "Streamerbot Actions"),
            Path.Combine(uiPath, "..", "..", "streamerbot-actions")) ?? Path.Combine(basePath, "Streamerbot Actions")));
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
        return new RuntimeOptions(dataPath, uiPath, actionPath, overlayPath, port,
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
    private static string _dataRoot = "";

    [STAThread]
    public static int Main(string[] args)
    {
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

        // The local file store is always created: it provides the active profile id and the
        // local folder used to serve the OBS overlay (overlay statics/state stay local even
        // when game data lives in the cloud). With --cloud, game data is read/written through
        // AppwriteDataStore instead; the local store keeps serving the overlay path.
        var localStore = new LocalFileDataStore(options.DataPath);
        IDataStore store = localStore;
        if (args.Contains("--cloud"))
        {
            var opts = AppwriteOptions.TryLoad(options.DataPath)
                ?? throw new InvalidOperationException($"--cloud requires {AppwriteOptions.FileName} in the data folder. See docs/0.7-appwrite-dev-setup.md.");
            var tenant = ResolveTenant(options.DataPath);
            if (!string.Equals(tenant, "local-dev", StringComparison.Ordinal))
            {
                var fromTenant = "local-dev";
                try { AppwriteDataStore.MigrateRowsToTenant(opts, fromTenant, tenant); }
                catch (Exception ex) { Console.Error.WriteLine($"Tenant migration warning: {ex.Message}"); }
            }
            store = new AppwriteDataStore(opts, tenant, localStore.ActiveProfileId);
        }
        var service = new CircuitService(store, options.ActionPath);
        var overlayDataPath = localStore.DataPath;
        _sessionMode = args.Contains("--cloud") ? "cloud" : "local";
        _sessionTwitch = TwitchTokens.TryLoad(options.DataPath);
        _dataRoot = options.DataPath;

        PublishOverlayStatics(options.OverlayPath, overlayDataPath);

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

            if (options.Headless)
            {
                serverTask.GetAwaiter().GetResult();
                return 0;
            }

            ApplicationConfiguration.Initialize();
            using var window = new CircuitWindow(url);
            Application.Run(window);

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
            _ = Task.Run(() => HandleRequestAsync(context, service, uiPath, overlayPath, overlayDataPath), cancellationToken);
        }
    }

    // overlayDataPath is the local folder where the OBS overlay statics + state live; it
    // stays local even when game data is served from the cloud (--cloud).
    private static async Task HandleRequestAsync(HttpListenerContext context, CircuitService service, string uiPath, string overlayPath, string overlayDataPath)
    {
        try
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath ?? "/";
            if (request.HttpMethod == "GET" && path == "/api/health")
                await SendJsonAsync(context, 200, new
                {
                    ok = true,
                    dataPath = overlayDataPath,
                    overlayFilePath = Path.Combine(overlayDataPath, "overlay", "index.html"),
                    runtime = ".NET",
                    version = "0.6.0.8",
                    mode = _sessionMode,
                    twitch = _sessionTwitch is null ? null : new { login = _sessionTwitch.Login, displayName = _sessionTwitch.DisplayName, userId = _sessionTwitch.UserId, expiresAt = _sessionTwitch.ExpiresAt }
                });
            else if (request.HttpMethod == "POST" && path == "/api/twitch/logout")
            {
                try { var tokenFile = Path.Combine(_dataRoot, TwitchTokens.FileName); if (File.Exists(tokenFile)) File.Delete(tokenFile); } catch { }
                _sessionTwitch = null;
                await SendJsonAsync(context, 200, new { ok = true });
            }
            else if (request.HttpMethod == "POST" && path == "/api/twitch/login")
            {
                // Runs the interactive OAuth flow (opens the browser, waits for consent).
                // The request blocks until the user authorizes or it times out.
                try
                {
                    var twitchOptions = TwitchOptions.TryLoad(_dataRoot)
                        ?? throw new InvalidOperationException($"{TwitchOptions.FileName} was not found in the data folder. See docs/0.7-twitch-auth-setup.md.");
                    var tokens = TwitchAuth.Login(twitchOptions, _dataRoot);
                    _sessionTwitch = tokens;
                    await SendJsonAsync(context, 200, new { ok = true, login = tokens.Login, displayName = tokens.DisplayName, userId = tokens.UserId });
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(context, 400, new { ok = false, error = ex.Message });
                }
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
            else if (request.HttpMethod == "POST" && path == "/api/setup")
            {
                var body = await ReadBodyAsync(request);
                await SendResultAsync(context, service.GetStreamerBotSetup(body["profile"] as JsonObject));
            }
            else if (request.HttpMethod == "POST" && path == "/api/backups")
                await SendResultAsync(context, service.InvokeBackupOperation(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/roles")
                await SendResultAsync(context, service.UpdateDiscordRoleAwards(await ReadBodyAsync(request)));
            else if (request.HttpMethod == "POST" && path == "/api/profiles")
            {
                var profileResult = service.InvokeProfileOperation(await ReadBodyAsync(request));
                await SendResultAsync(context, profileResult);
                if (profileResult.Status == 200) PublishOverlayStatics(overlayPath, overlayDataPath);
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
                var (bytes, contentType) = await ReadRawBodyAsync(request);
                await SendResultAsync(context, service.SaveOverlayBackground(bytes, contentType));
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
            opts = TwitchOptions.TryLoad(dataRoot);
            if (opts is null)
            {
                message = $"No Twitch config found.\n\nExpected {TwitchOptions.FileName} in:\n{dataRoot}\n\nSee docs/0.7-twitch-auth-setup.md.";
            }
            else
            {
                var tokens = TwitchAuth.Login(opts, dataRoot);
                ok = true;
                message = $"Logged in to Twitch.\n\nDisplay name: {tokens.DisplayName}\nLogin: {tokens.Login}\nUser id: {tokens.UserId}\n\n"
                    + $"Tokens cached in {TwitchTokens.FileName}. Push and --cloud now use this Twitch id as the tenant.\n"
                    + "Next: re-run --push-to-appwrite to move your data under this id, then --cloud.";
            }
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

    // Copies overlay statics and a normalized overlay-config.json into the active
    // profile's overlay folder so OBS local-file mode needs no cross-directory fetches.
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
            ["overlay-config.json"] = "application/json; charset=utf-8",
            ["bg.png"] = "image/png",
            ["bg.jpg"] = "image/jpeg",
            ["bg.gif"] = "image/gif",
            ["bg.webp"] = "image/webp"
        };

        string filePath;
        string contentType;
        if (dataMimes.TryGetValue(fileName, out contentType!))
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
