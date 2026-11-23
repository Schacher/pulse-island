namespace PulseIsland.Core.Settings;

/// <summary>
/// Tracks undocumented-API features that failed. A failure is remembered only for the exact
/// Windows build + app version it happened on; any update clears it so the feature is retried.
/// </summary>
public static class FeatureGate
{
    public const string DefaultDevice = "defaultDevice";

    /// <summary>True if the feature failed on this same build/version. Stale entries are removed.</summary>
    public static bool IsBroken(AppSettings s, string feature, string windowsBuild, string appVersion)
    {
        if (!s.BrokenFeatures.TryGetValue(feature, out var f)) return false;
        if (f.WindowsBuild == windowsBuild && f.AppVersion == appVersion) return true;
        s.BrokenFeatures.Remove(feature);
        return false;
    }

    public static void MarkBroken(AppSettings s, string feature, string windowsBuild, string appVersion, string error) =>
        s.BrokenFeatures[feature] = new FeatureFailure
        {
            WindowsBuild = windowsBuild, AppVersion = appVersion, Error = error, At = DateTimeOffset.Now,
        };
}
