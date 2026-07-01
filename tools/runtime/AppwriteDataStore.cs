using System.Text;
using System.Text.Json.Nodes;
using Appwrite;
using Appwrite.Models;
using Appwrite.Services;

namespace CircuitOS.Runtime;

// Cloud-backed data store (0.7), model A: one row per (userId, profileId, dataKey)
// in the `profile_data` table, with the file's JSON in the `json` column. The local
// Windows app keeps using LocalFileDataStore; AppwriteDataStore is the parallel cloud
// implementation, selected at runtime by config. See docs/0.7-cloud-foundation.md.
//
// The sync IDataStore methods block on the async Appwrite SDK — acceptable for the
// parallel-desktop scenario; the hosted backend can revisit async later.
//
// Implemented: core data ops, tenant-scoped profile management, and a single rolling
// recovery point per managed file (stored under a "#bak" profile namespace — no extra
// table needed). Pending: full timestamped backup history (dedicated table) and the
// overlay-background Storage bucket.
internal sealed class AppwriteDataStore : IDataStore
{
    // Reserved data key: the per-profile metadata row ({ name, createdAt } in `json`).
    private const string ProfileMetaKey = "__profile_meta__";

    // The four files that get recovery points, mirroring LocalFileDataStore.
    private static readonly (string Prefix, string Key, string Label)[] ManagedBackups =
    [
        ("components", DataKeys.Catalog, "Components Catalog"),
        ("featured-boost", DataKeys.Boost, "Featured Boost"),
        ("discord-role-awards", DataKeys.Roles, "Discord Role Awards"),
        ("system-profile", DataKeys.Profile, "System Profile"),
    ];

    private readonly AppwriteOptions _options;
    private readonly string _userId;
    private string _profileId;            // mutable: SwitchProfile retargets this instance
    private readonly TablesDB _tables;

    public AppwriteDataStore(AppwriteOptions options, string userId, string profileId)
    {
        _options = options;
        _userId = string.IsNullOrWhiteSpace(userId) ? throw new ArgumentException("userId is required.", nameof(userId)) : userId;
        _profileId = string.IsNullOrWhiteSpace(profileId) ? throw new ArgumentException("profileId is required.", nameof(profileId)) : profileId;
        var client = new Client()
            .SetEndpoint(options.Endpoint)
            .SetProject(options.ProjectId)
            .SetKey(options.ApiKey);
        _tables = new TablesDB(client);
    }

    public string ActiveProfileId => _profileId;

    // The backup namespace holds one prior-version snapshot per data key for this profile.
    private string BackupProfileId => _profileId + "#bak";

    // ── Core data ops ──────────────────────────────────────────────────────────

    public bool Exists(string key) => TryGetRow(_profileId, key) is not null;

    public JsonObject? TryRead(string key)
    {
        var row = TryGetRow(_profileId, key);
        return row is null ? null : ParseJsonColumn(row, key);
    }

    public JsonObject ReadRequired(string key)
    {
        var row = TryGetRow(_profileId, key)
            ?? throw new FileNotFoundException($"Required configuration '{key}' was not found in Appwrite.");
        return ParseJsonColumn(row, key);
    }

    public string? WriteAtomic(string key, JsonNode value, string backupLabel, string timestamp)
    {
        // Snapshot the current version into the backup namespace before overwriting,
        // then upsert the live row. Mirrors the local store's "backup-then-replace".
        string? backupFileName = null;
        var existing = TryGetRow(_profileId, key);
        if (existing is not null)
        {
            var prior = JsonColumn(existing);
            if (!string.IsNullOrWhiteSpace(prior))
            {
                UpsertJson(BackupProfileId, key, prior);
                if (ManagedBackups.Any(m => m.Key == key))
                    backupFileName = $"{backupLabel}_{timestamp}.json";
            }
        }
        UpsertJson(_profileId, key, value.ToJsonString(JsonUtil.IndentedOptions));
        return backupFileName;
    }

    public DataFileInfo? GetInfo(string key)
    {
        var row = TryGetRow(_profileId, key);
        if (row is null) return null;
        var json = JsonColumn(row) ?? "";
        var modified = ParseUpdatedAt(row);
        return new DataFileInfo(Encoding.UTF8.GetByteCount(json), modified);
    }

    // ── Round-trip self-test (the --appwrite-roundtrip diagnostic) ─────────────
    public string RoundTripSelfTest()
    {
        const string key = "__roundtrip_test__";
        var marker = Guid.NewGuid().ToString("N");
        WriteAtomic(key, new JsonObject { ["ok"] = true, ["marker"] = marker }, "roundtrip", "0");
        var read = TryRead(key) ?? throw new InvalidOperationException("Round-trip read returned null after write.");
        if (read["marker"]?.ToString() != marker)
            throw new InvalidOperationException("Round-trip read did not match the written marker.");
        TryDelete(_profileId, key);
        if (TryRead(key) is not null)
            throw new InvalidOperationException("Round-trip row still present after delete.");
        return $"write, read, verify, delete all OK (marker {marker[..8]}).";
    }

    // ── Backups (single rolling recovery point per managed file) ────────────────

    public IReadOnlyList<BackupFileEntry> ListBackups()
    {
        var entries = new List<BackupFileEntry>();
        foreach (var (prefix, key, label) in ManagedBackups)
        {
            var row = TryGetRow(BackupProfileId, key);
            if (row is null) continue;
            var json = JsonColumn(row) ?? "";
            var created = ParseUpdatedAt(row);
            var fileName = $"{prefix}_{created.UtcDateTime:yyyyMMdd_HHmmss_fff}.json";
            entries.Add(new BackupFileEntry(fileName, key, label, Encoding.UTF8.GetByteCount(json), created));
        }
        return entries;
    }

    public BackupFileEntry FindBackup(string fileName)
    {
        var (key, label, prefix) = ResolveBackupFile(fileName);
        var row = TryGetRow(BackupProfileId, key)
            ?? throw new FileNotFoundException("Backup was not found.");
        var json = JsonColumn(row) ?? "";
        return new BackupFileEntry(fileName, key, label, Encoding.UTF8.GetByteCount(json), ParseUpdatedAt(row));
    }

    public JsonObject ReadBackupJson(string fileName)
    {
        var (key, _, _) = ResolveBackupFile(fileName);
        var row = TryGetRow(BackupProfileId, key)
            ?? throw new FileNotFoundException("Backup was not found.");
        return ParseJsonColumn(row, key);
    }

    public static void MigrateRowsToTenant(AppwriteOptions options, string fromUserId, string toUserId)
    {
        if (string.IsNullOrWhiteSpace(fromUserId) || string.IsNullOrWhiteSpace(toUserId))
            throw new ArgumentException("Both fromUserId and toUserId are required.");
        if (string.Equals(fromUserId, toUserId, StringComparison.Ordinal)) return;

        var client = new Client()
            .SetEndpoint(options.Endpoint)
            .SetProject(options.ProjectId)
            .SetKey(options.ApiKey);
        var tables = new TablesDB(client);
        var rows = Run(tables.ListRows(options.DatabaseId, options.CollectionId, new List<string> { Query.Limit(1000) })).Rows
            .Where(row => string.Equals(RowUserId(row), fromUserId, StringComparison.Ordinal))
            .ToList();

        foreach (var row in rows)
        {
            var profileId = RowProfileId(row);
            var dataKey = RowDataKey(row);
            var json = JsonColumn(row);
            if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(dataKey) || string.IsNullOrWhiteSpace(json))
                continue;

            var existingTarget = FindRow(tables, options, toUserId, profileId, dataKey);
            if (existingTarget is not null)
            {
                Run(tables.DeleteRow(options.DatabaseId, options.CollectionId, row.Id));
                continue;
            }

            var data = new Dictionary<string, object>
            {
                ["userId"] = toUserId,
                ["profileId"] = profileId,
                ["dataKey"] = dataKey,
                ["json"] = json
            };
            Run(tables.CreateRow(options.DatabaseId, options.CollectionId, ID.Unique(), data));
            Run(tables.DeleteRow(options.DatabaseId, options.CollectionId, row.Id));
        }
    }

    // Maps a backup file name ("{prefix}_{timestamp}.json") back to its managed entry.
    private static (string Key, string Label, string Prefix) ResolveBackupFile(string fileName)
    {
        foreach (var (prefix, key, label) in ManagedBackups)
            if (fileName.StartsWith(prefix + "_", StringComparison.Ordinal) && fileName.EndsWith(".json", StringComparison.Ordinal))
                return (key, label, prefix);
        throw new InvalidDataException("This file is not a managed configuration backup.");
    }

    // Backup self-test: write v1, overwrite with v2 (snapshots v1), confirm the backup
    // holds v1 and the live row holds v2, then clean both up. Uses a throwaway key.
    public string BackupSelfTest()
    {
        const string key = "__bak_selftest__";
        var v1 = Guid.NewGuid().ToString("N");
        var v2 = Guid.NewGuid().ToString("N");
        try
        {
            WriteAtomic(key, new JsonObject { ["v"] = v1 }, "components", "0");
            WriteAtomic(key, new JsonObject { ["v"] = v2 }, "components", "0");
            var bak = TryGetRow(BackupProfileId, key) ?? throw new InvalidOperationException("No backup row created on overwrite.");
            if (ParseJsonColumn(bak, key)["v"]?.ToString() != v1)
                throw new InvalidOperationException("Backup did not capture the prior version.");
            if (TryRead(key)?["v"]?.ToString() != v2)
                throw new InvalidOperationException("Live row is not the latest version.");
            return "write v1, overwrite with v2, prior version captured in backup, live is v2.";
        }
        finally
        {
            TryDelete(_profileId, key);
            TryDelete(BackupProfileId, key);
        }
    }

    // ── Profile management ──────────────────────────────────────────────────────

    public IReadOnlyList<ProfileInfo> ListProfiles()
    {
        var rows = AllRowsForTenant();
        var byProfile = new Dictionary<string, (string Name, DateTimeOffset Created, bool Live)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var pid = RowProfileId(row);
            if (string.IsNullOrEmpty(pid) || pid.Contains('#')) continue;   // skip the #bak namespace
            if (!byProfile.ContainsKey(pid)) byProfile[pid] = (pid, DateTimeOffset.MinValue, pid == _profileId);
            if (RowDataKey(row) == ProfileMetaKey)
            {
                var meta = ParseJsonColumnOrNull(row);
                var name = meta?["name"]?.ToString();
                var created = DateTimeOffset.TryParse(meta?["createdAt"]?.ToString(), out var dt) ? dt : DateTimeOffset.MinValue;
                var live = meta?["active"] is JsonValue v && v.TryGetValue<bool>(out var on) ? on : pid == _profileId;
                byProfile[pid] = (string.IsNullOrWhiteSpace(name) ? pid : name!, created, live);
            }
        }
        return byProfile
            .Select(kv => new ProfileInfo(kv.Key, kv.Value.Name, kv.Value.Created, kv.Key == _profileId, kv.Value.Live))
            .OrderBy(p => p.CreatedAt)
            .ToList();
    }

    public void CreateProfile(string id, string name)
    {
        if (AllRowsForTenant().Any(r => RowProfileId(r) == id))
            throw new InvalidOperationException($"Profile '{id}' already exists.");
        WriteProfileMeta(id, name);
    }

    public void RenameProfile(string id, string name) => WriteProfileMeta(id, name);

    public void SwitchProfile(string id)
    {
        if (!AllRowsForTenant().Any(r => RowProfileId(r) == id))
            throw new InvalidDataException($"Profile '{id}' does not exist.");
        _profileId = id;
    }

    public void DeleteProfile(string id)
    {
        if (id == _profileId) throw new InvalidOperationException("Cannot delete the active profile.");
        var bak = id + "#bak";
        foreach (var row in AllRowsForTenant().Where(r => RowProfileId(r) == id || RowProfileId(r) == bak))
            Run(_tables.DeleteRow(_options.DatabaseId, _options.CollectionId, row.Id));
    }

    public void ImportProfileData(string profileId, IDictionary<string, JsonNode> data)
    {
        foreach (var (key, value) in data)
            UpsertJson(profileId, key, value.ToJsonString(JsonUtil.IndentedOptions));
    }

    public void SetProfileActive(string id, bool active)
    {
        var existing = TryGetRow(id, ProfileMetaKey);
        var meta = existing is not null
            ? ParseJsonColumn(existing, ProfileMetaKey)
            : new JsonObject { ["name"] = id, ["createdAt"] = DateTime.UtcNow.ToString("O") };
        meta["active"] = active;
        UpsertJson(id, ProfileMetaKey, meta.ToJsonString(JsonUtil.IndentedOptions));
    }

    public JsonObject? ReadProfileData(string profileId, string key)
    {
        var row = TryGetRow(profileId, key);
        return row is null ? null : ParseJsonColumn(row, key);
    }

    public void WriteProfileData(string profileId, string key, JsonNode value)
    {
        UpsertJson(profileId, key, value.ToJsonString(JsonUtil.IndentedOptions));
    }
    private void WriteProfileMeta(string profileId, string name)
    {
        // Preserve createdAt + active across rename/create.
        var existing = TryGetRow(profileId, ProfileMetaKey);
        var meta = existing is not null ? ParseJsonColumn(existing, ProfileMetaKey) : new JsonObject();
        meta["name"] = name;
        if (meta["createdAt"] is null) meta["createdAt"] = DateTime.UtcNow.ToString("O");
        UpsertJson(profileId, ProfileMetaKey, meta.ToJsonString(JsonUtil.IndentedOptions));
    }

    // ── Pending Phase 2b: overlay-background Storage ────────────────────────────
    public (string FilePath, string ContentType)? FindBackground() => null;            // → Storage URL
    public void SaveBackground(byte[] bytes, string extension, string slot) =>
        throw new NotImplementedException("Overlay-background storage needs an Appwrite Storage bucket (Phase 2b).");

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void UpsertJson(string profileId, string key, string json)
    {
        _ = JsonNode.Parse(json); // validate before persisting
        var data = new Dictionary<string, object>
        {
            ["userId"] = _userId,
            ["profileId"] = profileId,
            ["dataKey"] = key,
            ["json"] = json
        };
        // Address the row by its real $id resolved through the unique index, never by a
        // derived id: update in place when it exists, create with a fresh id otherwise.
        var existing = TryGetRow(profileId, key);
        if (existing is not null)
            Run(_tables.UpdateRow(_options.DatabaseId, _options.CollectionId, existing.Id, data));
        else
            Run(_tables.CreateRow(_options.DatabaseId, _options.CollectionId, ID.Unique(), data));
    }

    // Resolve a row by the unique index (userId, profileId, dataKey) and return it with
    // its real $id. Returns null when absent. Replaces the old derived-id GetRow, which
    // desynced when a stored row's $id didn't match the recomputed hash (re-push churn,
    // tenant swaps): UpsertRow(derivedId) would resolve the unique-index conflict against
    // the existing row and update ITS id, so the follow-up GetRow(derivedId) 404'd.
    private Row? TryGetRow(string profileId, string key)
    {
        // The Appwrite cloud Tables API is rejecting the query-string form in this environment,
        // so list rows once and filter client-side. This preserves the same semantics without
        // depending on the server's query parser.
        return AllRowsForTenant()
            .FirstOrDefault(row =>
                string.Equals(RowUserId(row), _userId, StringComparison.Ordinal) &&
                string.Equals(RowProfileId(row), profileId, StringComparison.Ordinal) &&
                string.Equals(RowDataKey(row), key, StringComparison.Ordinal));
    }

    private void TryDelete(string profileId, string key)
    {
        var row = TryGetRow(profileId, key);
        if (row is not null)
            Run(_tables.DeleteRow(_options.DatabaseId, _options.CollectionId, row.Id));
    }

    private List<Row> AllRowsForTenant()
    {
        // Avoid server-side query filters here; the cloud tables endpoint is rejecting them
        // with an invalid-query error in this environment. We fetch the tenant's rows once and
        // apply the necessary filters in memory instead.
        return Run(_tables.ListRows(_options.DatabaseId, _options.CollectionId, new List<string> { Query.Limit(1000) })).Rows
            .Where(row => string.Equals(RowUserId(row), _userId, StringComparison.Ordinal))
            .ToList();
    }

    private static Row? FindRow(TablesDB tables, AppwriteOptions options, string userId, string profileId, string dataKey)
    {
        var rows = Run(tables.ListRows(options.DatabaseId, options.CollectionId, new List<string> { Query.Limit(1000) })).Rows
            .Where(row => string.Equals(RowUserId(row), userId, StringComparison.Ordinal) &&
                          string.Equals(RowProfileId(row), profileId, StringComparison.Ordinal) &&
                          string.Equals(RowDataKey(row), dataKey, StringComparison.Ordinal))
            .ToList();
        return rows.FirstOrDefault();
    }

    private static string? RowUserId(Row row) => row.Data.TryGetValue("userId", out var v) ? v?.ToString() : null;
    private static string? RowProfileId(Row row) => row.Data.TryGetValue("profileId", out var v) ? v?.ToString() : null;
    private static string? RowDataKey(Row row) => row.Data.TryGetValue("dataKey", out var v) ? v?.ToString() : null;
    private static string? JsonColumn(Row row) => row.Data.TryGetValue("json", out var value) ? value?.ToString() : null;
    private static DateTimeOffset ParseUpdatedAt(Row row) => DateTimeOffset.TryParse(row.UpdatedAt, out var dt) ? dt : DateTimeOffset.UtcNow;

    private static JsonObject? ParseJsonColumnOrNull(Row row)
    {
        var json = JsonColumn(row);
        return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
    }

    private static JsonObject ParseJsonColumn(Row row, string key)
    {
        var json = JsonColumn(row);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"Appwrite row for '{key}' has an empty json column.");
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException($"Appwrite row for '{key}' is not a JSON object.");
    }

    private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();
}

