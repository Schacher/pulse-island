using System.Runtime.InteropServices;
using PulseIsland.Core.Input;
using static PulseIsland.Platform.Native;

namespace PulseIsland.Platform.Shell;

/// <summary>
/// Hidden top-level window that owns the tray icon and global hotkeys (both deliver window messages).
/// Not message-only: those don't receive the "TaskbarCreated" broadcast sent when Explorer restarts.
/// Must be created on the UI thread (its messages are pumped by the XAML message loop).
/// </summary>
internal sealed unsafe class ShellWindow : IDisposable
{
    private const uint TrayCallback = WM_APP + 1;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIN_SELECT = WM_USER, NIN_KEYSELECT = WM_USER + 1;
    private static ShellWindow? s_instance;

    private readonly nint _hwnd;
    private readonly nint _icon;
    private readonly uint _taskbarCreated;
    private readonly HashSet<int> _hotkeys = [];
    private string _tooltip = "";

    public event Action? TrayClicked;
    /// <summary>Right click on the tray icon, with screen coordinates.</summary>
    public event Action<int, int>? TrayMenuRequested;
    public event Action<int>? HotkeyPressed;

    public nint Handle => _hwnd;

    public ShellWindow(string iconPath)
    {
        s_instance = this;
        var instance = GetModuleHandleW(0);
        const string className = "PulseIsland.Shell";
        fixed (char* name = className)
        {
            var cls = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = name,
            };
            RegisterClassExW(cls);
        }
        _hwnd = CreateWindowExW(0x80 /* WS_EX_TOOLWINDOW */, className, "Pulse Island", 0x80000000 /* WS_POPUP */,
            0, 0, 0, 0, 0, 0, instance, 0);
        if (_hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        var size = GetSystemMetrics(49 /* SM_CXSMICON */);
        _icon = LoadImageW(0, iconPath, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
    }

    public void ShowTrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_ADD, &data);
        data.uVersion = 4; // NOTIFYICON_VERSION_4
        Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = NewData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    /// <summary>Windows notification from the tray icon (quiet, no sound).</summary>
    public void ShowBalloon(string title, string text, bool warning = false)
    {
        var data = NewData(NIF_INFO);
        CopyTo(text, data.szInfo, 256);
        CopyTo(title, data.szInfoTitle, 64);
        data.dwInfoFlags = (warning ? 2u /* NIIF_WARNING */ : 1u /* NIIF_INFO */) | 0x10 /* NIIF_NOSOUND */;
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    /// <summary>Shows a native popup menu and returns the chosen id (0 = dismissed).</summary>
    public int ShowMenu(int x, int y, IEnumerable<(int Id, string Text)> items)
    {
        var menu = CreatePopupMenu();
        foreach (var (id, text) in items)
            AppendMenuW(menu, id == 0 ? 0x800u /* MF_SEPARATOR */ : 0u, (nuint)id, id == 0 ? null : text);
        SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
        var chosen = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x80 /* TPM_NONOTIFY */ | 0x2 /* TPM_RIGHTBUTTON */, x, y, _hwnd, 0);
        PostMessageW(_hwnd, WM_NULL, 0, 0);
        DestroyMenu(menu);
        return chosen;
    }

    /// <summary>False if another app already owns this key combination.</summary>
    public bool RegisterHotkey(int id, Hotkey hotkey)
    {
        UnregisterHotkey(id);
        if (!Native.RegisterHotKey(_hwnd, id, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey)) return false;
        _hotkeys.Add(id);
        return true;
    }

    public void UnregisterHotkey(int id)
    {
        if (_hotkeys.Remove(id)) Native.UnregisterHotKey(_hwnd, id);
    }

    private NOTIFYICONDATAW NewData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = TrayCallback,
            hIcon = _icon,
        };
        CopyTo(_tooltip, data.szTip, 128);
        return data;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var self = s_instance;
        try
        {
            if (self != null && hwnd == self._hwnd && self.OnMessage(msg, wParam, lParam)) return 0;
        }
        catch (Exception e)
        {
            Log.Error("ShellWindow", e); // never let an exception cross into native code
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == TrayCallback)
        {
            // NOTIFYICON_VERSION_4: event in LOWORD(lParam), anchor point in wParam.
            switch ((uint)(lParam & 0xFFFF))
            {
                case NIN_SELECT or NIN_KEYSELECT:
                    TrayClicked?.Invoke();
                    return true;
                case WM_CONTEXTMENU:
                    TrayMenuRequested?.Invoke((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    return true;
            }
            return false;
        }
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke((int)wParam);
            return true;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            ShowTrayIcon(_tooltip); // Explorer restarted: the icon is gone, add it again
            if (_appBar) RegisterAppBar();
            return true;
        }
        if (msg == AppBarCallback && wParam == 2 /* ABN_FULLSCREENAPP */)
        {
            FullscreenAppChanged?.Invoke();
            return true;
        }
        if (msg == 0x007E /* WM_DISPLAYCHANGE */)
        {
            DisplayChanged?.Invoke();
            return false;
        }
        return false;
    }

    private const uint AppBarCallback = WM_APP + 2;
    private bool _appBar;

    /// <summary>An app entered or left fullscreen (shell notification, no polling).</summary>
    public event Action? FullscreenAppChanged;
    /// <summary>Monitors were added/removed or resolution changed.</summary>
    public event Action? DisplayChanged;

    /// <summary>
    /// Registers as an app bar only to receive ABN_FULLSCREENAPP. No screen space is reserved
    /// (that would need ABM_SETPOS, which we never send).
    /// </summary>
    public void RegisterAppBar()
    {
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = _hwnd, uCallbackMessage = AppBarCallback };
        SHAppBarMessage(0 /* ABM_NEW */, ref data);
        _appBar = true;
    }

    public void Dispose()
    {
        if (_appBar)
        {
            var bar = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = _hwnd };
            SHAppBarMessage(1 /* ABM_REMOVE */, ref bar);
        }
        foreach (var id in _hotkeys) Native.UnregisterHotKey(_hwnd, id);
        _hotkeys.Clear();
        var data = NewData(0);
        Shell_NotifyIconW(NIM_DELETE, &data);
        if (_icon != 0) DestroyIcon(_icon);
        DestroyWindow(_hwnd);
        s_instance = null;
    }
}
