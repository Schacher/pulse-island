using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PulseIsland.Platform;
using Windows.Foundation;
using Windows.Graphics;

namespace PulseIsland.Mixer;

/// <summary>Borderless acrylic flyout anchored to the bottom-right of the work area, like Windows quick settings.</summary>
internal sealed class MixerWindow : Window
{
    private const double WidthDip = 360, MarginDip = 12;

    private readonly MixerViewModel _vm;
    private readonly App _app;
    private readonly DispatcherQueueTimer _meterTimer;
    private long _hiddenAt;
    private Native.RECT _work;
    private double _scale = 1;

    public nint Hwnd { get; }

    /// <summary>Keep the flyout open while a file picker (which steals focus) is up.</summary>
    public bool SuppressAutoHide { get; set; }

    public bool IsShown => AppWindow.IsVisible;

    public MixerWindow(MixerViewModel vm, App app)
    {
        _vm = vm;
        _app = app;
        Title = "Pulse Island";
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SystemBackdrop = new DesktopAcrylicBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
            p.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false); // keeps Win11 rounded corners + shadow
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PulseIsland.ico"));
        AppWindow.Closing += (_, e) => { e.Cancel = true; Hide(); }; // Alt+F4 hides, never destroys

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && !SuppressAutoHide) Hide();
        };

        _meterTimer = DispatcherQueue.CreateTimer();
        _meterTimer.Interval = TimeSpan.FromMilliseconds(33);
        _meterTimer.Tick += (_, _) => _vm.TickMeters();

        // New rows only have a size after the next layout pass, so resize after it.
        _vm.Apps.CollectionChanged += (_, _) => { if (IsShown) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Relayout); };
        Rebuild();
    }

    /// <summary>Recreate the content (used after a language change: texts are bound once).</summary>
    public void Rebuild()
    {
        var view = new MixerView(_vm, _app) { RequestedTheme = _app.ElementTheme }; // the acrylic backdrop follows it
        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, e) => { e.Handled = true; Hide(); };
        view.KeyboardAccelerators.Add(escape);
        Content = view;
        if (IsShown) Relayout();
    }

    public void Toggle()
    {
        if (IsShown) Hide();
        // A tray click first deactivates (hides) the open flyout; don't reopen it on the same click.
        else if (Environment.TickCount64 - _hiddenAt > 300) Show();
    }

    public void Show()
    {
        (_work, _scale) = Native.MonitorUnderCursor();
        _vm.SyncAll();
        Relayout();
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(Hwnd);
        // Focus the inert root, not the first control: keys the user was typing elsewhere must not
        // toggle a mute button or move a slider. Tab still reaches every control.
        ((Control)Content).Focus(FocusState.Programmatic);
        _meterTimer.Start();
        // Lists (ItemsRepeater) only measure their real height once live; size again after that layout pass.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Relayout);
    }

    public void Hide()
    {
        if (!IsShown) return;
        _meterTimer.Stop();
        AppWindow.Hide();
        _hiddenAt = Environment.TickCount64;
    }

    private void Relayout()
    {
        var workHeightDip = (_work.Bottom - _work.Top) / _scale;
        var content = (FrameworkElement)Content;
        content.Measure(new Size(WidthDip, double.PositiveInfinity));
        var heightDip = content.DesiredSize.Height < 200 ? 560 : content.DesiredSize.Height;
        heightDip = Math.Min(heightDip, workHeightDip * 0.8);

        // Size the client area (content), then anchor the outer frame to the bottom-right corner.
        AppWindow.ResizeClient(new SizeInt32((int)Math.Round(WidthDip * _scale), (int)Math.Round(heightDip * _scale)));
        var outer = AppWindow.Size;
        var m = (int)Math.Round(MarginDip * _scale);
        AppWindow.Move(new PointInt32(_work.Right - outer.Width - m, _work.Bottom - outer.Height - m));    }
}
