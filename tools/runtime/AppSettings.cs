using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// App-level (not per-profile, not per-game) settings persisted at the data root in
// circuitos-settings.json. A tiny key/value JSON store so future Settings-page options
// (backup retention, start-with-Windows, update channel, …) get a home without new files.
// Fault-tolerant: a missing file or bad value returns the fallback; Set preserves other keys.
internal static class AppSettings
{
    public const string FileName = "circuitos-settings.json";
    public const string DataBackendKey = "dataBackend";

    private static string PathFor(string dataRoot) => Path.Combine(dataRoot, FileName);

    private static JsonObject Load(string dataRoot)
    {
        var path = PathFor(dataRoot);
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    private static void Save(string dataRoot, JsonObject json) =>
        File.WriteAllText(PathFor(dataRoot), json.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));

    // ── Generic accessors ──────────────────────────────────────────────────────
    public static string GetString(string dataRoot, string key, string fallback = "") =>
        Load(dataRoot)[key]?.ToString() is { Length: > 0 } value ? value : fallback;

    public static bool GetBool(string dataRoot, string key, bool fallback = false)
    {
        if (Load(dataRoot)[key] is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (bool.TryParse(v.ToString(), out var parsed)) return parsed;
        }
        return fallback;
    }

    public static int GetInt(string dataRoot, string key, int fallback)
    {
        if (Load(dataRoot)[key] is JsonValue v && int.TryParse(v.ToString(), out var i)) return i;
        return fallback;
    }

    // Sets one key, preserving every other key already in the file.
    public static void Set(string dataRoot, string key, JsonNode? value)
    {
        var json = Load(dataRoot);
        json[key] = value;
        Save(dataRoot, json);
    }

    // ── Backend choice (unchanged behavior; used by startup + the Settings page) ──
    public static bool CloudEnabled(string dataRoot) =>
        string.Equals(GetString(dataRoot, DataBackendKey), "cloud", StringComparison.OrdinalIgnoreCase);

    public static void SetCloudEnabled(string dataRoot, bool enabled) =>
        Set(dataRoot, DataBackendKey, enabled ? "cloud" : "local");
}
