using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// App-level (not per-profile, not per-game) settings persisted at the data root in
// circuitos-settings.json. Right now this is just the data backend choice (local vs cloud) so the
// UI can turn cloud mode on without a command-line flag. The chosen mode is read at startup; the
// --cloud flag still forces cloud regardless. Kept tiny and dependency-free on purpose.
internal static class AppSettings
{
    public const string FileName = "circuitos-settings.json";

    // True when the user has chosen the Appwrite cloud backend in Settings.
    public static bool CloudEnabled(string dataRoot)
    {
        var path = Path.Combine(dataRoot, FileName);
        if (!File.Exists(path)) return false;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return string.Equals(json?["dataBackend"]?.ToString(), "cloud", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetCloudEnabled(string dataRoot, bool enabled)
    {
        var path = Path.Combine(dataRoot, FileName);
        var json = new JsonObject { ["dataBackend"] = enabled ? "cloud" : "local" };
        File.WriteAllText(path, json.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
    }
}
