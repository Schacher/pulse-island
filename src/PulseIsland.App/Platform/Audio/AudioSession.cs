using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.UI.Dispatching;

namespace PulseIsland.Platform.Audio;

/// <summary>One app audio session (an app can have several; the mixer groups them by exe).</summary>
internal sealed class AudioSession : IDisposable
{
    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume _volume;
    private readonly IAudioMeterInformation? _meter;
    private readonly SessionEvents _events;

    public string InstanceId { get; }
    public uint ProcessId { get; }
    public bool IsSystemSounds { get; }
    public string? ExePath { get; }

    /// <summary>Groups sessions of the same app: exe path, "system" or the pid if the path is unknown.</summary>
    public string GroupKey => IsSystemSounds ? "system" : ExePath ?? $"pid:{ProcessId}";

    /// <summary>Lower-case exe file name, the key used by profiles. Empty for system sounds.</summary>
    public string ExeName => IsSystemSounds || ExePath == null ? "" : Path.GetFileName(ExePath).ToLowerInvariant();

    /// <summary>Raised on the UI thread when volume/mute was changed by someone else.</summary>
    public event Action? ExternalChange;
    /// <summary>Raised on the UI thread when the session ended.</summary>
    public event Action? Expired;

    public AudioSession(IAudioSessionControl2 control, DispatcherQueue ui)
    {
        _control = control;
        _volume = (ISimpleAudioVolume)control;
        try { _meter = (IAudioMeterInformation)control; } catch (InvalidCastException) { }
        InstanceId = control.GetSessionInstanceIdentifier();
        ProcessId = control.GetProcessId();
        IsSystemSounds = control.IsSystemSoundsSession() == 0;
        ExePath = IsSystemSounds ? null : Native.GetProcessPath(ProcessId);
        _events = new SessionEvents(
            () => ui.TryEnqueue(() => ExternalChange?.Invoke()),
            () => ui.TryEnqueue(() => Expired?.Invoke()));
        control.RegisterAudioSessionNotification(_events);
    }

    public AudioSessionState State => Safe(_control.GetState, AudioSessionState.Expired);

    public float Volume
    {
        get => Safe(_volume.GetMasterVolume, 0f);
        set => Safe(() => _volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), AudioService.OwnContext));
    }

    public bool Muted
    {
        get => Safe(_volume.GetMute, false);
        set => Safe(() => _volume.SetMute(value, AudioService.OwnContext));
    }

    public float Peak => _meter == null ? 0f : Safe(_meter.GetPeakValue, 0f);

    // Calls on a session whose process/device just went away fail; the expiry event cleans up.
    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); } catch (COMException) { return fallback; }
    }

    private static void Safe(Action a)
    {
        try { a(); } catch (COMException) { }
    }

    public void Dispose()
    {
        try { _control.UnregisterAudioSessionNotification(_events); } catch (COMException) { }
    }
}

[GeneratedComClass]
internal sealed partial class SessionEvents(Action onExternalChange, Action onExpired) : IAudioSessionEvents
{
    public void OnDisplayNameChanged(string newName, in Guid eventContext) { }
    public void OnIconPathChanged(string newIconPath, in Guid eventContext) { }

    public void OnSimpleVolumeChanged(float newVolume, bool newMute, in Guid eventContext)
    {
        if (eventContext != AudioService.OwnContext) onExternalChange();
    }

    public void OnChannelVolumeChanged(uint channelCount, nint newChannelVolumes, uint changedChannel, in Guid eventContext) { }
    public void OnGroupingParamChanged(in Guid newGroupingParam, in Guid eventContext) { }

    public void OnStateChanged(AudioSessionState newState)
    {
        if (newState == AudioSessionState.Expired) onExpired();
    }

    public void OnSessionDisconnected(int reason) => onExpired();
}
