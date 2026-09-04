using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using CircuitOS.Runtime;
using System.Diagnostics;

using var server = new FakeTables();
var store = new AppwriteDataStore(server.Options, "review", "default");
var failures = new List<string>();
void Test(string name, Action body) { try { server.Rows.Clear(); body(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures.Add(name); Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } catch (FileNotFoundException) { return; } throw new Exception("Expected rejection"); }
void Fill() { for (int i = 0; i < 1000; i++) server.Rows.Add(FakeTables.Row("other" + i, "other", "catalog", "{}", "other-tenant")); }
Test("Read and update inventory beyond first page", () =>
{
    Fill(); server.Rows.Add(FakeTables.Row("mine", "default", DataKeys.Inventory, "{\"count\":10}"));
    Check(store.ReadProfileDataStrict("default", DataKeys.Inventory)?["count"]?.GetValue<int>() == 10, "Inventory missing");
    store.WriteProfileData("default", DataKeys.Inventory, new JsonObject { ["count"] = 11 });
    Check(server.Rows.Count(r => r["$id"]!.ToString() == "mine") == 1, "Changed row identity");
    Check(store.ReadBackupJson(store.ListBackups().Single().FileName)["count"]!.GetValue<int>() == 10, "Snapshot missing");
});
Test("Profile scans and deletion cover later pages", () =>
{
    Fill(); server.Rows.Add(FakeTables.Row("default", "default", DataKeys.Catalog, "{}")); server.Rows.Add(FakeTables.Row("late", "late", DataKeys.Catalog, "{}")); server.Rows.Add(FakeTables.Row("bak", "late#bak", DataKeys.Catalog, "{}"));
    Check(store.ListProfiles().Any(p => p.Id == "late"), "Profile missing"); store.SwitchProfile("late"); store.SwitchProfile("default");
});
Test("Delete later-page profile and backup", () => { Fill(); server.Rows.Add(FakeTables.Row("default", "default", DataKeys.Catalog, "{}")); server.Rows.Add(FakeTables.Row("late", "late", DataKeys.Catalog, "{}")); server.Rows.Add(FakeTables.Row("bak", "late#bak", DataKeys.Catalog, "{}")); store.DeleteProfile("late"); Check(server.Rows.Count == 1001, "Rows survived deletion"); });
Test("Migration sees source and target beyond first page", () =>
{
    Fill(); server.Rows.Add(FakeTables.Row("old", "p", "catalog", "{\"old\":true}", "old")); server.Rows.Add(FakeTables.Row("new", "p", "catalog", "{\"new\":true}", "new")); server.Rows.Add(FakeTables.Row("second", "q", "catalog", "{}", "old"));
    AppwriteDataStore.MigrateRowsToTenant(server.Options, "old", "new");
    Check(!server.Rows.Any(r => r["userId"]!.ToString() == "old"), "Source rows remain"); Check(server.Rows.Count(r => r["userId"]!.ToString() == "new") == 2, "Missing or duplicate migrated row");
});
Test("Inventory import rejected before any writes", () => { server.Rows.Add(FakeTables.Row("mine", "default", "inventory", "{\"count\":10}")); Reject(() => store.ImportProfileData("default", new Dictionary<string, JsonNode> { [DataKeys.Catalog] = new JsonObject(), [DataKeys.Inventory] = new JsonObject() })); Check(server.Rows.Count == 1 && server.Rows[0]["json"]!.ToString().Contains("10"), "Import modified data"); });
Test("Stale rolling backup names rejected", () => { server.Rows.Add(FakeTables.Row("bak", "default#bak", "inventory", "{\"count\":1}")); var name = store.ListBackups().Single().FileName; Check(store.ReadBackupJson(name)["count"]!.GetValue<int>() == 1, "Valid backup unreadable"); server.Rows[0]["$updatedAt"] = "2026-09-04T01:00:00.000Z"; server.Rows[0]["json"] = "{\"count\":9}"; Reject(() => store.FindBackup(name)); Reject(() => store.ReadBackupJson(name)); Reject(() => store.ReadBackupJson("inventory_not-a-date.json")); });
Test("Profile view stays pinned during editing changes", () =>
{
    server.Rows.Add(FakeTables.Row("a", "default", DataKeys.Inventory, "{\"count\":1}")); server.Rows.Add(FakeTables.Row("b", "second", DataKeys.Inventory, "{\"count\":2}"));
    var pinned = store.ForProfile("default"); store.SwitchProfile("second");
    var backupName = pinned.WriteAtomic(DataKeys.Inventory, new JsonObject { ["count"] = 3 }, "inventory", "test");
    Check(store.ActiveProfileId == "second", "View changed editing selection");
    Check(store.ReadRequired(DataKeys.Inventory)["count"]!.GetValue<int>() == 2, "Wrong inventory changed");
    Check(pinned.ReadRequired(DataKeys.Inventory)["count"]!.GetValue<int>() == 3, "Pinned write missing");
    Check(pinned.ReadBackupJson(pinned.ListBackups().Single().FileName)["count"]!.GetValue<int>() == 1, "Backup targeted wrong profile");
    Check(pinned.ReadBackupJson(backupName!)["count"]!.GetValue<int>() == 1, "Returned backup name does not identify the snapshot");
    store.SwitchProfile("default");
});
Test("Failed snapshot prevents inventory overwrite", () =>
{
    server.Rows.Add(FakeTables.Row("a", "default", DataKeys.Inventory, "{\"count\":1}")); server.RejectBackupWrites = true;
    try
    {
        bool rejected = false;
        try { store.WriteProfileData("default", DataKeys.Inventory, new JsonObject { ["count"] = 2 }); } catch (Appwrite.AppwriteException) { rejected = true; }
        Check(rejected, "Snapshot failure ignored"); Check(store.ReadRequired(DataKeys.Inventory)["count"]!.GetValue<int>() == 1, "Live inventory changed without snapshot");
    }
    finally { server.RejectBackupWrites = false; }
});
var runtimeIndex = Array.IndexOf(args, "--runtime");
if (runtimeIndex >= 0)
{
    if (runtimeIndex + 1 >= args.Length) throw new ArgumentException("--runtime needs the built CircuitOS.dll path.");
    Test("Empty cloud falls back to a usable local server", () => StartupFallback.Run(args[runtimeIndex + 1], server.Options));
}
else Console.WriteLine("SKIP runtime startup integration (pass --runtime <built CircuitOS.dll>)");
Console.WriteLine($"Failures: {failures.Count}");
return failures.Count == 0 ? 0 : 1;

sealed class FakeTables : IDisposable
{
    public List<JsonObject> Rows { get; } = new();
    public AppwriteOptions Options { get; }
    public bool RejectBackupWrites { get; set; }
    readonly HttpListener listener = new(); readonly Task loop;
    public FakeTables() { using var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start(); Options = new($"http://127.0.0.1:{port}/v1", "fake", "fake", "db", "table"); loop = Task.Run(Serve); }
    public static JsonObject Row(string id, string profile, string key, string json, string tenant = "review") => new() { ["$id"] = id, ["$sequence"] = 1, ["$databaseId"] = "db", ["$tableId"] = "table", ["$createdAt"] = "2026-09-04T00:00:00.000Z", ["$updatedAt"] = "2026-09-04T00:00:00.000Z", ["$permissions"] = new JsonArray(), ["userId"] = tenant, ["profileId"] = profile, ["dataKey"] = key, ["json"] = json };
    async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx; try { ctx = await listener.GetContextAsync(); } catch { break; }
            try
            {
                var req = ctx.Request; JsonObject response;
                if (req.HttpMethod == "GET")
                {
                    int limit = 25, offset = 0;
                    foreach (var key in req.QueryString.AllKeys.Where(k => k?.StartsWith("queries") == true)) foreach (var value in req.QueryString.GetValues(key!)!) { var q = JsonNode.Parse(value)!; var method = q["method"]!.ToString(); if (method == "limit") limit = q["values"]![0]!.GetValue<int>(); if (method == "offset") offset = q["values"]![0]!.GetValue<int>(); if (method == "cursorAfter") offset = Rows.FindIndex(r => r["$id"]!.ToString() == q["values"]![0]!.ToString()) + 1; }
                    response = new() { ["total"] = Rows.Count, ["rows"] = new JsonArray(Rows.Skip(offset).Take(limit).Select(r => r.DeepClone()).ToArray()) };
                }
                else
                {
                    using var reader = new StreamReader(req.InputStream); var text = await reader.ReadToEndAsync(); var data = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text)?["data"] as JsonObject;
                    if (RejectBackupWrites && data?["profileId"]?.ToString().EndsWith("#bak") == true) { ctx.Response.StatusCode = 500; ctx.Response.ContentType = "application/json"; await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"message\":\"Simulated backup failure\",\"code\":500,\"type\":\"general_server_error\"}")); ctx.Response.Close(); continue; }
                    if (req.HttpMethod == "POST") { response = Row(Guid.NewGuid().ToString("N"), data!["profileId"]!.ToString(), data["dataKey"]!.ToString(), data["json"]!.ToString(), data["userId"]!.ToString()); Rows.Add(response); }
                    else if (req.HttpMethod == "PATCH") { response = Rows.First(r => r["$id"]!.ToString() == req.Url!.Segments.Last()); foreach (var pair in data!) response[pair.Key] = pair.Value?.DeepClone(); response["$updatedAt"] = DateTimeOffset.UtcNow.ToString("O"); }
                    else { Rows.RemoveAll(r => r["$id"]!.ToString() == req.Url!.Segments.Last()); response = new(); }
                }
                var bytes = Encoding.UTF8.GetBytes(response.ToJsonString()); ctx.Response.ContentType = "application/json"; await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
            }
            catch { ctx.Response.StatusCode = 500; ctx.Response.Close(); }
        }
    }
    public void Dispose() { listener.Stop(); loop.GetAwaiter().GetResult(); listener.Close(); }
}


static class StartupFallback
{
    public static void Run(string runtimePath, AppwriteOptions options)
    {
        runtimePath = Path.GetFullPath(runtimePath);
        if (!File.Exists(runtimePath)) throw new FileNotFoundException("Build the runtime before running its integration test.", runtimePath);
        var repository = new DirectoryInfo(Path.GetDirectoryName(runtimePath)!);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "tools", "runtime", "CircuitOS.Runtime.csproj"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Cannot locate repository for the supplied runtime.");
        var root = Path.Combine(Path.GetTempPath(), "CircuitOS-cloud-startup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? child = null;
        try
        {
            File.Copy(Path.Combine(repository.FullName, "data", "components.json"), Path.Combine(root, "components.json"));
            AppwriteOptions.Save(root, options.Endpoint, options.ProjectId, options.ApiKey, options.DatabaseId, options.CollectionId);
            using var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            // The fixture must never inherit real cloud credentials or a feed override from the host.
            foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("CIRCUITOS_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            foreach (var arg in new[] { runtimePath, "--cloud", "--headless", "--data", root, "--port", port.ToString(), "--ui", Path.Combine(repository.FullName, "tools", "admin"), "--overlay", Path.Combine(repository.FullName, "overlays", "lower-quarter") }) start.ArgumentList.Add(arg);
            child = Process.Start(start) ?? throw new InvalidOperationException("Runtime failed to start.");
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(1) };
            var deadline = Stopwatch.StartNew();
            JsonObject? health = null;
            while (deadline.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (child.HasExited) throw new Exception($"Runtime exited {child.ExitCode}: {errors.GetAwaiter().GetResult()}");
                try { health = JsonNode.Parse(http.GetStringAsync("api/health").GetAwaiter().GetResult()) as JsonObject; break; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                Thread.Sleep(100);
            }
            if (health?["mode"]?.ToString() != "local") throw new Exception("Runtime did not serve health in local mode.");
            if (health["dataPath"]?.ToString().StartsWith(root, StringComparison.OrdinalIgnoreCase) != true) throw new Exception("Response was not from the fixture data directory.");
            if (string.IsNullOrWhiteSpace(health["cloudError"]?.ToString())) throw new Exception("Fallback reason missing from health.");
            var settings = JsonNode.Parse(http.GetStringAsync("api/settings").GetAwaiter().GetResult());
            if (settings?["dataBackend"]?.ToString() != "local" || string.IsNullOrWhiteSpace(settings["cloudError"]?.ToString())) throw new Exception("Settings does not expose local fallback and reason.");
            var config = JsonNode.Parse(http.GetStringAsync("api/config").GetAwaiter().GetResult());
            if (config is not JsonObject) throw new Exception("Local catalog is not usable after fallback.");
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.WaitForExit();
                child.Dispose();
            }
            // This GUID-named directory was created by this test and never contains user data.
            Directory.Delete(root, recursive: true);
        }
    }
}
