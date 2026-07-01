using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// Connection settings for the Appwrite-backed data store (0.7). Loaded from
// <dataRoot>/appwrite.local.json (gitignored), with CIRCUITOS_APPWRITE_* environment
// variables overriding individual fields. TryLoad returns null when no config is
// present, so the runtime stays on LocalFileDataStore by default. The API key is never
// logged. See docs/0.7-appwrite-dev-setup.md.
internal sealed record AppwriteOptions(
    string Endpoint,
    string ProjectId,
    string ApiKey,
    string DatabaseId,
    string CollectionId)
{
    public const string FileName = "appwrite.local.json";

    // Reads <dataRoot>/appwrite.local.json (if present), then applies CIRCUITOS_APPWRITE_*
    // env overrides. Returns null when neither a config file nor any env var is present
    // (the app then uses the local file store). Throws InvalidDataException when a config
    // exists but is malformed or missing required fields.
    public static AppwriteOptions? TryLoad(string dataRoot)
    {
        string endpoint = "", projectId = "", apiKey = "";
        string databaseId = "circuitos", collectionId = "profile_data";
        var found = false;

        var path = Path.Combine(dataRoot, FileName);
        if (File.Exists(path))
        {
            found = true;
            JsonObject json;
            try
            {
                json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidDataException($"{FileName} must contain a JSON object.");
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) { throw new InvalidDataException($"{FileName} is not valid JSON: {ex.Message}"); }

            endpoint = Read(json, "endpoint", endpoint);
            projectId = Read(json, "projectId", projectId);
            apiKey = Read(json, "apiKey", apiKey);
            databaseId = Read(json, "databaseId", databaseId);
            collectionId = Read(json, "collectionId", collectionId);
        }

        endpoint = Env("CIRCUITOS_APPWRITE_ENDPOINT", endpoint, ref found);
        projectId = Env("CIRCUITOS_APPWRITE_PROJECT_ID", projectId, ref found);
        apiKey = Env("CIRCUITOS_APPWRITE_API_KEY", apiKey, ref found);
        databaseId = Env("CIRCUITOS_APPWRITE_DATABASE_ID", databaseId, ref found);
        collectionId = Env("CIRCUITOS_APPWRITE_COLLECTION_ID", collectionId, ref found);

        if (!found) return null;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(endpoint)) missing.Add("endpoint");
        if (string.IsNullOrWhiteSpace(projectId)) missing.Add("projectId");
        if (string.IsNullOrWhiteSpace(apiKey)) missing.Add("apiKey");
        if (string.IsNullOrWhiteSpace(databaseId)) missing.Add("databaseId");
        if (string.IsNullOrWhiteSpace(collectionId)) missing.Add("collectionId");
        if (missing.Count > 0)
            throw new InvalidDataException(
                $"Appwrite config is missing required field(s): {string.Join(", ", missing)}. See docs/0.7-appwrite-dev-setup.md.");

        return new AppwriteOptions(endpoint, projectId, apiKey, databaseId, collectionId);
    }

    private static string Read(JsonObject json, string key, string fallback)
    {
        var value = json[key]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string Env(string name, string current, ref bool found)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return current;
        found = true;
        return value.Trim();
    }

    // Safe-to-log summary — the API key is reduced to a length, never shown.
    public string Describe() =>
        $"endpoint={Endpoint}, project={ProjectId}, db={DatabaseId}, collection={CollectionId}, " +
        $"apiKey={(string.IsNullOrEmpty(ApiKey) ? "MISSING" : $"set ({ApiKey.Length} chars)")}";

    // Writes appwrite.local.json from a Settings form. The API key is write-only in the UI: a blank
    // apiKey preserves the one already on disk, so editing the endpoint doesn't require re-pasting the
    // key. Database/collection default to the standard names when left blank.
    public static void Save(string dataRoot, string endpoint, string projectId, string apiKey, string databaseId, string collectionId)
    {
        var path = Path.Combine(dataRoot, FileName);
        if (string.IsNullOrWhiteSpace(apiKey) && File.Exists(path))
        {
            try { apiKey = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)?["apiKey"]?.ToString() ?? ""; }
            catch { }
        }
        var json = new JsonObject
        {
            ["endpoint"] = (endpoint ?? "").Trim(),
            ["projectId"] = (projectId ?? "").Trim(),
            ["apiKey"] = (apiKey ?? "").Trim(),
            ["databaseId"] = string.IsNullOrWhiteSpace(databaseId) ? "circuitos" : databaseId.Trim(),
            ["collectionId"] = string.IsNullOrWhiteSpace(collectionId) ? "profile_data" : collectionId.Trim()
        };
        File.WriteAllText(path, json.ToJsonString(JsonUtil.IndentedOptions), new System.Text.UTF8Encoding(false));
    }

    // Non-secret status for the Settings UI: what's configured, minus the API key (only whether one
    // is present). Never returns the key itself.
    public static JsonObject RedactedStatus(string dataRoot)
    {
        var path = Path.Combine(dataRoot, FileName);
        var status = new JsonObject
        {
            ["configured"] = false,
            ["endpoint"] = "",
            ["projectId"] = "",
            ["databaseId"] = "circuitos",
            ["collectionId"] = "profile_data",
            ["hasApiKey"] = false
        };
        if (!File.Exists(path)) return status;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            var endpoint = json["endpoint"]?.ToString() ?? "";
            var projectId = json["projectId"]?.ToString() ?? "";
            var hasKey = !string.IsNullOrWhiteSpace(json["apiKey"]?.ToString());
            status["endpoint"] = endpoint;
            status["projectId"] = projectId;
            status["databaseId"] = string.IsNullOrWhiteSpace(json["databaseId"]?.ToString()) ? "circuitos" : json["databaseId"]!.ToString();
            status["collectionId"] = string.IsNullOrWhiteSpace(json["collectionId"]?.ToString()) ? "profile_data" : json["collectionId"]!.ToString();
            status["hasApiKey"] = hasKey;
            status["configured"] = !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(projectId) && hasKey;
        }
        catch { }
        return status;
    }
}
