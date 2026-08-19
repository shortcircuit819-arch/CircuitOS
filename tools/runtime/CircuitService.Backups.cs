using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed partial class CircuitService
{
    public JsonObject GetBackupCenter()
    {
        var targetDefs = new[]
        {
            (DataKeys.Catalog, "Components Catalog"),
            (DataKeys.Boost, "Featured Boost"),
            (DataKeys.Roles, "Discord Role Awards"),
            (DataKeys.Profile, "System Profile"),
            (DataKeys.Inventory, "Viewer Inventory"),
        };
        var liveFiles = new JsonArray();
        foreach (var (key, label) in targetDefs)
        {
            var info = _store.GetInfo(key);
            liveFiles.Add(new JsonObject
            {
                ["key"] = key, ["label"] = label, ["exists"] = info is not null,
                ["size"] = info?.Size ?? 0, ["modifiedAtUtc"] = info?.ModifiedAt.ToString("O") ?? ""
            });
        }

        var backupEntries = _store.ListBackups();
        var rows = backupEntries.Select(entry => new JsonObject
        {
            ["fileName"] = entry.FileName, ["targetKey"] = entry.Key, ["targetLabel"] = entry.Label,
            ["size"] = entry.Size, ["createdAtUtc"] = entry.CreatedAt.ToString("O")
        }).ToList();
        var backups = new JsonArray(rows.OrderByDescending(row => ParseDate(JsonUtil.String(row, "createdAtUtc"))).Select(row => row.DeepClone()).ToArray());
        return new JsonObject
        {
            ["liveFiles"] = liveFiles, ["backups"] = backups, ["backupPath"] = DisplayBackupPath,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    public ServiceResult InvokeBackupOperation(JsonObject request)
    {
        var operation = JsonUtil.String(request, "operation");
        BackupFileEntry entry;
        try { entry = _store.FindBackup(JsonUtil.String(request, "fileName")); }
        catch (Exception exception) { return Error([exception.Message]); }

        JsonObject content;
        try { content = _store.ReadBackupJson(entry.FileName); }
        catch (Exception exception)
        {
            var message = $"Backup JSON could not be parsed: {exception.Message}";
            if (operation != "preview") return Error([message]);
            return Ok(new JsonObject
            {
                ["ok"] = true, ["file"] = BackupFileObject(entry), ["content"] = null,
                ["liveContent"] = _store.TryRead(entry.Key),
                ["validationErrors"] = ToJsonArray([message])
            });
        }

        var errors = ValidateBackup(entry, content);
        if (operation == "preview")
        {
            return Ok(new JsonObject
            {
                ["ok"] = true, ["file"] = BackupFileObject(entry), ["content"] = content,
                ["liveContent"] = _store.TryRead(entry.Key),
                ["validationErrors"] = ToJsonArray(errors)
            });
        }
        if (operation != "restore") return Error(["Unknown backup operation."]);
        if (errors.Count > 0) return Error(errors);
        // Restoring inventory takes the same per-profile lock as a live pull, so a restore and a
        // redemption landing together can't interleave.
        string? preRestore;
        if (entry.Key == DataKeys.Inventory)
            lock (InventoryLock(_store.ActiveProfileId))
                preRestore = _store.WriteAtomic(entry.Key, content, BackupLabelFromKey(entry.Key), Timestamp());
        else
            preRestore = _store.WriteAtomic(entry.Key, content, BackupLabelFromKey(entry.Key), Timestamp());
        return Ok(new JsonObject
        {
            ["ok"] = true, ["restoredFile"] = entry.FileName, ["target"] = entry.Label,
            ["restoredAtUtc"] = DateTime.UtcNow.ToString("O"), ["preRestoreBackup"] = preRestore
        });
    }

    private static string BackupLabelFromKey(string key) => key switch
    {
        DataKeys.Catalog => "components",
        DataKeys.Boost => "featured-boost",
        DataKeys.Roles => "discord-role-awards",
        DataKeys.Profile => "system-profile",
        DataKeys.Inventory => "inventory",
        _ => key
    };

    private static JsonObject BackupFileObject(BackupFileEntry entry) => new()
    {
        ["fileName"] = entry.FileName, ["targetKey"] = entry.Key,
        ["targetLabel"] = entry.Label, ["size"] = entry.Size,
        ["createdAtUtc"] = entry.CreatedAt.ToString("O")
    };

    private static List<string> ValidateRoleState(JsonObject state)
    {
        var errors = new List<string>();
        if (state["roleNames"] is not JsonObject) errors.Add("Role award state needs roleNames.");
        if (state["fulfilled"] is not JsonObject) errors.Add("Role award state needs fulfilled acknowledgements.");
        return errors;
    }

    private List<string> ValidateBackup(BackupFileEntry entry, JsonObject content) => entry.Key switch
    {
        DataKeys.Catalog => ValidateConfiguration(content, _store.TryRead(DataKeys.Boost) ?? DefaultBoost()),
        DataKeys.Boost => ValidateConfiguration(_store.ReadRequired(DataKeys.Catalog), content),
        DataKeys.Roles => ValidateRoleState(content),
        DataKeys.Profile => ValidateProfile(content),
        DataKeys.Inventory => ValidateInventory(content),
        _ => ["Unknown backup target."]
    };

    // Inventory is a map of viewerId -> viewer object. Light shape check so a restore can't drop a
    // structurally broken document over live collections.
    private static List<string> ValidateInventory(JsonObject content)
    {
        var errors = new List<string>();
        foreach (var (viewerId, node) in content)
        {
            if (node is not JsonObject)
            {
                errors.Add($"Inventory entry '{viewerId}' is not a viewer object.");
                break;
            }
        }
        return errors;
    }
}
