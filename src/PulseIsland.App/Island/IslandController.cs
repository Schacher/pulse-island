using Microsoft.UI.Dispatching;
using PulseIsland.Core.Island;
using PulseIsland.Core.Settings;
using PulseIsland.Mixer;
using PulseIsland.Platform;
using PulseIsland.Platform.Audio;
using PulseIsland.Platform.Media;
using PulseIsland.Platform.Shell;

namespace PulseIsland.Island;

/// <summary>
/// Decides what the island shows and when: media state, hover/expand, alerts, fullscreen, monitor.
/// Idle cost is zero: timers only run while something on the island moves.
/// </summary>
internal sealed class IslandController : IDisposable
{
    private readonly App _app;
    private readonly MediaService _media;
    private readonly ForegroundWatcher _foreground;
    private readonly IslandViewModel _vm;
    private readonly IslandView _view;
    private readonly IslandWindow _window;
    private readonly AlertQueue _alerts = new();
    private readonly DispatcherQueueTimer _frameTimer, _alertTimer, _leaveTimer;
    private readonly float[] _levels = new float[24];
    private LoopbackCapture? _capture;
    private IslandAlert? _shownAlert;
    private DispatcherQueueTimer? _demoTimer;
    private readonly MixerViewModel _mixer;
    private string? _lastTrack;
    private bool _hover, _expanded;
    private nint _monitor;

    private IslandSettings Settings => _app.Settings.Island;

    public IslandController(App app, MediaService media, MixerViewModel mixer, ForegroundWatcher foreground, DispatcherQueue ui)
    {
        _app = app;
        _media = media;
        _mixer = mixer;
        _foreground = foreground;
        _vm = new IslandViewModel(media);
        _view = new IslandView(_vm);
        _view.SizePanels(Shape);
        _window = new IslandWindow(_view);

        _frameTimer = ui.CreateTimer();
        _frameTimer.Interval = TimeSpan.FromMilliseconds(33);
        _frameTimer.Tick += (_, _) => Frame();
        _alertTimer = ui.CreateTimer();
        _alertTimer.Interval = TimeSpan.FromMilliseconds(100);
        _alertTimer.Tick += (_, _) => Update();
        _leaveTimer = ui.CreateTimer();
        _leaveTimer.Interval = TimeSpan.FromMilliseconds(300); // grace for brushing past the edge, still feels instant
        _leaveTimer.IsRepeating = false;
        _leaveTimer.Tick += (_, _) => { _hover = false; _expanded = false; Update(); };

        _window.PillEntered += () => { _leaveTimer.Stop(); _hover = true; Update(); };
        _window.PillExited += () => _leaveTimer.Start();
        _window.PillTapped += () => { if (!_expanded) { _expanded = true; Update(); } };
        _vm.AccentChanged += _window.SetGlow;
        _media.Changed += OnMediaChanged;
        _foreground.Changed += OnForegroundChanged;
    }

    public void Start()
    {
        // The app's audio session often appears a moment after its media session does.
        _mixer.Apps.CollectionChanged += (_, _) => ResolveMediaApp();
        Reposition();
        Update();
        // Dev aid for screenshots: PULSE_DEMO=osd|card|cleaning|focus shows a sample (nothing is changed or locked).
        var demo = TimeSpan.FromSeconds(60);
        switch (Environment.GetEnvironmentVariable("PULSE_DEMO"))
        {
            case "osd": Post(new IslandAlert("demo", AlertPriority.Osd, demo, new OsdPayload("", "", 0.42))); break;
            case "lock": Post(new IslandAlert("demo", AlertPriority.Osd, demo, new OsdPayload("", "Caps Lock açık"))); break;
            case "card": Post(new IslandAlert("demo", AlertPriority.Alarm, demo, new CardPayload("💧", "Su içme zamanı", "Su 750 ml / 2000 ml",
                new CardAction("+250 ml", () => { }), new CardAction("Sonra", () => { }), Windows.UI.Color.FromArgb(255, 30, 80, 110)))); break;
            case "cleaning": _vm.SetCleaning(TimeSpan.FromSeconds(42), TimeSpan.FromMinutes(1)); Post(new IslandAlert("demo", AlertPriority.Modal, demo)); break;
            case "focus": _vm.ShowFocusPage(); _expanded = true; Update(); break;
            case "morph": // open/close every 1.5 s to inspect the transition
                _demoTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer(); // field: a local would be collected
                _demoTimer.Interval = TimeSpan.FromMilliseconds(1500);
                _demoTimer.Tick += (_, _) => { _expanded = !_expanded; Update(); };
                _demoTimer.Start();
                break;
        }
    }

    /// <summary>Settings changed (menu, import): re-apply everything.</summary>
    public void ApplySettings()
    {
        Reposition();
        Update();
    }

    public void RefreshTexts()
    {
        _vm.RefreshTexts();
        _vm.RefreshPanelTexts();
    }

    internal IslandViewModel Vm => _vm;

    /// <summary>The island window on screen, in physical pixels (cleaning mode lets clicks through here).</summary>
    internal Native.RECT ScreenRect => Native.GetWindowRect(_window.Hwnd, out var r) ? r : default;

    /// <summary>Shows an alert (OSD, card, cleaning). Same key = update in place.</summary>
    public void Post(IslandAlert alert)
    {
        _alerts.Post(alert, DateTime.Now);
        _shownAlert = null; // re-apply the payload even if the same alert stays on screen
        Update();
    }

    public void Dismiss(string key)
    {
        _alerts.Dismiss(key);
        Update();
    }

    /// <summary>The Pomodoro ring needs the compact pill even when nothing plays.</summary>
    public void PomodoroChanged() => Update();

    public void Reposition()
    {
        var monitor = Settings.Monitor == "active" && _foreground.Monitor != 0
            ? _foreground.Monitor
            : Native.MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */);
        _monitor = monitor;
        _window.Place(monitor, Settings.OffsetX, Settings.OffsetY, Settings.Scale);
    }

    private void OnForegroundChanged()
    {
        if (Settings.Monitor == "active" && _foreground.Monitor != _monitor) Reposition();
        Update();
    }

    private void OnMediaChanged()
    {
        var now = _media.Current;
        _vm.SetMedia(now);
        ResolveMediaApp();
        if (now != null && _lastTrack != null && now.TrackKey != _lastTrack && Settings.TrackToasts)
            _alerts.Post(new IslandAlert("track", AlertPriority.Toast, TimeSpan.FromSeconds(3)), DateTime.Now);
        if (now != null) _lastTrack = now.TrackKey;
        Update();
    }

    /// <summary>Links the island's volume row to the mixer row of the app that is playing.</summary>
    private void ResolveMediaApp() =>
        _vm.SetMediaApp(_media.Current is { } now ? _mixer.FindMediaApp(now.SourceApp) : null);

    /// <summary>Recomputes the mode and everything that depends on it.</summary>
    private void Update()
    {
        var fullscreen = _foreground.Fullscreen && _foreground.Monitor == _monitor
                         && !Settings.FullscreenAllowList.Contains(_foreground.Exe);
        _alerts.Fullscreen = fullscreen && Settings.Fullscreen != "show";
        _alerts.UserInteracting = _hover || _expanded;
        var alert = _alerts.Update(DateTime.Now);
        if (_alerts.IsEmpty) _alertTimer.Stop(); else _alertTimer.Start();

        // A shown alert's payload decides its panel; the track toast reuses the Peek layout.
        var alertMode = alert?.Payload switch
        {
            OsdPayload => IslandMode.Osd,
            CardPayload => IslandMode.Card,
            _ when alert?.Priority == AlertPriority.Modal => IslandMode.Cleaning,
            _ when alert?.Key == "track" => IslandMode.Peek,
            _ => (IslandMode?)null,
        };
        if (alert != _shownAlert)
        {
            _shownAlert = alert;
            if (alert?.Payload is OsdPayload osd) _vm.ShowOsd(osd);
            if (alert?.Payload is CardPayload card) _vm.ShowCard(card, () => Dismiss(alert.Key));
        }

        var mode = !Settings.Enabled && alertMode != IslandMode.Cleaning ? IslandMode.Hidden
            : fullscreen && Settings.Fullscreen == "hide" && alertMode == null ? IslandMode.Hidden
            : fullscreen && Settings.Fullscreen == "dot" && alertMode == null ? IslandMode.Dot
            : alertMode is IslandMode.Cleaning or IslandMode.Osd or IslandMode.Card ? alertMode.Value
            : _expanded || _hover ? IslandMode.Expanded // hovering opens it fully, no click needed
            : alertMode == IslandMode.Peek ? IslandMode.Peek
            : _vm.HasMedia || _vm.PomodoroActive ? IslandMode.Compact
            : IslandMode.Idle;
        // Dev aid: PULSE_ISLAND_MODE=Expanded etc. pins a layout for screenshots without a mouse.
        if (Enum.TryParse<IslandMode>(Environment.GetEnvironmentVariable("PULSE_ISLAND_MODE"), out var pinned)) mode = pinned;

        if (mode != _vm.Mode || !_window.IsShown)
        {
            _vm.Mode = mode;
            if (mode == IslandMode.Hidden) _window.HideIsland();
            else
            {
                var (w, h, r) = Shape(mode);
                _window.ShowIsland();
                _window.Morph(w, h, r);
            }
        }

        // Bars only run while music plays on a visible panel; the seek bar only while expanded.
        var bars = Settings.Visualizer && _vm.IsPlaying && mode is IslandMode.Compact or IslandMode.Peek or IslandMode.Expanded;
        if (bars) EnsureCapture();
        else
        {
            StopCapture();
            // Settle here, not only when the timer stops: expanded keeps the timer for the seek bar.
            foreach (var b in _view.AllBars) b.Settle();
        }
        if (bars || mode == IslandMode.Expanded) _frameTimer.Start(); else _frameTimer.Stop();
    }

    internal static (float W, float H, float R) Shape(IslandMode mode) => mode switch
    {
        IslandMode.Dot => (14, 14, 7),
        IslandMode.Idle => (128, 30, 15),
        IslandMode.Compact => (236, 32, 16),
        IslandMode.Peek => (340, 60, 30),
        IslandMode.Expanded => (400, 222, 34),
        IslandMode.Osd => (300, 40, 20),
        IslandMode.Card => (400, 68, 34),
        IslandMode.Cleaning => (400, 186, 34),
        _ => (0, 0, 0),
    };

    private void Frame()
    {
        if (_capture != null)
        {
            _capture.Analyzer.CopyLevels(_levels);
            _view.VisibleBars?.Update(_levels);
        }
        if (_vm.Mode == IslandMode.Expanded) _vm.TickPosition();
    }

    private void EnsureCapture() => _capture ??= LoopbackCapture.Start();

    private void StopCapture()
    {
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose()
    {
        _frameTimer.Stop();
        _alertTimer.Stop();
        StopCapture();
        _window.HideIsland();
    }
}
