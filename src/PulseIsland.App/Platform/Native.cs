using System.Runtime.InteropServices;

namespace PulseIsland.Platform;

/// <summary>Win32 imports used across the app. All blittable (NativeAOT-safe).</summary>
internal static unsafe partial class Native
{
    public const uint WM_NULL = 0x0000, WM_DESTROY = 0x0002, WM_CONTEXTMENU = 0x007B, WM_HOTKEY = 0x0312, WM_USER = 0x0400, WM_APP = 0x8000;
    public const uint MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    // user32
    [LibraryImport("user32.dll")] public static partial ushort RegisterClassExW(in WNDCLASSEXW cls);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnregisterHotKey(nint hwnd, int id);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessageW(string name);
    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyMenu(nint menu);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AllowSetForegroundWindow(int processId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT point);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromPoint(POINT point, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetWindowRect(nint hwnd, out RECT rect);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll")] public static partial int GetClassNameW(nint hwnd, char* name, int max);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint pid, uint tid, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnhookWinEvent(nint hook);
    [LibraryImport("gdi32.dll")] public static partial nint CreateRectRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(nint obj);

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_BLURBEHIND { public uint dwFlags; public int fEnable; public nint hRgnBlur; public int fTransitionOnMaximized; }
    [LibraryImport("dwmapi.dll")] public static partial int DwmEnableBlurBehindWindow(nint hwnd, in DWM_BLURBEHIND blur);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA { public uint cbSize; public nint hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public nint lParam; }
    [LibraryImport("shell32.dll")] public static partial nuint SHAppBarMessage(uint message, ref APPBARDATA data);

    [StructLayout(LayoutKind.Sequential)]
    public struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }
    [LibraryImport("CoreMessaging.dll")] public static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetLastInputInfo(ref LASTINPUTINFO info);
    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PlaySoundW(string? sound, nint module, uint flags);

    /// <summary>Time since the last keyboard/mouse input anywhere in the session.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime) : TimeSpan.Zero;
    }

    /// <summary>Plays a Windows sound event (respects the user's sound scheme and mute).</summary>
    public static void PlaySystemSound(string alias) => PlaySoundW(alias, 0, 0x10000 /* SND_ALIAS */ | 0x1 /* ASYNC */ | 0x2 /* NODEFAULT */);

    public static uint MonitorDpi(nint monitor) =>
        GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0 ? dx : 96;

    public static MONITORINFO MonitorInfo(nint monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(monitor, ref info);
        return info;
    }

    public static string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var n = GetClassNameW(hwnd, buffer, 256);
        return new string(buffer, 0, Math.Max(0, n));
    }

    // shell32 / shcore
    [LibraryImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);
    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    // kernel32
    [LibraryImport("kernel32.dll")] public static partial nint GetModuleHandleW(nint name);
    [LibraryImport("kernel32.dll")] public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseHandle(nint handle);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* buffer, ref uint size);

    /// <summary>Full exe path of a process, or null if it is gone. Works for elevated processes too.</summary>
    public static string? GetProcessPath(uint pid)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageNameW(h, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Work area (screen minus taskbar) and scale factor of the monitor under the mouse.</summary>
    public static (RECT Work, double Scale) MonitorUnderCursor()
    {
        GetCursorPos(out var p);
        var monitor = MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(monitor, ref info);
        var dpi = GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0 ? dx : 96;
        return (info.rcWork, dpi / 96.0);
    }

    public static void CopyTo(string text, char* dest, int capacity)
    {
        var n = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, n).CopyTo(new Span<char>(dest, capacity));
        dest[n] = '\0';
    }
}
