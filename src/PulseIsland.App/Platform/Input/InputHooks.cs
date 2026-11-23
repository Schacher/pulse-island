using System.Runtime.InteropServices;
using static PulseIsland.Platform.Native;

namespace PulseIsland.Platform.Input;

/// <summary>
/// Low-level keyboard (and, while cleaning, mouse) hooks on a dedicated thread, so input never waits on
/// our UI thread. Handles: volume keys (so Windows' own flyout doesn't appear), lock-key OSDs, and the
/// keyboard cleaning lock. Callbacks are raised on the hook thread; subscribers must marshal to the UI.
/// The lock can't block Ctrl+Alt+Del (Windows reserves it), and it always ends at its deadline even if
/// the UI thread hangs, because the deadline is checked here.
/// </summary>
internal sealed unsafe partial class InputHooks : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const uint WM_TIMER = 0x0113, WM_QUIT = 0x0012, MsgMouseHook = WM_APP + 10;
    private const uint VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;
    private const uint VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;
    private const uint LLKHF_UP = 0x80;
    private static readonly TimeSpan UnlockHold = TimeSpan.FromSeconds(3);
    private static InputHooks? s_instance;

    private readonly Thread _thread;
    private uint _threadId;
    private nint _keyboardHook, _mouseHook;

    // Shared with the UI thread.
    private volatile bool _locked;
    private long _lockDeadline;          // Environment.TickCount64
    private RECT _mouseAllowed;          // clicks inside still work while the mouse is locked (the island)
    private volatile bool _lockMouse;
    private bool _escDown, _spaceDown;
    private long _bothDownSince;

    /// <summary>Swallow the volume keys and report them instead (keydown only).</summary>
    public volatile bool HandleVolumeKeys;

    public event Action<uint>? VolumeKey;       // VK_VOLUME_*
    /// <summary>A lock key was released. Read its state with <see cref="IsToggled"/> once back on the UI thread.</summary>
    public event Action<uint>? LockKey;
    /// <summary>The cleaning lock ended (deadline, Esc+Space held, or <see cref="Unlock"/>).</summary>
    public event Action? Unlocked;
    /// <summary>Progress of the Esc+Space hold, 0..1 (0 when released).</summary>
    public event Action<double>? UnlockHoldProgress;

    public const uint VolumeMute = VK_VOLUME_MUTE, VolumeDown = VK_VOLUME_DOWN, VolumeUp = VK_VOLUME_UP;
    public const uint CapsLock = VK_CAPITAL, NumLock = VK_NUMLOCK, ScrollLock = VK_SCROLL;

    public bool IsLocked => _locked;

    public static bool IsToggled(uint vk) => (GetKeyState((int)vk) & 1) != 0;

    public InputHooks()
    {
        s_instance = this;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Pulse input hooks", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(2000);
    }

    /// <summary>Blocks keyboard input (and clicks outside <paramref name="mouseAllowed"/>, if asked) until the deadline.</summary>
    public void Lock(TimeSpan duration, bool lockMouse, RECT mouseAllowed)
    {
        _mouseAllowed = mouseAllowed;
        _lockMouse = lockMouse;
        Interlocked.Exchange(ref _lockDeadline, Environment.TickCount64 + (long)duration.TotalMilliseconds);
        _escDown = _spaceDown = false;
        _bothDownSince = 0;
        _locked = true;
        if (lockMouse) PostThreadMessageW(_threadId, MsgMouseHook, 1, 0);
    }

    public void Unlock()
    {
        if (!_locked) return;
        _locked = false;
        PostThreadMessageW(_threadId, MsgMouseHook, 0, 0);
        Unlocked?.Invoke();
    }

    public TimeSpan LockRemaining =>
        _locked ? TimeSpan.FromMilliseconds(Math.Max(0, Interlocked.Read(ref _lockDeadline) - Environment.TickCount64)) : TimeSpan.Zero;

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, GetModuleHandleW(0), 0);
        if (_keyboardHook == 0) Log.Info($"input: keyboard hook failed ({Marshal.GetLastPInvokeError()})");
        SetTimer(0, 0, 100, 0); // lock deadline + unlock-hold checks
        ready.Set();

        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            try
            {
                if (msg.message == WM_TIMER) OnTimer();
                else if (msg.message == MsgMouseHook) SetMouseHook(msg.wParam != 0);
            }
            catch (Exception e) { Log.Error("input hooks", e); }
        }
        SetMouseHook(false);
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
    }

    private void SetMouseHook(bool on)
    {
        if (on && _mouseHook == 0) _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, &MouseProc, GetModuleHandleW(0), 0);
        else if (!on && _mouseHook != 0) { UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
    }

    private void OnTimer()
    {
        if (!_locked) return;
        var now = Environment.TickCount64;
        if (now >= Interlocked.Read(ref _lockDeadline)) { Unlock(); return; }
        if (_bothDownSince != 0)
        {
            var held = TimeSpan.FromMilliseconds(now - _bothDownSince);
            UnlockHoldProgress?.Invoke(Math.Min(1, held / UnlockHold));
            if (held >= UnlockHold) Unlock();
        }
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        var self = s_instance;
        if (code >= 0 && self != null)
        {
            try
            {
                var k = (KBDLLHOOKSTRUCT*)lParam;
                if (self.OnKey(k->vkCode, (k->flags & LLKHF_UP) != 0)) return 1;
            }
            catch (Exception e) { Log.Error("keyboard hook", e); } // must never throw into Windows
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>True = swallow the key.</summary>
    private bool OnKey(uint vk, bool up)
    {
        if (_locked)
        {
            if (vk == VK_ESCAPE) _escDown = !up;
            if (vk == VK_SPACE) _spaceDown = !up;
            var both = _escDown && _spaceDown;
            if (both && _bothDownSince == 0) _bothDownSince = Environment.TickCount64;
            else if (!both && _bothDownSince != 0)
            {
                _bothDownSince = 0;
                UnlockHoldProgress?.Invoke(0);
            }
            return true; // everything is blocked while cleaning
        }

        if (vk is VK_VOLUME_MUTE or VK_VOLUME_DOWN or VK_VOLUME_UP && HandleVolumeKeys)
        {
            if (!up) VolumeKey?.Invoke(vk);
            return true;
        }
        if (vk is VK_CAPITAL or VK_NUMLOCK or VK_SCROLL && up) LockKey?.Invoke(vk);
        return false;
    }

    [UnmanagedCallersOnly]
    private static nint MouseProc(int code, nint wParam, nint lParam)
    {
        var self = s_instance;
        if (code >= 0 && self is { _locked: true, _lockMouse: true })
        {
            var m = (MSLLHOOKSTRUCT*)lParam;
            var msg = (uint)wParam;
            var isClick = msg is 0x201 or 0x202 or 0x203 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20A or 0x20B or 0x20C or 0x20E;
            var r = self._mouseAllowed;
            var inside = m->pt.X >= r.Left && m->pt.X < r.Right && m->pt.Y >= r.Top && m->pt.Y < r.Bottom;
            if (isClick && !inside) return 1;
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Dispose()
    {
        _locked = false;
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(1000);
        s_instance = null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; public uint lPrivate; }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint threadId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessageW(uint threadId, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] private static partial short GetKeyState(int vk);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
}
