using System.Reflection;
using System.Text.Json.Nodes;
using CircuitOS.Runtime;

internal static class ReviewRegressionTests
{
    public static void Run()
    {
        var failures = new List<string>();
        Check("Portable data discovery survives profile migration", PortableDataDiscovery, failures);
        Check("Update restart preserves effective app options", RestartOptions, failures);
        foreach (var operation in new[] { "remove", "reset", "restore" })
            Check("Profile switch cannot redirect " + operation, () => InventoryTargetStaysPinned(operation), failures);
        foreach (var damage in new[] { "missing", "corrupt" })
            Check("Recover " + damage + " profile metadata", () => RecoverMetadata(damage), failures);
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }

    private static void Check(string name, Action test, List<string> failures)
    {
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL: " + failures[^1]); }
    }

    private static JsonObject Inventory(int count) => new()
    {
        ["viewer"] = new JsonObject { ["components"] = new JsonObject { ["item"] = count } }
    };

    private static void PortableDataDiscovery()
    {
        using var fixture = new Fixture();
        var data = Path.Combine(fixture.Root, "portable", "Data");
        var ui = Path.Combine(fixture.Root, "portable", "App");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(ui);
        File.WriteAllText(Path.Combine(ui, "index.html"), "test");
        File.WriteAllText(Path.Combine(data, "components.json"), "{}");
        var args = new[] { "--ui", ui };
        Require(RuntimeOptions.Parse(args).DataPath == data, "Initial portable data root should be detected.");
        var portable = new LocalFileDataStore(data);
        portable.CreateProfile("custom", "Custom");
        portable.WriteProfileData("custom", DataKeys.Catalog, new JsonObject());
        portable.SwitchProfile("custom");
        Require(RuntimeOptions.Parse(args).DataPath == data, "Second launch must use the migrated portable data root.");
        Require(new LocalFileDataStore(RuntimeOptions.Parse(args).DataPath).ActiveProfileId == "custom", "Selected portable profile must survive relaunch.");
    }

    private static void RestartOptions()
    {
        var options = new RuntimeOptions(Path.GetFullPath("custom data"), Path.GetFullPath("custom ui"), Path.GetFullPath("custom overlay"), 9123, true);
        var restarted = RuntimeOptions.Parse(options.GetRestartArguments(cloud: true));
        Require(restarted == options, "Restart must preserve custom data/UI/overlay locations, port, and headless mode.");
        Require(options.GetRestartArguments(cloud: true).Contains("--cloud"), "Explicit cloud override must survive restart.");
        Require(!options.GetRestartArguments(cloud: false).Contains("--cloud"), "Persisted backend choice must remain changeable when no override was used.");
    }

    private static void InventoryTargetStaysPinned(string operation)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var service = new CircuitService(store);
        store.CreateProfile("second", "Second");
        store.WriteProfileData("default", DataKeys.Inventory, Inventory(1));
        var backup = store.WriteAtomic(DataKeys.Inventory, Inventory(2), "inventory", "20260904_120000_000")!;
        store.WriteProfileData("second", DataKeys.Inventory, Inventory(90));
        var gate = typeof(CircuitService).GetMethod("InventoryLock", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { "default" })!;
        ServiceResult? result = null;
        Exception? workerError = null;
        var thread = new Thread(() =>
        {
            try
            {
                var request = new JsonObject { ["viewerId"] = "viewer", ["itemId"] = "item", ["operation"] = "restore", ["fileName"] = Path.GetFileName(backup) };
                result = operation switch
                {
                    "remove" => service.RemoveInventoryItem(request),
                    "reset" => service.ResetViewer(request),
                    _ => service.InvokeBackupOperation(request)
                };
            }
            catch (Exception ex) { workerError = ex; }
        });
        lock (gate)
        {
            thread.Start();
            if (!SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000))
                throw new Exception("Inventory operation did not reach the lock.");
            store.SwitchProfile("second");
        }
        if (!thread.Join(5000)) throw new Exception("Inventory operation did not finish.");
        if (workerError is not null) throw workerError;
        Require(result?.Status == 200, "Inventory operation should succeed.");
        var first = store.ReadProfileDataStrict("default", DataKeys.Inventory)!;
        var second = store.ReadProfileDataStrict("second", DataKeys.Inventory)!;
        Require(JsonNode.DeepEquals(second, Inventory(90)), "The newly selected profile was modified.");
        Require(operation switch
        {
            "remove" => first["viewer"]?["components"]?["item"] is null,
            "reset" => first["viewer"] is null,
            _ => JsonNode.DeepEquals(first, Inventory(1))
        }, "The original profile did not receive the intended edit.");
        var backups = Directory.GetFiles(Path.Combine(fixture.Root, "profiles", "default", "config-backups"), "inventory_*.json");
        Require(backups.Any(path => JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), Inventory(2))), "The pre-edit inventory must be backed up in its own profile.");
    }

    private static void RecoverMetadata(string damage)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        store.CreateProfile("recover", "Recover");
        store.WriteProfileData("recover", DataKeys.Inventory, Inventory(7));
        var meta = Path.Combine(fixture.Root, "profiles", "recover", "profile-meta.json");
        if (damage == "missing") File.Delete(meta); else File.WriteAllText(meta, "{broken");
        var service = new CircuitService(store);
        Require(store.ListProfiles().Any(p => p.Id == "recover"), "Data-bearing profile should be listed.");
        var renamed = service.InvokeProfileOperation(new JsonObject { ["operation"] = "rename", ["id"] = "recover", ["name"] = "Restored" });
        Require(renamed.Status == 200, "Rename should repair damaged metadata: " + renamed.Body);
        // Damage it again to exercise switching independently of rename.
        if (damage == "missing") File.Delete(meta); else File.WriteAllText(meta, "{broken");
        var switched = service.InvokeProfileOperation(new JsonObject { ["operation"] = "switch", ["id"] = "recover" });
        Require(switched.Status == 200, "Switch should repair damaged metadata: " + switched.Body);
        Require(JsonNode.DeepEquals(store.ReadRequired(DataKeys.Inventory), Inventory(7)), "Recovery must preserve inventory.");
        Require(JsonNode.Parse(File.ReadAllText(meta))?["active"]?.GetValue<bool>() == false, "Recovered profile must not automatically go live.");
        Require(service.InvokeProfileOperation(new JsonObject { ["operation"] = "switch", ["id"] = "does-not-exist" }).Status != 200, "Missing empty profile must not be invented.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "CircuitOS-regression-" + Guid.NewGuid().ToString("N"));
        public LocalFileDataStore Store { get; }
        public Fixture() => Store = new LocalFileDataStore(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
