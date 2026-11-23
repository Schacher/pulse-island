using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

// Core Audio (mmdeviceapi.h / endpointvolume.h / audiopolicy.h) as source-generated COM (NativeAOT-safe).
// Vtable order matters: methods are declared exactly in header order. Base-interface methods are
// flattened into the derived interface (IAudioSessionManager2, IAudioSessionControl2).
namespace PulseIsland.Platform.Audio;

internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid fmtid, uint pid)
{
    public Guid FormatId = fmtid;
    public uint PropertyId = pid;
}

/// <summary>Only the parts we read (VT_LPWSTR). 24 bytes on 64-bit.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Type;
    public ushort Reserved1, Reserved2, Reserved3;
    public nint Value;
    public nint Value2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    public int Muted;
    public float MasterVolume;
    public uint Channels;
    // float afChannelVolumes[Channels] follows
}

internal static class CoreAudioIds
{
    public static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    public static readonly Guid IPolicyConfig = new("F8679F50-850A-41CF-9C72-430F290290C8");
    public static readonly Guid IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static readonly Guid IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly PropertyKey FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    public const uint DeviceStateActive = 0x1;
    public const uint ClsctxAll = 0x17;
    public const ushort VtLpwstr = 31;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    IMMDeviceCollection EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask);
    IMMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role);
    IMMDevice GetDevice(string id);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    uint GetCount();
    IMMDevice Item(uint index);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    nint Activate(in Guid iid, uint clsCtx, nint activationParams);
    IPropertyStore OpenPropertyStore(uint access);
    string GetId();
    uint GetState();
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    uint GetCount();
    PropertyKey GetAt(uint index);
    PropVariant GetValue(in PropertyKey key);
    void SetValue(in PropertyKey key, in PropVariant value);
    void Commit();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IMMNotificationClient
{
    void OnDeviceStateChanged(string deviceId, uint newState);
    void OnDeviceAdded(string deviceId);
    void OnDeviceRemoved(string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId);
    void OnPropertyValueChanged(string deviceId, PropertyKey key);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    uint GetChannelCount();
    void SetMasterVolumeLevel(float levelDb, in Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, in Guid eventContext);
    float GetMasterVolumeLevel();
    float GetMasterVolumeLevelScalar();
    void SetChannelVolumeLevel(uint channel, float levelDb, in Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, in Guid eventContext);
    float GetChannelVolumeLevel(uint channel);
    float GetChannelVolumeLevelScalar(uint channel);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);
    [return: MarshalAs(UnmanagedType.Bool)] bool GetMute();
    // GetVolumeStepInfo, VolumeStepUp, VolumeStepDown, QueryHardwareSupport, GetVolumeRange: unused, last in vtable.
}

[GeneratedComInterface]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
internal partial interface IAudioEndpointVolumeCallback
{
    void OnNotify(nint notifyData); // AUDIO_VOLUME_NOTIFICATION_DATA*
}

[GeneratedComInterface]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
internal partial interface IAudioMeterInformation
{
    float GetPeakValue();
    // GetMeteringChannelCount, GetChannelsPeakValues, QueryHardwareSupport: unused, last in vtable.
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2
{
    // IAudioSessionManager
    nint GetAudioSessionControl(in Guid sessionGuid, uint streamFlags);
    nint GetSimpleAudioVolume(in Guid sessionGuid, uint streamFlags);
    // IAudioSessionManager2
    IAudioSessionEnumerator GetSessionEnumerator();
    void RegisterSessionNotification(IAudioSessionNotification notification);
    void UnregisterSessionNotification(IAudioSessionNotification notification);
    void RegisterDuckNotification(string sessionId, nint duckNotification);
    void UnregisterDuckNotification(nint duckNotification);
}

[GeneratedComInterface]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    int GetCount();
    IAudioSessionControl2 GetSession(int index);
}

[GeneratedComInterface]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
internal partial interface IAudioSessionNotification
{
    void OnSessionCreated(IAudioSessionControl2 newSession);
}

internal enum AudioSessionState { Inactive = 0, Active = 1, Expired = 2 }

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal partial interface IAudioSessionControl2
{
    // IAudioSessionControl
    AudioSessionState GetState();
    string GetDisplayName();
    void SetDisplayName(string value, in Guid eventContext);
    string GetIconPath();
    void SetIconPath(string value, in Guid eventContext);
    Guid GetGroupingParam();
    void SetGroupingParam(in Guid value, in Guid eventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents events);
    void UnregisterAudioSessionNotification(IAudioSessionEvents events);
    // IAudioSessionControl2
    string GetSessionIdentifier();
    string GetSessionInstanceIdentifier();
    uint GetProcessId();
    [PreserveSig] int IsSystemSoundsSession(); // S_OK (0) = yes, S_FALSE (1) = no
    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
internal partial interface IAudioSessionEvents
{
    void OnDisplayNameChanged(string newName, in Guid eventContext);
    void OnIconPathChanged(string newIconPath, in Guid eventContext);
    void OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, in Guid eventContext);
    void OnChannelVolumeChanged(uint channelCount, nint newChannelVolumes, uint changedChannel, in Guid eventContext);
    void OnGroupingParamChanged(in Guid newGroupingParam, in Guid eventContext);
    void OnStateChanged(AudioSessionState newState);
    void OnSessionDisconnected(int reason);
}

[GeneratedComInterface]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal partial interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, in Guid eventContext);
    float GetMasterVolume();
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);
    [return: MarshalAs(UnmanagedType.Bool)] bool GetMute();
}

/// <summary>Undocumented (used by the Windows Sound control panel). Only SetDefaultEndpoint is called.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
internal partial interface IPolicyConfig
{
    void GetMixFormat();          // placeholders keep vtable slots; never called
    void GetDeviceFormat();
    void ResetDeviceFormat();
    void SetDeviceFormat();
    void GetProcessingPeriod();
    void SetProcessingPeriod();
    void GetShareMode();
    void SetShareMode();
    void GetPropertyValue();
    void SetPropertyValue();
    void SetDefaultEndpoint(string deviceId, ERole role);
}

internal static partial class Com
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    public static T Create<T>(Guid clsid, Guid iid) where T : class
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, CoreAudioIds.ClsctxAll, iid, out var ptr));
        return Wrap<T>(ptr);
    }

    /// <summary>Wraps (and takes ownership of) a raw interface pointer.</summary>
    public static T Wrap<T>(nint ptr) where T : class
    {
        try { return (T)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None); }
        finally { Marshal.Release(ptr); }
    }

    public static T Activate<T>(IMMDevice device, Guid iid) where T : class =>
        Wrap<T>(device.Activate(iid, CoreAudioIds.ClsctxAll, 0));

    public static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        var v = store.GetValue(key);
        try { return v.Type == CoreAudioIds.VtLpwstr ? Marshal.PtrToStringUni(v.Value) : null; }
        finally { PropVariantClear(ref v); }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant pvar);
}
