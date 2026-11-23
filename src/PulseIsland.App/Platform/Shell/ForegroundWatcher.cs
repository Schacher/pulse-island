using System.Runtime.InteropServices;
using static PulseIsland.Platform.Native;

namespace PulseIsland.Platform.Shell;

/// <summary>
/// Event-driven (no polling) view of the foreground window: which monitor it is on and whether it covers
/// that monitor (fullscreen game/video). Sources: foreground-change WinEvents plus the shell's
/// fullscreen-app notification (forwarded by <see cref="ShellWindow"/>). UI thread only.
/// </summary>
internal sealed unsafe class ForegroundWatcher : IDisposable
{
    private static ForegroundWatcher? s_instance;
    private readonly nint _hook;

    public event Action? Changed;

    public nint Monitor { get; private set; }
    public bool Fullscreen { get; private set; }
    /// <summary>Lower-case exe name of the foreground app ("" if unknown).</summary>
    public string Exe { get; private set; } = "";

    public ForegroundWatcher()
    {
        s_instance = this;
        // EVENT_SYSTEM_FOREGROUND, out-of-context: delivered through our message loop.
        _hook = SetWinEventHook(3, 3, 0, &OnWinEvent, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
        Evaluate();
    }

    /// <summary>Re-check now (e.g. the shell said an app went fullscreen or left it).</summary>
    public void Evaluate()
    {
        var fg = GetForegroundWindow();
        var monitor = fg == 0 ? MonitorFromPoint(default, 1) : MonitorFromWindow(fg, 2 /* NEAREST */);
        var fullscreen = false;
        var exe = "";
        if (fg != 0 && ClassName(fg) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"))
        {
            GetWindowThreadProcessId(fg, out var pid);
            exe = Path.GetFileName(GetProcessPath(pid) ?? "").ToLowerInvariant();
            var screen = MonitorInfo(monitor).rcMonitor;
            fullscreen = GetWindowRect(fg, out var r) &&
                         r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
            // Exclusive-mode D3D games may not report a covering rect.
            if (!fullscreen && SHQueryUserNotificationState(out var state) == 0 && state == 3 /* QUNS_RUNNING_D3D_FULL_SCREEN */)
                fullscreen = true;
        }
        if (monitor == Monitor && fullscreen == Fullscreen && exe == Exe) return;
        Monitor = monitor;
        Fullscreen = fullscreen;
        Exe = exe;
        Changed?.Invoke();
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { s_instance?.Evaluate(); }
        catch (Exception e) { Log.Error("ForegroundWatcher", e); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    public void Dispose()
    {
        if (_hook != 0) UnhookWinEvent(_hook);
        s_instance = null;
    }
}
