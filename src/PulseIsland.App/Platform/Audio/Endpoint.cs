using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.UI.Dispatching;

namespace PulseIsland.Platform.Audio;

/// <summary>A device's master volume, mute and peak level.</summary>
internal sealed class Endpoint : IDisposable
{
    private readonly IAudioEndpointVolume _volume;
    private readonly IAudioMeterInformation _meter;
    private readonly EndpointVolumeCallback _callback;

    public IMMDevice Device { get; }
    public string Id { get; }
    public string Name { get; }

    /// <summary>Volume or mute changed by someone else (keys, other apps). Raised on the UI thread.</summary>
    public event Action? ExternalChange;

    public Endpoint(IMMDevice device, string id, DispatcherQueue ui)
    {
        Device = device;
        Id = id;
        Name = ReadName(device);
        _volume = Com.Activate<IAudioEndpointVolume>(device, CoreAudioIds.IAudioEndpointVolume);
        _meter = Com.Activate<IAudioMeterInformation>(device, CoreAudioIds.IAudioMeterInformation);
        _callback = new EndpointVolumeCallback(() => ui.TryEnqueue(() => ExternalChange?.Invoke()));
        _volume.RegisterControlChangeNotify(_callback);
    }

    public float Volume
    {
        get => Safe(_volume.GetMasterVolumeLevelScalar);
        set => Safe(() => _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), AudioService.OwnContext));
    }

    public bool Muted
    {
        get => Safe(_volume.GetMute);
        set => Safe(() => _volume.SetMute(value, AudioService.OwnContext));
    }

    public float Peak => Safe(_meter.GetPeakValue);

    internal static string ReadName(IMMDevice device)
    {
        try { return Com.ReadString(device.OpenPropertyStore(0 /* STGM_READ */), CoreAudioIds.FriendlyName) ?? "?"; }
        catch (COMException) { return "?"; }
    }

    // A device can vanish at any moment (unplugged); its calls then fail with AUDCLNT_E_DEVICE_INVALIDATED.
    // The device-removed notification rebuilds everything, so failing quietly here is enough.
    private static T Safe<T>(Func<T> f)
    {
        try { return f(); } catch (COMException) { return default!; }
    }

    private static void Safe(Action a)
    {
        try { a(); } catch (COMException) { }
    }

    public void Dispose()
    {
        try { _volume.UnregisterControlChangeNotify(_callback); } catch (COMException) { }
    }
}

[GeneratedComClass]
internal sealed partial class EndpointVolumeCallback(Action onExternalChange) : IAudioEndpointVolumeCallback
{
    public unsafe void OnNotify(nint notifyData)
    {
        if (((AudioVolumeNotificationData*)notifyData)->EventContext != AudioService.OwnContext)
            onExternalChange();
    }
}
