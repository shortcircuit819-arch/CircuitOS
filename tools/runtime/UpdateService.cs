using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace CircuitOS.Runtime;

// In-app auto-update via Velopack + GitHub Releases (docs/updater-velopack-plan.md).
//
// Only functional when the app was installed by the Velopack Setup.exe (UpdateManager.IsInstalled). A
// raw-exe / ZIP-install / dev run reports Managed=false and simply does nothing — those users update by
// running the new Setup.exe once.
//
// The GitHub Releases feed must be PUBLICLY readable so the app can fetch it without shipping a token
// (shipping a token in a client is the same foot-gun as a master key). If the source repo is private,
// publish releases to a public releases repo and point RepoUrl there.
internal static class UpdateService
{
    // The release feed. `vpk upload github --tag vX.Y.Z` publishes here; the app checks the same place.
    private const string RepoUrl = "https://github.com/shortcircuit819-arch/CircuitOS";

    // Test/dev override: point the updater at a local directory feed instead of GitHub. Unset in
    // production (the app always uses the GitHub feed). Used to validate the update flow end-to-end
    // against a local feed without publishing anything.
    private static string? FeedOverride => Environment.GetEnvironmentVariable("CIRCUITOS_UPDATE_FEED");

    public sealed record UpdateStatus(bool Managed, bool Available, string? LatestVersion, string CurrentVersion, string? Error);

    private static UpdateManager NewManager() =>
        string.IsNullOrWhiteSpace(FeedOverride)
            ? new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false))
            : new UpdateManager(FeedOverride);

    private static string CurrentVersion()
    {
        // Prefer the informational version (the release string, e.g. "0.9.0"); fall back to assembly.
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0];
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    }

    // Checks the feed. Never throws — surfaces problems as Error so the panel can show a friendly line.
    public static async Task<UpdateStatus> CheckAsync()
    {
        var current = CurrentVersion();
        try
        {
            var mgr = NewManager();
            // A raw/ZIP/dev copy isn't updatable — except when a local test feed is explicitly configured,
            // where we're deliberately exercising the check against that feed.
            if (!mgr.IsInstalled && string.IsNullOrWhiteSpace(FeedOverride))
                return new UpdateStatus(false, false, null, current, null);
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
                return new UpdateStatus(true, false, null, current, null);
            return new UpdateStatus(true, true, info.TargetFullRelease.Version.ToString(), current, null);
        }
        catch (Exception ex)
        {
            return new UpdateStatus(false, false, null, current, ex.Message);
        }
    }

    // Downloads and applies the newest release, then restarts the app (this process exits on success).
    // Returns a status (with Error) only when it declines to update; on success the process is replaced.
    public static async Task<UpdateStatus> ApplyAsync()
    {
        var current = CurrentVersion();
        try
        {
            var mgr = NewManager();
            if (!mgr.IsInstalled)
                return new UpdateStatus(false, false, null, current, "This copy wasn't installed by the CircuitOS updater. Download the latest Setup.exe to update.");
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
                return new UpdateStatus(true, false, null, current, null);
            await mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
            mgr.ApplyUpdatesAndRestart(info); // replaces the process — nothing after this runs on success
            return new UpdateStatus(true, true, info.TargetFullRelease.Version.ToString(), current, null);
        }
        catch (Exception ex)
        {
            return new UpdateStatus(false, false, null, current, ex.Message);
        }
    }
}
