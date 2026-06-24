using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

// The portable data contract. Both LocalFileDataStore and (eventually)
// AppwriteDataStore implement this. It must stay free of filesystem-path concepts.
internal interface IDataStore
{
    string ActiveProfileId { get; }

    bool Exists(string key);
    JsonObject ReadRequired(string key);
    JsonObject? TryRead(string key);
    string? WriteAtomic(string key, JsonNode value, string backupLabel, string timestamp);

    DataFileInfo? GetInfo(string key);

    IReadOnlyList<BackupFileEntry> ListBackups();
    BackupFileEntry FindBackup(string fileName);
    JsonObject ReadBackupJson(string fileName);

    void SaveBackground(byte[] bytes, string extension);
    (string FilePath, string ContentType)? FindBackground();

    IReadOnlyList<ProfileInfo> ListProfiles();
    void CreateProfile(string id, string name);
    void SwitchProfile(string id);
    void DeleteProfile(string id);
    void RenameProfile(string id, string name);
    void ImportProfileData(string profileId, IDictionary<string, JsonNode> data);
}

// Local-filesystem extension of IDataStore. The .NET host (WinForms/HttpListener)
// uses this; AppwriteDataStore implements only IDataStore. DataPath/BackupPath are
// filesystem concepts consumed by Streamer.bot path injection and local overlay
// serving — neither exists in the cloud host. See docs/0.7-cloud-foundation.md.
internal interface ILocalDataStore : IDataStore
{
    // Active profile folder; used for Streamer.bot path injection and overlay serving.
    string DataPath { get; }
    // Folder holding timestamped config backups for the active profile.
    string BackupPath { get; }
}

internal static class DataKeys
{
    public const string Catalog = "catalog";
    public const string Boost = "boost";
    public const string Inventory = "inventory";
    public const string Roles = "roles";
    public const string Profile = "profile";
    public const string OverlayConfig = "overlay-config";
}

internal sealed record DataFileInfo(long Size, DateTimeOffset ModifiedAt);
internal sealed record BackupFileEntry(string FileName, string Key, string Label, long Size, DateTimeOffset CreatedAt);
internal sealed record ProfileInfo(string Id, string Name, DateTimeOffset CreatedAt, bool IsActive);
