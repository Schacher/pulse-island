using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Settings;
using PulseIsland.Platform;
using PulseIsland.Platform.Audio;
using Windows.Media.Audio;
using Windows.Media.Devices;

namespace PulseIsland.Mixer;

public sealed partial class DeviceItem(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
}

public sealed partial class ProfileItem : Observable
{
    private readonly MixerViewModel _owner;
    internal AudioProfile Profile { get; }

    internal ProfileItem(MixerViewModel owner, AudioProfile profile)
    {
        _owner = owner;
        Profile = profile;
    }

    public string Name => LocalizedName(Profile.Id);
    public string SaveText => Loc.T("Profile.SaveCurrent");
    public void Apply() => _owner.ApplyProfile(this);
    public void SaveCurrent() => _owner.SaveProfile(this);
    internal void RefreshTexts() { Raise(nameof(Name)); Raise(nameof(SaveText)); }

    internal static string LocalizedName(string id)
    {
        var key = $"Profile.{id}";
        var text = Loc.T(key);
        return text == key ? id : text;
    }
}

/// <summary>State of the mixer flyout. UI thread only.</summary>
public sealed partial class MixerViewModel : Observable
{
    private readonly AudioService _audio;
    private readonly App _app;
    private Endpoint? _output, _input;
    private bool _syncing;

    private double _masterVolume, _micVolume, _masterPeak, _micPeak;
    private bool _masterMuted, _micMuted, _canSwitchDevices = true;
    private string _spatialText = "";
    private string _message = "";
    private bool _messageOpen;
    private InfoBarSeverity _messageSeverity;

    public ObservableCollection<DeviceItem> Outputs { get; } = [];
    public ObservableCollection<DeviceItem> Inputs { get; } = [];
    public ObservableCollection<AppGroupViewModel> Apps { get; } = [];
    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    internal MixerViewModel(AudioService audio, App app)
    {
        _audio = audio;
        _app = app;
        _audio.DevicesChanged += SyncDevices;
        _audio.SessionAdded += AddSession;
        _audio.SessionRemoved += RemoveSession;
        foreach (var s in _audio.Sessions) AddSession(s);
        ReloadProfiles();
        SyncDevices();
    }

    // ---- devices ----
    public string OutputName => _output?.Name ?? Loc.T("Mixer.NoDevice");
    public string InputName => _input?.Name ?? Loc.T("Mixer.NoDevice");
    internal string? OutputId => _output?.Id;
    internal string? InputId => _input?.Id;

    /// <summary>Explicit user choice from the device menu.</summary>
    internal void SelectDevice(string deviceId)
    {
        if (deviceId != _output?.Id && deviceId != _input?.Id) SwitchDefault(deviceId);
    }

    // ---- output ----
    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (!Set(ref _masterVolume, value)) return;
            Raise(nameof(MasterVolumeText));
            if (!_syncing && _output != null) _output.Volume = (float)(value / 100);
        }
    }

    public string MasterVolumeText => $"{_masterVolume:0}";

    public bool MasterMuted
    {
        get => _masterMuted;
        set
        {
            if (!Set(ref _masterMuted, value)) return;
            Raise(nameof(MasterGlyph));
            if (!_syncing && _output != null) _output.Muted = value;
        }
    }

    public string MasterGlyph => _masterMuted ? "" : "";
    public double MasterPeak { get => _masterPeak; private set => Set(ref _masterPeak, value); }
    public void ToggleMasterMute() => MasterMuted = !MasterMuted;

    // ---- input ----
    public double MicVolume
    {
        get => _micVolume;
        set
        {
            if (!Set(ref _micVolume, value)) return;
            Raise(nameof(MicVolumeText));
            if (!_syncing && _input != null) _input.Volume = (float)(value / 100);
        }
    }

    public string MicVolumeText => $"{_micVolume:0}";

    public bool MicMuted
    {
        get => _micMuted;
        set
        {
            if (!Set(ref _micMuted, value)) return;
            Raise(nameof(MicGlyph));
            if (!_syncing && _input != null) _input.Muted = value;
        }
    }

    public string MicGlyph => _micMuted ? "" : "";
    public double MicPeak { get => _micPeak; private set => Set(ref _micPeak, value); }
    public void ToggleMicMuteClick() => MicMuted = !MicMuted;
    public bool HasInput => _input != null;

    /// <summary>For the mic hotkey. Returns the new muted state, or null if there is no microphone.</summary>
    internal bool? ToggleMicMute()
    {
        if (_input == null) return null;
        MicMuted = !_input.Muted;
        return MicMuted;
    }

    // ---- misc ----
    public bool CanSwitchDevices
    {
        get => _canSwitchDevices;
        set { if (Set(ref _canSwitchDevices, value)) Raise(nameof(DeviceSwitchTooltip)); }
    }

    public string? DeviceSwitchTooltip => _canSwitchDevices ? null : Loc.T($"Feature.Broken.{FeatureGate.DefaultDevice}");
    public string SpatialText { get => _spatialText; private set => Set(ref _spatialText, value); }
    public bool NoApps => Apps.Count == 0;

    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool MessageOpen { get => _messageOpen; set => Set(ref _messageOpen, value); }
    public InfoBarSeverity MessageSeverity { get => _messageSeverity; private set => Set(ref _messageSeverity, value); }

    internal void ShowMessage(string text, bool warning = false)
    {
        Message = text;
        MessageSeverity = warning ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        MessageOpen = true;
    }

    /// <summary>Called ~30x/s while the flyout is visible.</summary>
    internal void TickMeters()
    {
        MasterPeak = _output == null || _masterMuted ? 0 : _output.Peak;
        MicPeak = _input == null || _micMuted ? 0 : _input.Peak;
        foreach (var app in Apps) app.TickMeter();
    }

    /// <summary>Before showing: pull everything fresh (cheap, and catches changes we weren't told about).</summary>
    internal void SyncAll()
    {
        SyncLevels();
        foreach (var app in Apps) app.SyncFromSystem();
        SpatialText = ReadSpatialState();
    }

    internal void RefreshTexts()
    {
        foreach (var app in Apps) app.RefreshTexts();
        foreach (var p in Profiles) p.RefreshTexts();
        Raise(nameof(DeviceSwitchTooltip));
        Raise(nameof(OutputName));
        Raise(nameof(InputName));
        SpatialText = ReadSpatialState();
        MessageOpen = false;
    }

    /// <summary>
    /// The mixer row of the app behind a media session. Media ids are usually the exe's name ("Brave",
    /// "MSEdge", "Spotify.exe", "SpotifyAB.SpotifyMusic_…!Spotify"); if none matches (Firefox uses a hash),
    /// fall back to the app that is making sound right now.
    /// </summary>
    internal AppGroupViewModel? FindMediaApp(string sourceApp)
    {
        var id = sourceApp.ToLowerInvariant();
        var apps = Apps.Where(a => !a.IsSystem && a.ExeName.Length > 0).ToList();
        return apps.FirstOrDefault(a => id.Contains(Path.GetFileNameWithoutExtension(a.ExeName)))
               ?? apps.Select(a => (App: a, Peak: a.LivePeak)).Where(x => x.Peak > 0.001f).OrderByDescending(x => x.Peak).Select(x => x.App).FirstOrDefault();
    }

    internal void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _app.Settings.Profiles) Profiles.Add(new ProfileItem(this, p));
    }

    internal void ApplyProfile(ProfileItem item)
    {
        var p = item.Profile;
        if (_canSwitchDevices)
        {
            if (p.OutputDeviceId != null && p.OutputDeviceId != _output?.Id && _audio.Outputs.Any(d => d.Id == p.OutputDeviceId))
                SwitchDefault(p.OutputDeviceId);
            if (p.InputDeviceId != null && p.InputDeviceId != _input?.Id && _audio.Inputs.Any(d => d.Id == p.InputDeviceId))
                SwitchDefault(p.InputDeviceId);
        }
        // SwitchDefault refreshes synchronously, so these now target the new devices.
        if (p.MasterVolume is { } mv && _output != null) _output.Volume = mv;
        if (_input != null)
        {
            if (p.MicVolume is { } micv) _input.Volume = micv;
            if (p.MicMuted is { } mm) _input.Muted = mm;
        }
        foreach (var app in Apps)
            if (app.ExeName.Length > 0 && p.AppVolumes.TryGetValue(app.ExeName, out var v)) app.SetVolumeFromProfile(v);
        SyncLevels();
        ShowMessage(Loc.T("Profile.Applied", item.Name));
    }

    internal void SaveProfile(ProfileItem item)
    {
        var p = item.Profile;
        p.MasterVolume = _output?.Volume;
        p.OutputDeviceId = _output?.Id;
        p.InputDeviceId = _input?.Id;
        p.MicVolume = _input?.Volume;
        p.MicMuted = _input?.Muted;
        p.AppVolumes = Apps.Where(a => a.ExeName.Length > 0)
                           .GroupBy(a => a.ExeName)
                           .ToDictionary(g => g.Key, g => g.First().CurrentVolume);
        _app.SaveSettings();
        ShowMessage(Loc.T("Profile.Saved", item.Name));
    }

    private void SwitchDefault(string deviceId)
    {
        try
        {
            _audio.SetDefaultDevice(deviceId);
        }
        catch (Exception e)
        {
            _app.ReportBroken(FeatureGate.DefaultDevice, e.Message);
            SyncDevices(); // show the real default again
        }
    }

    private void SyncDevices()
    {
        if (_output != _audio.Output)
        {
            if (_output != null) _output.ExternalChange -= SyncLevels;
            _output = _audio.Output;
            if (_output != null) _output.ExternalChange += SyncLevels;
        }
        if (_input != _audio.Input)
        {
            if (_input != null) _input.ExternalChange -= SyncLevels;
            _input = _audio.Input;
            if (_input != null) _input.ExternalChange += SyncLevels;
            Raise(nameof(HasInput));
        }

        Fill(Outputs, _audio.Outputs);
        Fill(Inputs, _audio.Inputs);
        Raise(nameof(OutputName));
        Raise(nameof(InputName));
        SyncLevels();
        SpatialText = ReadSpatialState();
    }

    private static void Fill(ObservableCollection<DeviceItem> target, IReadOnlyList<AudioDevice> devices)
    {
        target.Clear();
        foreach (var d in devices) target.Add(new DeviceItem(d.Id, d.Name));
    }

    private void SyncLevels()
    {
        _syncing = true;
        try
        {
            MasterVolume = _output == null ? 0 : Math.Round(_output.Volume * 100);
            MasterMuted = _output?.Muted ?? false;
            MicVolume = _input == null ? 0 : Math.Round(_input.Volume * 100);
            MicMuted = _input?.Muted ?? false;
        }
        finally { _syncing = false; }
    }

    private void AddSession(AudioSession session)
    {
        var group = Apps.FirstOrDefault(a => a.Key == session.GroupKey);
        if (group != null)
        {
            group.Add(session);
            return;
        }
        group = new AppGroupViewModel(session);
        // System sounds first, then by name.
        var index = 0;
        while (index < Apps.Count && (Apps[index].IsSystem ||
               (!group.IsSystem && string.Compare(Apps[index].Name, group.Name, StringComparison.CurrentCultureIgnoreCase) < 0)))
            index++;
        Apps.Insert(index, group);
        Raise(nameof(NoApps));
    }

    private void RemoveSession(AudioSession session)
    {
        var group = Apps.FirstOrDefault(a => a.Key == session.GroupKey);
        if (group == null) return;
        group.Remove(session);
        if (group.IsEmpty)
        {
            Apps.Remove(group);
            Raise(nameof(NoApps));
        }
    }

    private static string ReadSpatialState()
    {
        string state;
        try
        {
            var id = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            var config = string.IsNullOrEmpty(id) ? null : SpatialAudioDeviceConfiguration.GetForDeviceId(id);
            state = config is not { IsSpatialAudioSupported: true } ? Loc.T("Mixer.SpatialUnsupported")
                : string.IsNullOrEmpty(config.ActiveSpatialAudioFormat) ? Loc.T("Mixer.SpatialOff")
                : Loc.T("Mixer.SpatialOn");
        }
        catch (Exception e)
        {
            Log.Info($"spatial state: {e.Message}");
            state = Loc.T("Mixer.SpatialUnsupported");
        }
        return Loc.T("Mixer.Spatial", state);
    }
}
