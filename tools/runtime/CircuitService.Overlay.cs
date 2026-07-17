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

    public ServiceResult SaveOverlayBackground(byte[] bytes, string state)
    {
        // Identify the image from its actual bytes, not the client-declared Content-Type — a mislabeled
        // or non-image payload must not be written as an overlay background.
        var ext = SniffImageExtension(bytes);
        if (ext is null)
            return Error(["That file isn't a recognized image. Use a real PNG, JPEG, GIF, or WebP."]);
        // Empty/unknown state = the global background; rare/complete/duplicate = per-state overrides.
        var slot = state?.Trim().ToLowerInvariant() is "rare" or "complete" or "duplicate" ? state!.Trim().ToLowerInvariant() : "";
        _store.SaveBackground(bytes, ext, slot);
        var filename = string.IsNullOrEmpty(slot) ? $"bg.{ext}" : $"bg-{slot}.{ext}";
        return Ok(new JsonObject { ["ok"] = true, ["filename"] = filename });
    }

    // Returns the file extension for a supported image by its magic number, or null if the bytes aren't
    // a recognized image. Signature-based so the server never trusts a client-supplied MIME type.
    internal static string? SniffImageExtension(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38
            && (b[4] == 0x37 || b[4] == 0x39) && b[5] == 0x61) return "gif";       // GIF87a / GIF89a
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "webp"; // RIFF....WEBP
        return null;
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
