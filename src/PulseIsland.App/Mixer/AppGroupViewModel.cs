using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PulseIsland.Core.Localization;
using PulseIsland.Platform;
using PulseIsland.Platform.Audio;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PulseIsland.Mixer;

/// <summary>One mixer row: all audio sessions of one app (same exe).</summary>
public sealed partial class AppGroupViewModel : Observable
{
    private readonly List<AudioSession> _sessions = [];
    private double _volume;
    private bool _muted;
    private double _peak;
    private ImageSource? _icon;

    internal AppGroupViewModel(AudioSession first)
    {
        Key = first.GroupKey;
        IsSystem = first.IsSystemSounds;
        ExeName = first.ExeName;
        _exePath = first.ExePath;
        Name = ResolveName();
        Add(first);
        if (_exePath != null) LoadIcon(_exePath);
    }

    private readonly string? _exePath;
    public string Key { get; }
    public bool IsSystem { get; }
    internal string ExeName { get; }
    public string Name { get; private set; }
    public string FallbackGlyph => IsSystem ? "" : "";
    public bool IsEmpty => _sessions.Count == 0;

    public ImageSource? Icon
    {
        get => _icon;
        private set { if (Set(ref _icon, value)) Raise(nameof(GlyphVisibility)); }
    }

    public Visibility GlyphVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>0–100. Setting it changes every session of the app.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value)) return;
            foreach (var s in _sessions) s.Volume = (float)(value / 100);
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            foreach (var s in _sessions) s.Muted = value;
            Raise(nameof(MuteGlyph));
        }
    }

    public string MuteGlyph => _muted ? "" : "";

    public double Peak
    {
        get => _peak;
        private set => Set(ref _peak, value);
    }

    internal float CurrentVolume => (float)(_volume / 100);

    /// <summary>Peak read right now (<see cref="Peak"/> only updates while the mixer is open).</summary>
    internal float LivePeak => _sessions.Count == 0 ? 0 : _sessions.Max(s => s.Peak);

    public void ToggleMute() => Muted = !Muted;

    internal void Add(AudioSession session)
    {
        _sessions.Add(session);
        session.ExternalChange += SyncFromSystem;
        if (_sessions.Count == 1) SyncFromSystem();
    }

    internal void Remove(AudioSession session)
    {
        session.ExternalChange -= SyncFromSystem;
        _sessions.Remove(session);
    }

    /// <summary>Pull volume/mute from the first session without writing anything back.</summary>
    internal void SyncFromSystem()
    {
        if (_sessions.Count == 0) return;
        var s = _sessions[0];
        // Rounded so the slider (step 1) doesn't snap and write a slightly different value back.
        Set(ref _volume, Math.Round(s.Volume * 100), nameof(Volume));
        if (Set(ref _muted, s.Muted, nameof(Muted))) Raise(nameof(MuteGlyph));
    }

    internal void SetVolumeFromProfile(float volume) => Volume = Math.Round(volume * 100);

    internal void TickMeter() => Peak = _muted ? 0 : _sessions.Count == 0 ? 0 : _sessions.Max(s => s.Peak);

    internal void RefreshTexts()
    {
        Name = ResolveName();
        Raise(nameof(Name));
    }

    private string ResolveName()
    {
        if (IsSystem) return Loc.T("Mixer.SystemSounds");
        if (_exePath == null) return ExeName;
        try
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(_exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Path.GetFileNameWithoutExtension(_exePath);
    }

    private async void LoadIcon(string exePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(exePath);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32, ThumbnailOptions.UseCurrentScale);
            if (thumb == null) return;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumb);
            Icon = bitmap;
        }
        catch (Exception e)
        {
            Log.Info($"icon for {exePath}: {e.Message}"); // falls back to the glyph
        }
    }
}
