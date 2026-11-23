using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.UI.Dispatching;

namespace PulseIsland.Platform.Audio;

internal sealed record AudioDevice(string Id, string Name);

/// <summary>
/// Default output/input endpoints, their device lists and the app sessions on the default output.
/// All public members and events are used on the UI thread; COM callbacks are marshalled there.
/// </summary>
internal sealed class AudioService : IDisposable
{
    /// <summary>Event context we pass on every change we make, so our own changes are not echoed back.</summary>
    public static readonly Guid OwnContext = Guid.NewGuid();

    private readonly DispatcherQueue _ui;
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceNotifications _deviceNotifications;
    private readonly Dictionary<string, AudioSession> _sessions = new();
    private IAudioSessionManager2? _sessionManager;
    private SessionNotifications? _sessionNotifications;
    private int _generation;
    private int _refreshQueued;

    public Endpoint? Output { get; private set; }
    public Endpoint? Input { get; private set; }
    public IReadOnlyList<AudioDevice> Outputs { get; private set; } = [];
    public IReadOnlyList<AudioDevice> Inputs { get; private set; } = [];
    public IEnumerable<AudioSession> Sessions => _sessions.Values;

    /// <summary>Device list or a default device changed. Output/Input may be new objects.</summary>
    public event Action? DevicesChanged;
    /// <summary>The default output's volume/mute was changed by someone else (keys, other apps).</summary>
    public event Action? OutputVolumeChanged;
    public event Action<AudioSession>? SessionAdded;
    public event Action<AudioSession>? SessionRemoved;

    public AudioService(DispatcherQueue ui)
    {
        _ui = ui;
        _enumerator = Mta(() => Com.Create<IMMDeviceEnumerator>(CoreAudioIds.MMDeviceEnumeratorClsid, CoreAudioIds.IMMDeviceEnumerator));
        _deviceNotifications = new DeviceNotifications(this);
        _enumerator.RegisterEndpointNotificationCallback(_deviceNotifications);
        Refresh();
    }

    /// <summary>Re-reads devices and defaults; rebinds endpoints/sessions whose default changed.</summary>
    public void Refresh()
    {
        Outputs = ListDevices(EDataFlow.Render);
        Inputs = ListDevices(EDataFlow.Capture);

        var outId = DefaultId(EDataFlow.Render);
        if (outId != Output?.Id)
        {
            Output?.Dispose();
            Output = outId == null ? null : new Endpoint(_enumerator.GetDevice(outId), outId, _ui);
            if (Output != null) Output.ExternalChange += () => OutputVolumeChanged?.Invoke();
            BindSessions();
        }
        var inId = DefaultId(EDataFlow.Capture);
        if (inId != Input?.Id)
        {
            Input?.Dispose();
            Input = inId == null ? null : new Endpoint(_enumerator.GetDevice(inId), inId, _ui);
        }
        DevicesChanged?.Invoke();
    }

    /// <summary>Makes the device default for all roles. Throws if the undocumented API fails.</summary>
    public void SetDefaultDevice(string deviceId)
    {
        Platform.Log.Info($"set default device: {Outputs.Concat(Inputs).FirstOrDefault(d => d.Id == deviceId)?.Name ?? deviceId}");
        var policy = Com.Create<IPolicyConfig>(CoreAudioIds.PolicyConfigClsid, CoreAudioIds.IPolicyConfig);
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            policy.SetDefaultEndpoint(deviceId, role);
        Refresh(); // apply now; the notification-driven refresh that follows is a no-op
    }

    /// <summary>Checks the undocumented default-device API is present. Returns the error, or null if OK.</summary>
    public static string? ProbeDefaultDeviceApi()
    {
        try { _ = Com.Create<IPolicyConfig>(CoreAudioIds.PolicyConfigClsid, CoreAudioIds.IPolicyConfig); return null; }
        catch (Exception e) { return e.Message; }
    }

    internal void QueueRefresh()
    {
        // Default changes arrive once per role (3x); coalesce into one refresh on the UI thread.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            _ui.TryEnqueue(() => { _refreshQueued = 0; Refresh(); });
    }

    internal void OnSessionCreated(IAudioSessionControl2 control, int generation)
    {
        AudioSession session;
        try { session = new AudioSession(control, _ui); }
        catch (COMException) { return; } // session died before we could read it
        _ui.TryEnqueue(() => Add(session, generation));
    }

    private void BindSessions()
    {
        foreach (var s in _sessions.Values.ToList()) Remove(s);
        if (_sessionManager != null && _sessionNotifications != null)
            Try(() => _sessionManager.UnregisterSessionNotification(_sessionNotifications));
        _sessionManager = null;
        var generation = ++_generation;
        if (Output == null) return;

        // Session notifications are only delivered to MTA registrations.
        var device = Output.Device;
        var (manager, notifications, initial) = Mta(() =>
        {
            var m = Com.Activate<IAudioSessionManager2>(device, CoreAudioIds.IAudioSessionManager2);
            var en = m.GetSessionEnumerator(); // must be called once before registering
            var n = new SessionNotifications(this, generation);
            m.RegisterSessionNotification(n);
            var list = new List<AudioSession>();
            for (var i = 0; i < en.GetCount(); i++)
            {
                try { list.Add(new AudioSession(en.GetSession(i), _ui)); }
                catch (COMException) { }
            }
            return (m, n, list);
        });
        _sessionManager = manager;
        _sessionNotifications = notifications;
        foreach (var s in initial) Add(s, generation);
    }

    private void Add(AudioSession session, int generation)
    {
        // ExePath == null: the process is already gone (Windows keeps such sessions around; its mixer hides them too).
        if (generation != _generation || session.State == AudioSessionState.Expired || _sessions.ContainsKey(session.InstanceId)
            || (!session.IsSystemSounds && session.ExePath == null))
        {
            session.Dispose();
            return;
        }
        _sessions[session.InstanceId] = session;
        session.Expired += () => Remove(session);
        SessionAdded?.Invoke(session);
    }

    private void Remove(AudioSession session)
    {
        if (!_sessions.Remove(session.InstanceId)) return;
        session.Dispose();
        SessionRemoved?.Invoke(session);
    }

    private List<AudioDevice> ListDevices(EDataFlow flow)
    {
        var result = new List<AudioDevice>();
        var collection = _enumerator.EnumAudioEndpoints(flow, CoreAudioIds.DeviceStateActive);
        for (uint i = 0; i < collection.GetCount(); i++)
        {
            var device = collection.Item(i);
            result.Add(new AudioDevice(device.GetId(), Endpoint.ReadName(device)));
        }
        return result;
    }

    private string? DefaultId(EDataFlow flow)
    {
        try { return _enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia).GetId(); }
        catch (COMException) { return null; } // E_NOTFOUND: no device of this kind
    }

    private static T Mta<T>(Func<T> f) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA ? f() : Task.Run(f).GetAwaiter().GetResult();

    private static void Try(Action a)
    {
        try { a(); } catch (COMException) { }
    }

    public void Dispose()
    {
        foreach (var s in _sessions.Values) s.Dispose();
        _sessions.Clear();
        if (_sessionManager != null && _sessionNotifications != null)
            Try(() => _sessionManager.UnregisterSessionNotification(_sessionNotifications));
        Try(() => _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications));
        Output?.Dispose();
        Input?.Dispose();
    }
}

[GeneratedComClass]
internal sealed partial class DeviceNotifications(AudioService owner) : IMMNotificationClient
{
    public void OnDeviceStateChanged(string deviceId, uint newState) => owner.QueueRefresh();
    public void OnDeviceAdded(string deviceId) => owner.QueueRefresh();
    public void OnDeviceRemoved(string deviceId) => owner.QueueRefresh();
    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => owner.QueueRefresh();
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { } // very chatty; names rarely change
}

[GeneratedComClass]
internal sealed partial class SessionNotifications(AudioService owner, int generation) : IAudioSessionNotification
{
    public void OnSessionCreated(IAudioSessionControl2 newSession) => owner.OnSessionCreated(newSession, generation);
}
