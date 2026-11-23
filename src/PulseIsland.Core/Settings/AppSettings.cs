using System.Text.Json.Serialization;

namespace PulseIsland.Core.Settings;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>UI language code (e.g. "tr-TR"); null follows the OS language.</summary>
    public string? Language { get; set; }

    public string MixerHotkey { get; set; } = "Ctrl+Alt+V";

    /// <summary>Optional; null or empty disables the mic mute hotkey.</summary>
    public string? MicMuteHotkey { get; set; } = "Ctrl+Alt+M";

    public List<AudioProfile> Profiles { get; set; } = AudioProfile.Defaults();

    /// <summary>Undocumented-API features that failed, keyed by feature id.</summary>
    public Dictionary<string, FeatureFailure> BrokenFeatures { get; set; } = new();

    /// <summary>True once the "running in the tray" hint was shown.</summary>
    public bool FirstRunHintShown { get; set; }

    public IslandSettings Island { get; set; } = new();
    public WellnessSettings Wellness { get; set; } = new();
    public OsdSettings Osd { get; set; } = new();
    public CleaningSettings Cleaning { get; set; } = new();
}

public sealed class WellnessSettings
{
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroShortBreakMinutes { get; set; } = 5;
    public int PomodoroLongBreakMinutes { get; set; } = 15;
    public int PomodoroCycles { get; set; } = 4;
    /// <summary>Play a sound when a Pomodoro phase ends.</summary>
    public bool Chime { get; set; } = true;

    public bool Hydration { get; set; } = true;
    public int WaterGoalMl { get; set; } = 2000;
    public int WaterIntervalMinutes { get; set; } = 60;
    public int GlassMl { get; set; } = 250;
    /// <summary>"ml" or "oz" (display only; stored in ml).</summary>
    public string WaterUnit { get; set; } = "ml";
    /// <summary>Today's intake, kept across restarts. Day as yyyy-MM-dd.</summary>
    public string? WaterDay { get; set; }
    public int WaterTodayMl { get; set; }

    public bool EyeRest { get; set; } = true;
    public int EyeRestMinutes { get; set; } = 20;
    public bool StandUp { get; set; } = true;
    public int StandUpMinutes { get; set; } = 50;
}

public sealed class OsdSettings
{
    /// <summary>Volume changes (keys, other apps) show on the island.</summary>
    public bool Volume { get; set; } = true;
    /// <summary>Handle the volume keys ourselves so Windows' own volume flyout doesn't appear.</summary>
    public bool ReplaceWindowsVolumeFlyout { get; set; } = true;
    public bool LockKeys { get; set; } = true;
    public bool Bluetooth { get; set; } = true;
}

public sealed class CleaningSettings
{
    public int Minutes { get; set; } = 1;
    /// <summary>Also block mouse clicks (except on the island, so its unlock slider works).</summary>
    public bool LockMouse { get; set; }
}

public sealed class IslandSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>"primary" or "active" (follows the monitor of the active window).</summary>
    public string Monitor { get; set; } = "primary";

    /// <summary>Offset from top-center, in DIPs.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; } = 6;

    /// <summary>Size multiplier, 0.75–1.5.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>What the island does over fullscreen apps: "dot", "hide" or "show".</summary>
    public string Fullscreen { get; set; } = "dot";

    /// <summary>Lower-case exe names that keep the full island even in fullscreen.</summary>
    public List<string> FullscreenAllowList { get; set; } = [];

    /// <summary>Briefly show the new track when the song changes.</summary>
    public bool TrackToasts { get; set; } = true;

    /// <summary>Real spectrum from the audio output (costs a little CPU while music plays).</summary>
    public bool Visualizer { get; set; } = true;
}

public sealed class AudioProfile
{
    public string Id { get; set; } = "";
    public float? MasterVolume { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? InputDeviceId { get; set; }
    public float? MicVolume { get; set; }
    public bool? MicMuted { get; set; }

    /// <summary>Volume per app, keyed by lower-case exe file name (e.g. "spotify.exe").</summary>
    public Dictionary<string, float> AppVolumes { get; set; } = new();

    public static List<AudioProfile> Defaults() =>
    [
        new() { Id = "gaming", MasterVolume = 0.80f },
        new() { Id = "meeting", MasterVolume = 0.60f, MicMuted = false, MicVolume = 0.80f },
        new() { Id = "focus", MasterVolume = 0.35f },
        new() { Id = "night", MasterVolume = 0.20f },
    ];
}

public sealed class FeatureFailure
{
    public string WindowsBuild { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

// Nulls are written on purpose: null MicMuteHotkey means "disabled" and must survive a reload.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
