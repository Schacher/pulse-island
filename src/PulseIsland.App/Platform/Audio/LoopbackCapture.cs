using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using PulseIsland.Core.Audio;

namespace PulseIsland.Platform.Audio;

[GeneratedComInterface]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
    uint GetBufferSize();
    long GetStreamLatency();
    uint GetCurrentPadding();
    void IsFormatSupported(); // unused placeholder (vtable slot)
    nint GetMixFormat();      // WAVEFORMATEX*, CoTaskMem
    void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    void Start();
    void Stop();
    void Reset();
    void SetEventHandle(nint handle);
    nint GetService(in Guid iid);
}

[GeneratedComInterface]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
internal partial interface IAudioCaptureClient
{
    void GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    void ReleaseBuffer(uint frames);
    uint GetNextPacketSize();
}

[GeneratedComInterface]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
internal partial interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(out int activateResult, out nint activatedInterface);
}

[GeneratedComInterface]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
internal partial interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

/// <summary>Marker: the completion handler may be called from any thread.</summary>
[GeneratedComInterface]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface IAgileObject;

[GeneratedComClass]
internal sealed partial class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    public readonly TaskCompletionSource<nint> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
    {
        operation.GetActivateResult(out var hr, out var client);
        if (hr < 0) Result.SetException(Marshal.GetExceptionForHR(hr)!);
        else Result.SetResult(client);
    }
}

/// <summary>
/// Captures the audio every other app is playing (WASAPI process loopback, excluding our own process) and
/// feeds a <see cref="SpectrumAnalyzer"/> on a background thread. Process loopback taps the apps' streams
/// before device volume/mute and whatever output they are routed to — device loopback returns pure silence
/// on a muted device even while you hear the music elsewhere (e.g. through a monitor's own jack).
/// Started only while something plays and the island shows the bars.
/// </summary>
internal sealed partial class LoopbackCapture : IDisposable
{
    private static readonly Guid IAudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IAudioCaptureClientId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private const int SampleRate = 48000, Channels = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParams { public int Type; public uint TargetProcessId; public int Mode; }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat { public ushort Tag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, Size; }

    [LibraryImport("Mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ActivateAudioInterfaceAsync(string deviceInterfacePath, in Guid riid, in PropVariant activationParams, nint completionHandler, out nint operation);

    private readonly Thread _thread;
    private volatile bool _stop;

    public SpectrumAnalyzer Analyzer { get; }

    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _capture;
    private readonly int _channels;

    private LoopbackCapture(IAudioClient client, IAudioCaptureClient capture, int channels, int sampleRate)
    {
        _client = client;
        _capture = capture;
        _channels = channels;
        Analyzer = new SpectrumAnalyzer(sampleRate);
        _thread = new Thread(Run) { IsBackground = true, Name = "Pulse loopback", Priority = ThreadPriority.BelowNormal };
    }

    /// <summary>Starts capturing all apps' audio except ours. Returns null if Windows refuses.</summary>
    public static unsafe LoopbackCapture? Start()
    {
        try
        {
            var p = new ActivationParams
            {
                Type = 1,                          // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                TargetProcessId = (uint)Environment.ProcessId,
                Mode = 1,                          // PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE
            };
            var blob = new PropVariant { Type = 65 /* VT_BLOB */, Value = sizeof(ActivationParams), Value2 = (nint)(&p) };
            var handler = new ActivationHandler();
            var handlerPtr = (nint)ComInterfaceMarshaller<IActivateAudioInterfaceCompletionHandler>.ConvertToUnmanaged(handler);
            try
            {
                Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", IAudioClientId, blob, handlerPtr, out var op));
                if (!handler.Result.Task.Wait(3000)) throw new TimeoutException("activation timed out");
                if (op != 0) Marshal.Release(op);
            }
            finally { Marshal.Release(handlerPtr); }

            var client = Com.Wrap<IAudioClient>(handler.Result.Task.Result);
            // Process loopback has no mix format of its own: ask for float stereo and let Windows convert.
            var format = new WaveFormat
            {
                Tag = 3, // WAVE_FORMAT_IEEE_FLOAT
                Channels = Channels,
                SamplesPerSec = SampleRate,
                AvgBytesPerSec = SampleRate * Channels * 4,
                BlockAlign = Channels * 4,
                BitsPerSample = 32,
            };
            const uint Loopback = 0x00020000, AutoConvertPcm = 0x80000000, DefaultQualitySrc = 0x08000000;
            client.Initialize(0 /* shared */, Loopback | AutoConvertPcm | DefaultQualitySrc, 200 * 10_000 /* 200 ms */, 0, (nint)(&format), 0);
            var capture = Com.Wrap<IAudioCaptureClient>(client.GetService(IAudioCaptureClientId));
            var instance = new LoopbackCapture(client, capture, Channels, SampleRate);
            client.Start();
            instance._thread.Start();
            return instance;
        }
        catch (Exception e)
        {
            Log.Info($"spectrum: process loopback unavailable: {e.Message}");
            return null;
        }
    }

    private unsafe void Run()
    {
        try
        {
            while (!_stop)
            {
                Thread.Sleep(15);
                var any = false;
                while (_capture.GetNextPacketSize() > 0)
                {
                    _capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    if ((flags & 0x2) != 0 /* AUDCLNT_BUFFERFLAGS_SILENT */) Analyzer.AddSilence((int)frames);
                    else Analyzer.AddInterleaved(new ReadOnlySpan<float>((void*)data, (int)frames * _channels), _channels);
                    _capture.ReleaseBuffer(frames);
                    any = true;
                }
                // Loopback delivers nothing while the device is idle: feed silence so bars fall.
                if (!any) Analyzer.AddSilence(720);
                Analyzer.Compute();
            }
        }
        catch (COMException e)
        {
            Log.Info($"spectrum: capture stopped: {e.Message}"); // device removed / format changed
            Analyzer.Reset();
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(500);
        try { _client.Stop(); } catch (COMException) { }
    }
}
