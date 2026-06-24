using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal static class JsonUtil
{
    public static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions IndentedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static JsonObject Object(JsonNode? node) => node as JsonObject ?? new JsonObject();
    public static JsonArray Array(JsonNode? node) => node as JsonArray ?? new JsonArray();
    public static JsonObject? Object(JsonObject? parent, string key) => parent?[key] as JsonObject;
    public static JsonArray? Array(JsonObject? parent, string key) => parent?[key] as JsonArray;

    public static string String(JsonObject? parent, string key, string fallback = "")
    {
        if (parent?[key] is not JsonValue value) return fallback;
        try { return value.GetValue<string>() ?? fallback; }
        catch { return value.ToString(); }
    }

    public static bool Bool(JsonObject? parent, string key, bool fallback = false)
    {
        if (parent?[key] is not JsonValue value) return fallback;
        try { return value.GetValue<bool>(); }
        catch { return fallback; }
    }

    public static long Long(JsonObject? parent, string key, long fallback = 0)
    {
        if (parent?[key] is not JsonValue value) return fallback;
        try { return value.GetValue<long>(); }
        catch
        {
            return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }

    public static double Double(JsonObject? parent, string key, double fallback = 0)
    {
        if (parent?[key] is not JsonValue value) return fallback;
        try { return value.GetValue<double>(); }
        catch
        {
            return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();
}
