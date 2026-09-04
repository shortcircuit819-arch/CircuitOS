namespace CircuitOS.Runtime;

internal sealed record RuntimeOptions(string DataPath, string UiPath, string OverlayPath, int Port, bool Headless)
{
    public static RuntimeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--headless" or "--no-browser" or "--check-appwrite" or "--appwrite-roundtrip" or "--push-to-appwrite" or "--appwrite-profiles" or "--appwrite-backups" or "--twitch-login" or "--twitch-reward" or "--twitch-listen" or "--cloud") { flags.Add(arg); continue; }
            if (arg.StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length) values[arg] = args[++index];
        }

        var basePath = Path.GetFullPath(AppContext.BaseDirectory);
        var uiPath = Path.GetFullPath(values.GetValueOrDefault("--ui", FindFolderContaining(
            "index.html",
            Path.Combine(basePath, "App"),
            basePath,
            Path.Combine(basePath, "..")) ?? Path.Combine(basePath, "App")));
        // An installed (Velopack) build ships no Data beside the exe: its writable data lives in a stable
        // per-user folder that SURVIVES updates, because the versioned program folder is replaced on every
        // update. Portable/ZIP still finds Data next to the exe; dev finds the repo data/ via candidates.
        var installedDataDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CircuitOS", "Data");
        var dataPath = Path.GetFullPath(values.GetValueOrDefault("--data", FindDataFolder(
            Path.Combine(basePath, "Data"),
            Path.Combine(basePath, "..", "Data"),
            Path.Combine(uiPath, "..", "Data"),
            Path.Combine(uiPath, "..", "..", "data")) ?? installedDataDefault));
        var overlayPath = Path.GetFullPath(values.GetValueOrDefault("--overlay", FindFolderContaining(
            "overlay.js",
            Path.Combine(basePath, "Overlay"),
            Path.Combine(basePath, "..", "Overlay"),
            Path.Combine(uiPath, "..", "Overlay"),
            Path.Combine(uiPath, "..", "..", "overlays", "lower-quarter"),
            Path.Combine(dataPath, "overlay")) ?? Path.Combine(dataPath, "overlay")));
        var port = int.TryParse(values.GetValueOrDefault("--port", "8787"), out var parsedPort) && parsedPort is > 0 and < 65536
            ? parsedPort
            : 8787;
        return new RuntimeOptions(dataPath, uiPath, overlayPath, port,
            flags.Contains("--headless") || flags.Contains("--no-browser"));
    }

    // Persist effective locations, not relative launch paths: an updater may restart from a
    // different working directory. Only carry normal app options, never installer hook arguments.
    public string[] GetRestartArguments(bool cloud)
    {
        var args = new List<string> { "--data", DataPath, "--ui", UiPath, "--overlay", OverlayPath, "--port", Port.ToString() };
        if (Headless) args.Add("--headless");
        if (cloud) args.Add("--cloud");
        return args.ToArray();
    }

    private static string? FindDataFolder(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var path = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(path, "components.json")) || Directory.Exists(Path.Combine(path, "profiles")))
                return path;
        }
        return null;
    }

    private static string? FindFolderContaining(string requiredFile, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, requiredFile))) return fullPath;
        }
        return null;
    }
}
