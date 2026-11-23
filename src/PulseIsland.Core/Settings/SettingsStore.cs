using System.Text.Json;

namespace PulseIsland.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. Writes are atomic (temp file + rename).</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseIsland", "settings.json");

    /// <summary>Missing file → defaults. Corrupt file → moved aside as settings.corrupt-*.json, then defaults.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new AppSettings();
        try
        {
            return Parse(File.ReadAllText(Path));
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            var backup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,
                $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(Path, backup, overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) => WriteAtomic(Path, settings);

    public void Export(AppSettings settings, string targetPath) => WriteAtomic(targetPath, settings);

    /// <summary>Returns null (and leaves everything untouched) if the file is not valid settings JSON.</summary>
    public static AppSettings? TryImport(string sourcePath)
    {
        try { return Parse(File.ReadAllText(sourcePath)); }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static AppSettings Parse(string json)
    {
        var s = JsonSerializer.Deserialize(json, CoreJsonContext.Default.AppSettings)
                ?? throw new InvalidDataException("empty settings");
        // Repair nulls an edited/old file may contain.
        s.MixerHotkey = string.IsNullOrWhiteSpace(s.MixerHotkey) ? new AppSettings().MixerHotkey : s.MixerHotkey;
        s.Profiles ??= AudioProfile.Defaults();
        s.BrokenFeatures ??= new();
        foreach (var p in s.Profiles) p.AppVolumes ??= new();
        s.Island ??= new();
        s.Island.FullscreenAllowList ??= [];
        s.Island.Scale = Math.Clamp(s.Island.Scale, 0.75, 1.5);
        s.Wellness ??= new();
        s.Osd ??= new();
        s.Cleaning ??= new();
        // Durations outside sane bounds would make timers fire constantly or never.
        var w = s.Wellness;
        w.PomodoroWorkMinutes = Math.Clamp(w.PomodoroWorkMinutes, 1, 180);
        w.PomodoroShortBreakMinutes = Math.Clamp(w.PomodoroShortBreakMinutes, 1, 60);
        w.PomodoroLongBreakMinutes = Math.Clamp(w.PomodoroLongBreakMinutes, 1, 120);
        w.PomodoroCycles = Math.Clamp(w.PomodoroCycles, 1, 12);
        w.WaterGoalMl = Math.Clamp(w.WaterGoalMl, 250, 10000);
        w.WaterIntervalMinutes = Math.Clamp(w.WaterIntervalMinutes, 10, 480);
        w.GlassMl = Math.Clamp(w.GlassMl, 50, 2000);
        w.EyeRestMinutes = Math.Clamp(w.EyeRestMinutes, 5, 120);
        w.StandUpMinutes = Math.Clamp(w.StandUpMinutes, 10, 240);
        s.Cleaning.Minutes = Math.Clamp(s.Cleaning.Minutes, 1, 15);
        return s;
    }

    private static void WriteAtomic(string target, AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target))!);
        var tmp = target + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, settings, CoreJsonContext.Default.AppSettings);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, target, overwrite: true);
    }
}
