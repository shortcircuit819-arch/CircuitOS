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

    // slot = "" for the global background (bg.<ext>), or a state name (rare/complete/duplicate) for a
    // per-state background (bg-<slot>.<ext>).
    void SaveBackground(byte[] bytes, string extension, string slot);
    (string FilePath, string ContentType)? FindBackground();

    IReadOnlyList<ProfileInfo> ListProfiles();
    void CreateProfile(string id, string name);
    void SwitchProfile(string id);
    void DeleteProfile(string id);
    void RenameProfile(string id, string name);
    void ImportProfileData(string profileId, IDictionary<string, JsonNode> data);

    // The active SET (which profiles are live), independent of the editing selection
    // (ActiveProfileId). SwitchProfile changes what you edit; SetProfileActive changes
    // what runs. ReadProfileData reads one data key from ANY profile (the read counterpart
    // of ImportProfileData) — used for cross-profile command-collision checks.
    void SetProfileActive(string id, bool active);
    JsonObject? ReadProfileData(string profileId, string key);
    void WriteProfileData(string profileId, string key, JsonNode value);
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

    // Atomically writes the OBS overlay state file (profiles/<id>/overlay/overlay-state.json)
    // that overlay.js polls. Display data, written on every pull — no backup is kept. This is a
    // local-filesystem concept (OBS reads it via file://), so it lives on ILocalDataStore even
    // when game data is served from the cloud.
    void WriteOverlayState(string profileId, JsonObject state);
}

internal static class DataKeys
{
    public const string Catalog = "catalog";
    public const string Boost = "boost";
    public const string Inventory = "inventory";
    public const string Roles = "roles";
    public const string Profile = "profile";
    public const string OverlayConfig = "overlay-config";
    public const string TwitchRewards = "twitch-rewards";
}

internal sealed record DataFileInfo(long Size, DateTimeOffset ModifiedAt);
internal sealed record BackupFileEntry(string FileName, string Key, string Label, long Size, DateTimeOffset CreatedAt);
// IsActive = the editing selection (== ActiveProfileId). IsLive = in the active set (runs).
internal sealed record ProfileInfo(string Id, string Name, DateTimeOffset CreatedAt, bool IsActive, bool IsLive);
