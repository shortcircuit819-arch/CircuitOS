using System.Text;
using System.Text.Json.Nodes;

namespace CircuitOS.Runtime;

internal sealed partial class CircuitService
{
    public JsonObject GetOverlayConfig()
    {
        var config = _store.TryRead(DataKeys.OverlayConfig);
        if (config is null && _localStore is not null)
        {
            var templatePath = Path.Combine(_localStore.DataPath, "overlay-config.template.json");
            config = ReadLocalJsonFile(templatePath);
        }
        config ??= new JsonObject();
        return new JsonObject { ["ok"] = true, ["isConfigured"] = _store.Exists(DataKeys.OverlayConfig), ["config"] = config };
    }

    public ServiceResult SaveOverlayConfig(JsonObject request)
    {
        var config = request["config"] as JsonObject;
        if (config is null || JsonUtil.Long(config, "schemaVersion") != 1)
            return Error(["Overlay config schemaVersion must be 1."]);
        var backup = _store.WriteAtomic(DataKeys.OverlayConfig, config, "overlay-config", Timestamp());
        return Ok(new JsonObject { ["ok"] = true, ["backup"] = backup, ["config"] = JsonUtil.Clone(config) });
    }

    public ServiceResult SaveOverlayBackground(byte[] bytes, string contentType, string state)
    {
        var mimeToExt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = "png", ["image/jpeg"] = "jpg",
            ["image/jpg"] = "jpg", ["image/gif"] = "gif", ["image/webp"] = "webp"
        };
        var mime = contentType.Split(';')[0].Trim();
        if (!mimeToExt.TryGetValue(mime, out var ext))
            return Error(["Unsupported image type. Use PNG, JPEG, GIF, or WebP."]);
        // Empty/unknown state = the global background; rare/complete/duplicate = per-state overrides.
        var slot = state?.Trim().ToLowerInvariant() is "rare" or "complete" or "duplicate" ? state!.Trim().ToLowerInvariant() : "";
        _store.SaveBackground(bytes, ext, slot);
        var filename = string.IsNullOrEmpty(slot) ? $"bg.{ext}" : $"bg-{slot}.{ext}";
        return Ok(new JsonObject { ["ok"] = true, ["filename"] = filename });
    }

    private static JsonObject ReadLocalJsonFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Required file was not found: {path}");
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException($"File is empty: {path}");
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException($"File must be a top-level JSON object: {path}");
    }
}
