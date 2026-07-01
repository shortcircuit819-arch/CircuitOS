using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CircuitOS.Runtime;

internal sealed class LocalFileDataStore : ILocalDataStore
{
    private static readonly (string Prefix, string Key, string Label)[] ManagedBackups =
    [
        ("components", DataKeys.Catalog, "Components Catalog"),
        ("featured-boost", DataKeys.Boost, "Featured Boost"),
        ("discord-role-awards", DataKeys.Roles, "Discord Role Awards"),
        ("system-profile", DataKeys.Profile, "System Profile"),
    ];

    // Keep at most this many timestamped config backups per file type by default (0 = keep all).
    // Overridable via the backupRetention app setting. Stops config-backups from growing unbounded.
    public const int DefaultBackupRetention = 30;

    private static readonly string[] BackgroundNames = ["bg.png", "bg.jpg", "bg.gif", "bg.webp"];
    private static readonly (string Name, string Mime)[] BackgroundCandidates =
    [
        ("bg.png", "image/png"), ("bg.jpg", "image/jpeg"),
        ("bg.gif", "image/gif"), ("bg.webp", "image/webp")
    ];

    // Root = the top-level data folder (where profiles/ and active-profile live).
    // DataPath = the active profile's subfolder (what the rest of the app uses for file ops).
    private readonly string _rootDataPath;
    private string _activeProfileId;
    private string _profileDataPath;

    public LocalFileDataStore(string dataPath)
    {
        _rootDataPath = Path.GetFullPath(dataPath);
        MigrateIfNeeded();
        _activeProfileId = ReadActiveProfileId();
        _profileDataPath = GetProfilePath(_activeProfileId);
        EnsureProfileFolderExists(_profileDataPath, _activeProfileId);
        BackfillActiveFlags();
    }

    public string DataPath => _profileDataPath;
    public string BackupPath => Path.Combine(_profileDataPath, "config-backups");
    public string ActiveProfileId => _activeProfileId;

    // ── JSON document CRUD ──────────────────────────────────────────────────

    private string KeyToPath(string key) =>
        KeyToProfilePath(_profileDataPath, key)
        ?? throw new InvalidOperationException($"Unknown data key: {key}");

    private static string? KeyToProfilePath(string profileDir, string key) => key switch
    {
        DataKeys.Catalog => Path.Combine(profileDir, "components.json"),
        DataKeys.Boost => Path.Combine(profileDir, "featured-boost.json"),
        DataKeys.Inventory => Path.Combine(profileDir, "inventory.json"),
        DataKeys.Roles => Path.Combine(profileDir, "discord-role-awards.json"),
        DataKeys.Profile => Path.Combine(profileDir, "system-profile.json"),
        DataKeys.OverlayConfig => Path.Combine(profileDir, "overlay-config.json"),
        DataKeys.TwitchRewards => Path.Combine(profileDir, "twitch-rewards.json"),
        _ => null
    };

    public bool Exists(string key) => File.Exists(KeyToPath(key));

    public JsonObject ReadRequired(string key)
    {
        var path = KeyToPath(key);
        if (!File.Exists(path)) throw new FileNotFoundException($"Required configuration file was not found: {path}");
        return ParseFile(path);
    }

    public JsonObject? TryRead(string key)
    {
        var path = KeyToPath(key);
        return File.Exists(path) ? ParseFile(path) : null;
    }

    public string? WriteAtomic(string key, JsonNode value, string backupLabel, string timestamp)
    {
        var path = KeyToPath(key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(BackupPath);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(BackupPath, $"{backupLabel}_{timestamp}.json");
        try
        {
            File.WriteAllText(temporary, value.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
            ParseFile(temporary);
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else { File.Move(temporary, path); backup = null!; }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        // A backup was just created — trim old ones so the folder doesn't grow forever.
        if (backup is not null) PruneBackups(AppSettings.GetInt(_rootDataPath, "backupRetention", DefaultBackupRetention));
        return backup;
    }

    // Trims each managed backup file type to the `keep` most recent (0 = keep all). Only touches
    // recognized managed config backups — never inventory or other files. Best-effort per file.
    internal void PruneBackups(int keep)
    {
        if (keep <= 0) return;
        foreach (var group in ListBackups().GroupBy(entry => entry.Key))
        {
            foreach (var stale in group.OrderByDescending(entry => entry.FileName).Skip(keep))
            {
                try { File.Delete(Path.Combine(BackupPath, stale.FileName)); } catch { }
            }
        }
    }

    public DataFileInfo? GetInfo(string key)
    {
        var path = KeyToPath(key);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        return new DataFileInfo(info.Length, info.LastWriteTimeUtc);
    }

    // ── Backup files ─────────────────────────────────────────────────────────

    public IReadOnlyList<BackupFileEntry> ListBackups()
    {
        if (!Directory.Exists(BackupPath)) return [];
        var entries = new List<BackupFileEntry>();
        foreach (var path in Directory.GetFiles(BackupPath, "*.json"))
        {
            var info = new FileInfo(path);
            if (TryParseBackupFileName(info.Name, info.Length, info.LastWriteTimeUtc) is { } entry)
                entries.Add(entry);
        }
        return entries;
    }

    public BackupFileEntry FindBackup(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new InvalidDataException("Invalid backup filename.");
        var root = Path.GetFullPath(BackupPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(BackupPath, fileName));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup path escaped the managed backup folder.");
        if (!File.Exists(path)) throw new FileNotFoundException("Backup file was not found.");
        var info = new FileInfo(path);
        return TryParseBackupFileName(fileName, info.Length, info.LastWriteTimeUtc)
            ?? throw new InvalidDataException("This file is not a managed configuration backup.");
    }

    public JsonObject ReadBackupJson(string fileName)
    {
        var path = Path.Combine(BackupPath, Path.GetFileName(fileName));
        return ParseFile(path);
    }

    // ── Binary blob (overlay background) ────────────────────────────────────

    public void SaveBackground(byte[] bytes, string extension)
    {
        var overlayDir = Path.Combine(_profileDataPath, "overlay");
        Directory.CreateDirectory(overlayDir);
        foreach (var name in BackgroundNames)
        {
            var old = Path.Combine(overlayDir, name);
            if (File.Exists(old)) File.Delete(old);
        }
        File.WriteAllBytes(Path.Combine(overlayDir, $"bg.{extension}"), bytes);
    }

    public (string FilePath, string ContentType)? FindBackground()
    {
        foreach (var (name, mime) in BackgroundCandidates)
        {
            var filePath = Path.Combine(_profileDataPath, "overlay", name);
            if (File.Exists(filePath)) return (filePath, mime);
        }
        return null;
    }

    // ── Profile management ───────────────────────────────────────────────────

    public IReadOnlyList<ProfileInfo> ListProfiles()
    {
        var profilesDir = Path.Combine(_rootDataPath, "profiles");
        if (!Directory.Exists(profilesDir)) return [];
        var profiles = new List<ProfileInfo>();
        foreach (var dir in Directory.GetDirectories(profilesDir))
        {
            var metaPath = Path.Combine(dir, "profile-meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var meta = ParseFile(metaPath);
                var id = meta["id"]?.ToString() ?? Path.GetFileName(dir);
                var name = meta["name"]?.ToString() ?? id;
                var createdAt = DateTimeOffset.TryParse(meta["createdAt"]?.ToString(), out var dt) ? dt : DateTimeOffset.MinValue;
                var isLive = meta["active"] is JsonValue v && v.TryGetValue<bool>(out var on) && on;
                profiles.Add(new ProfileInfo(id, name, createdAt, id == _activeProfileId, isLive));
            }
            catch { }
        }
        return profiles.OrderBy(p => p.CreatedAt).ToList();
    }

    public void CreateProfile(string id, string name)
    {
        var profileDir = GetProfilePath(id);
        if (Directory.Exists(profileDir)) throw new InvalidOperationException($"Profile '{id}' already exists.");
        Directory.CreateDirectory(profileDir);
        WriteProfileMeta(profileDir, id, name);
    }

    public void SwitchProfile(string id)
    {
        var profileDir = GetProfilePath(id);
        if (!File.Exists(Path.Combine(profileDir, "profile-meta.json")))
            throw new InvalidDataException($"Profile '{id}' does not exist.");
        WriteActiveProfileId(id);
        _activeProfileId = id;
        _profileDataPath = profileDir;
    }

    public void DeleteProfile(string id)
    {
        if (id == _activeProfileId) throw new InvalidOperationException("Cannot delete the active profile.");
        var profileDir = GetProfilePath(id);
        if (!File.Exists(Path.Combine(profileDir, "profile-meta.json")))
            throw new InvalidDataException($"Profile '{id}' does not exist.");
        Directory.Delete(profileDir, recursive: true);
    }

    public void RenameProfile(string id, string name)
    {
        var metaPath = Path.Combine(GetProfilePath(id), "profile-meta.json");
        if (!File.Exists(metaPath)) throw new InvalidDataException($"Profile '{id}' does not exist.");
        var meta = ParseFile(metaPath);
        meta["name"] = name;
        File.WriteAllText(metaPath, meta.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
    }

    public void ImportProfileData(string profileId, IDictionary<string, JsonNode> data)
    {
        var profileDir = GetProfilePath(profileId);
        if (!Directory.Exists(profileDir)) throw new InvalidDataException($"Profile '{profileId}' does not exist.");
        foreach (var (key, value) in data)
        {
            var path = KeyToProfilePath(profileDir, key);
            if (path is null) continue;
            File.WriteAllText(path, value.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
        }
    }

    public void SetProfileActive(string id, bool active)
    {
        var metaPath = Path.Combine(GetProfilePath(id), "profile-meta.json");
        if (!File.Exists(metaPath)) throw new InvalidDataException($"Profile '{id}' does not exist.");
        var meta = ParseFile(metaPath);
        meta["active"] = active;
        File.WriteAllText(metaPath, meta.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
    }

    public JsonObject? ReadProfileData(string profileId, string key)
    {
        var path = KeyToProfilePath(GetProfilePath(profileId), key);
        if (path is null || !File.Exists(path)) return null;
        try { return ParseFile(path); }
        catch { return null; }
    }

    public void WriteProfileData(string profileId, string key, JsonNode value)
    {
        var path = KeyToProfilePath(GetProfilePath(profileId), key) ?? throw new InvalidDataException($"Unsupported profile data key: {key}");
        WriteFileAtomic(path, value, keepRollingBackup: key == DataKeys.Inventory);
    }

    public void WriteOverlayState(string profileId, JsonObject state)
    {
        var overlayDir = Path.Combine(GetProfilePath(profileId), "overlay");
        WriteFileAtomic(Path.Combine(overlayDir, "overlay-state.json"), state, keepRollingBackup: false);
    }

    // Crash-safe write: serialize to a temp file, re-parse to validate, then atomically swap into
    // place (File.Replace on NTFS is atomic). A direct File.WriteAllText can leave a half-written,
    // unparseable file if the process dies mid-write — unacceptable for live inventory. When
    // keepRollingBackup is set, the prior contents are preserved one-deep as <name>.bak so a bad
    // save is recoverable without unbounded per-pull backup growth.
    private static void WriteFileAtomic(string path, JsonNode value, bool keepRollingBackup)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, value.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
            ParseFile(temporary); // validate the bytes on disk before swapping
            if (File.Exists(path) && keepRollingBackup)
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path, overwrite: true); // atomic same-volume swap (MoveFileEx)
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    // One-time backfill so every profile has an explicit `active` flag: pre-feature installs
    // (no flag anywhere) treat the editing-current profile as the live one, matching the old
    // single-active behavior. Idempotent — profiles that already carry a flag are left alone.
    private void BackfillActiveFlags()
    {
        var profilesDir = Path.Combine(_rootDataPath, "profiles");
        if (!Directory.Exists(profilesDir)) return;
        var metas = new List<(string Path, JsonObject Meta, string Id)>();
        foreach (var dir in Directory.GetDirectories(profilesDir))
        {
            var metaPath = Path.Combine(dir, "profile-meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var meta = ParseFile(metaPath);
                metas.Add((metaPath, meta, meta["id"]?.ToString() ?? Path.GetFileName(dir)));
            }
            catch { }
        }
        if (metas.Count == 0 || metas.Any(m => m.Meta["active"] is not null)) return;
        foreach (var (path, meta, id) in metas)
        {
            meta["active"] = id == _activeProfileId;
            File.WriteAllText(path, meta.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private string GetProfilePath(string id) =>
        Path.Combine(_rootDataPath, "profiles", id);

    private string ReadActiveProfileId()
    {
        var file = Path.Combine(_rootDataPath, "active-profile");
        if (!File.Exists(file)) return "default";
        var id = File.ReadAllText(file, Encoding.UTF8).Trim();
        return string.IsNullOrWhiteSpace(id) ? "default" : id;
    }

    private void WriteActiveProfileId(string id) =>
        File.WriteAllText(Path.Combine(_rootDataPath, "active-profile"), id, new UTF8Encoding(false));

    private static void WriteProfileMeta(string dir, string id, string name, bool active = false)
    {
        var meta = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["createdAt"] = DateTime.UtcNow.ToString("O"),
            ["active"] = active
        };
        File.WriteAllText(Path.Combine(dir, "profile-meta.json"), meta.ToJsonString(JsonUtil.IndentedOptions), new UTF8Encoding(false));
    }

    // Creates the default profile folder structure if it doesn't exist yet.
    // Called after migration so a freshly-created default profile always has a meta file.
    private static void EnsureProfileFolderExists(string profilePath, string id)
    {
        if (Directory.Exists(profilePath)) return;
        Directory.CreateDirectory(profilePath);
        WriteProfileMeta(profilePath, id, "Default", active: true);
    }

    // Moves all existing root-level user data into profiles/default/ on first run with 0.5+.
    private void MigrateIfNeeded()
    {
        var profilesDir = Path.Combine(_rootDataPath, "profiles");
        if (Directory.Exists(profilesDir)) return;

        var defaultDir = Path.Combine(profilesDir, "default");
        Directory.CreateDirectory(defaultDir);

        if (File.Exists(Path.Combine(_rootDataPath, "components.json")))
        {
            // Existing installation — read profile name from system-profile.json if available
            var profileName = "Default";
            var profileJsonPath = Path.Combine(_rootDataPath, "system-profile.json");
            if (File.Exists(profileJsonPath))
            {
                try
                {
                    var name = ParseFile(profileJsonPath)["gameName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) profileName = name;
                }
                catch { }
            }
            WriteProfileMeta(defaultDir, "default", profileName, active: true);

            // Move user data files
            foreach (var fileName in new[] {
                "components.json", "featured-boost.json", "inventory.json",
                "discord-role-awards.json", "system-profile.json", "overlay-config.json" })
            {
                var src = Path.Combine(_rootDataPath, fileName);
                if (File.Exists(src)) File.Move(src, Path.Combine(defaultDir, fileName));
            }

            // Move subdirectories
            foreach (var dirName in new[] { "config-backups", "overlay" })
            {
                var src = Path.Combine(_rootDataPath, dirName);
                if (Directory.Exists(src))
                    Directory.Move(src, Path.Combine(defaultDir, dirName));
            }
        }
        else
        {
            // Fresh install — create an empty default profile
            WriteProfileMeta(defaultDir, "default", "Default", active: true);
        }

        WriteActiveProfileId("default");
    }

    private static JsonObject ParseFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException($"Configuration file is empty: {path}");
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException($"Configuration file needs a top-level JSON object: {path}");
    }

    private static BackupFileEntry? TryParseBackupFileName(string fileName, long size, DateTimeOffset createdAt)
    {
        foreach (var (prefix, key, label) in ManagedBackups)
        {
            if (Regex.IsMatch(fileName, $@"^{Regex.Escape(prefix)}_\d{{8}}_\d{{6}}_\d{{3}}\.json$"))
                return new BackupFileEntry(fileName, key, label, size, createdAt);
        }
        return null;
    }
}

