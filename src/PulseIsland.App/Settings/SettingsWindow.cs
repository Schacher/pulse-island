using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PulseIsland.Core.Input;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Settings;
using PulseIsland.Platform;
using PulseIsland.Platform.Shell;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace PulseIsland.Settings;

/// <summary>
/// Every setting in one scrollable page. Built in code from a few row helpers (like the menus); each change
/// is saved and applied at once through <see cref="App.ChangeSettings"/>. Rebuilt on language change.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const double WidthDip = 620, HeightDip = 760;
    private readonly App _app;
    private InfoBar _message = null!;

    private AppSettings S => _app.Settings;

    public SettingsWindow(App app)
    {
        _app = app;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PulseIsland.ico"));
        var (work, scale) = Native.MonitorUnderCursor();
        int w = (int)(WidthDip * scale), h = (int)Math.Min(HeightDip * scale, (work.Bottom - work.Top) * 0.9);
        AppWindow.MoveAndResize(new RectInt32((work.Left + work.Right - w) / 2, (work.Top + work.Bottom - h) / 2, w, h));
        Rebuild();
    }

    public void Rebuild()
    {
        Title = $"{Loc.T("App.Name")} — {Loc.T("Settings.Title")}";
        _message = new InfoBar { IsClosable = true };
        var page = new StackPanel { Spacing = 8, Padding = new Thickness(24, 16, 24, 32), MaxWidth = 720 };
        page.Children.Add(new TextBlock { Text = Loc.T("Settings.Title"), Style = Style("TitleTextBlockStyle"), Margin = new Thickness(0, 0, 0, 8) });
        page.Children.Add(_message);

        // General
        page.Children.Add(Section("Settings.General",
            Row("Menu.Language", LanguageBox()),
            Row("Menu.StartWithWindows", Switch(AutoStart.IsEnabled, v => AutoStart.IsEnabled = v))));

        // Shortcuts
        page.Children.Add(Section("Settings.Hotkeys",
            Row("Settings.MixerHotkey", HotkeyBox(S.MixerHotkey, v => S.MixerHotkey = v ?? S.MixerHotkey, allowNone: false)),
            Row("Settings.MicHotkey", HotkeyBox(S.MicMuteHotkey, v => S.MicMuteHotkey = v, allowNone: true))));

        // Island
        var i = S.Island;
        page.Children.Add(Section("Menu.Island",
            Row("Menu.ShowIsland", Switch(i.Enabled, v => Change(a => a.Island.Enabled = v))),
            Row("Settings.Monitor", Combo([("primary", "Menu.IslandPrimary"), ("active", "Menu.IslandActive")], i.Monitor, v => Change(a => a.Island.Monitor = v))),
            Row("Menu.Fullscreen", Combo([("dot", "Menu.Fullscreen.dot"), ("hide", "Menu.Fullscreen.hide"), ("show", "Menu.Fullscreen.show")], i.Fullscreen, v => Change(a => a.Island.Fullscreen = v))),
            Row("Settings.AllowList", AllowListBox()),
            Row("Settings.OffsetX", Number(i.OffsetX, -2000, 2000, v => Change(a => a.Island.OffsetX = v))),
            Row("Settings.OffsetY", Number(i.OffsetY, 0, 400, v => Change(a => a.Island.OffsetY = v))),
            Row("Settings.Scale", Number((int)Math.Round(i.Scale * 100), 75, 150, v => Change(a => a.Island.Scale = v / 100.0), step: 5)),
            Row("Menu.TrackToasts", Switch(i.TrackToasts, v => Change(a => a.Island.TrackToasts = v))),
            Row("Menu.Visualizer", Switch(i.Visualizer, v => Change(a => a.Island.Visualizer = v)))));

        // Pomodoro
        var w = S.Wellness;
        page.Children.Add(Section("Pomodoro",
            Row(Minutes("Menu.FocusLength"), Number(w.PomodoroWorkMinutes, 1, 180, v => Change(a => a.Wellness.PomodoroWorkMinutes = v))),
            Row(Minutes("Menu.ShortBreak"), Number(w.PomodoroShortBreakMinutes, 1, 60, v => Change(a => a.Wellness.PomodoroShortBreakMinutes = v))),
            Row(Minutes("Menu.LongBreak"), Number(w.PomodoroLongBreakMinutes, 1, 120, v => Change(a => a.Wellness.PomodoroLongBreakMinutes = v))),
            Row("Settings.Cycles", Number(w.PomodoroCycles, 1, 12, v => Change(a => a.Wellness.PomodoroCycles = v))),
            Row("Menu.Chime", Switch(w.Chime, v => Change(a => a.Wellness.Chime = v)))));

        // Water (amounts always edited in ml; the unit only changes how the island shows them)
        page.Children.Add(Section("Menu.Water",
            Row("Menu.Remind", Switch(w.Hydration, v => Change(a => a.Wellness.Hydration = v))),
            Row(Ml("Menu.Glass"), Number(w.GlassMl, 50, 2000, v => Change(a => a.Wellness.GlassMl = v), step: 50)),
            Row(Ml("Menu.WaterGoal"), Number(w.WaterGoalMl, 250, 10000, v => Change(a => a.Wellness.WaterGoalMl = v), step: 250)),
            Row(Minutes("Menu.Every"), Number(w.WaterIntervalMinutes, 10, 480, v => Change(a => a.Wellness.WaterIntervalMinutes = v), step: 5)),
            Row("Menu.Unit", Combo([("ml", "ml"), ("oz", "oz")], w.WaterUnit, v => Change(a => a.Wellness.WaterUnit = v), localize: false))));

        // Breaks
        page.Children.Add(Section("Settings.Breaks",
            Row("Menu.EyeRest", Switch(w.EyeRest, v => Change(a => a.Wellness.EyeRest = v))),
            Row(Minutes("Menu.Every"), Number(w.EyeRestMinutes, 5, 120, v => Change(a => a.Wellness.EyeRestMinutes = v), step: 5)),
            Row("Menu.StandUp", Switch(w.StandUp, v => Change(a => a.Wellness.StandUp = v))),
            Row(Minutes("Menu.Every"), Number(w.StandUpMinutes, 10, 240, v => Change(a => a.Wellness.StandUpMinutes = v), step: 5))));

        // Indicators
        var o = S.Osd;
        page.Children.Add(Section("Menu.Osd",
            Row("Menu.VolumeOsd", Switch(o.Volume, v => Change(a => a.Osd.Volume = v))),
            Row("Menu.ReplaceFlyout", Switch(o.ReplaceWindowsVolumeFlyout, v => Change(a => a.Osd.ReplaceWindowsVolumeFlyout = v))),
            Row("Menu.LockKeys", Switch(o.LockKeys, v => Change(a => a.Osd.LockKeys = v))),
            Row("Menu.Bluetooth", Switch(o.Bluetooth, v => Change(a => a.Osd.Bluetooth = v)))));

        // Cleaning
        page.Children.Add(Section("Menu.Cleaning",
            Row(Minutes("Settings.CleaningMinutes"), Number(S.Cleaning.Minutes, 1, 15, v => Change(a => a.Cleaning.Minutes = v))),
            Row("Menu.CleaningMouse", Switch(S.Cleaning.LockMouse, v => Change(a => a.Cleaning.LockMouse = v)))));

        // Backup
        var export = new Button { Content = Loc.T("Menu.Export") };
        export.Click += async (_, _) => await _app.ExportSettingsAsync(Hwnd, Report);
        var import = new Button { Content = Loc.T("Menu.Import") };
        import.Click += async (_, _) => await _app.ImportSettingsAsync(Hwnd, Report);
        page.Children.Add(Section("Settings.Backup", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import } }));

        Content = new ScrollViewer { Content = page, HorizontalScrollMode = ScrollMode.Disabled };
    }

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void Change(Action<AppSettings> change) => _app.ChangeSettings(change);

    private void Report(string text, bool warning)
    {
        _message.Message = text;
        _message.Severity = warning ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        _message.IsOpen = true;
    }

    // ---- row helpers ----

    private static string Minutes(string key) => Loc.T("Settings.Minutes", Loc.T(key));
    private static string Ml(string key) => Loc.T("Settings.Ml", Loc.T(key));

    private static Style Style(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A card with a title. Like <see cref="Row"/>, an unknown key (e.g. "Pomodoro") is shown as is.</summary>
    private static Border Section(string titleKey, params UIElement[] rows)
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock
        {
            Text = Loc.T(titleKey),
            Style = Style("BodyStrongTextBlockStyle"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var r in rows) body.Children.Add(r);
        return new Border
        {
            Child = body,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    /// <summary>A label on the left, its control on the right. Keys are localized; ready-made text is used as is.</summary>
    private static Grid Row(string keyOrText, FrameworkElement control)
    {
        var text = Loc.T(keyOrText);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        AutomationName(control, text);
        var row = new Grid { ColumnSpacing = 16, MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(control, 1);
        row.Children.Add(label);
        row.Children.Add(control);
        return row;
    }

    private static void AutomationName(UIElement e, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(e, name);

    private static ToggleSwitch Switch(bool on, Action<bool> set)
    {
        var t = new ToggleSwitch { IsOn = on, OnContent = null, OffContent = null, MinWidth = 0 };
        t.Toggled += (_, _) => set(t.IsOn);
        return t;
    }

    private static NumberBox Number(int value, int min, int max, Action<int> set, int step = 1)
    {
        var n = new NumberBox
        {
            Value = value,
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = step * 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Width = 150,
        };
        n.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) { n.Value = e.OldValue; return; } // emptied box: keep the old value
            var v = (int)Math.Clamp(Math.Round(e.NewValue), min, max);
            if (v != (int)e.OldValue) set(v);
        };
        return n;
    }

    private static ComboBox Combo((string Value, string Key)[] options, string current, Action<string> set, bool localize = true)
    {
        var c = new ComboBox { MinWidth = 220 };
        foreach (var (_, key) in options) c.Items.Add(localize ? Loc.T(key) : key);
        c.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Value == current));
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) set(options[c.SelectedIndex].Value); };
        return c;
    }

    private ComboBox LanguageBox()
    {
        var codes = new List<string?> { null };
        codes.AddRange(Loc.Available);
        var c = new ComboBox { MinWidth = 220 };
        foreach (var code in codes) c.Items.Add(code == null ? Loc.T("Menu.SystemLanguage") : Loc.NativeName(code));
        c.SelectedIndex = Math.Max(0, codes.IndexOf(S.Language));
        // Deferred: SetLanguage rebuilds this page, which must not happen inside the ComboBox's own event.
        c.SelectionChanged += (_, _) =>
        {
            var code = codes[Math.Max(0, c.SelectedIndex)];
            if (code != S.Language) DispatcherQueue.TryEnqueue(() => _app.SetLanguage(code));
        };
        return c;
    }

    private TextBox AllowListBox()
    {
        var box = new TextBox
        {
            Width = 220,
            Text = string.Join(", ", S.Island.FullscreenAllowList),
            PlaceholderText = Loc.T("Settings.AllowListHint"),
        };
        box.LostFocus += (_, _) => Change(a => a.Island.FullscreenAllowList = box.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Select(x => x.EndsWith(".exe") ? x : x + ".exe")
            .Distinct().ToList());
        return box;
    }

    /// <summary>
    /// Click, then press the combination. Our global shortcuts are paused while recording, otherwise
    /// Windows would deliver the current shortcut to us instead of the box.
    /// </summary>
    private StackPanel HotkeyBox(string? current, Action<string?> set, bool allowNone)
    {
        var box = new TextBox
        {
            Width = 170,
            IsReadOnly = true,
            Text = string.IsNullOrWhiteSpace(current) ? Loc.T("Settings.HotkeyNone") : current,
            PlaceholderText = Loc.T("Settings.HotkeyHint"),
        };
        ToolTipService.SetToolTip(box, Loc.T("Settings.HotkeyHint"));
        box.GotFocus += (_, _) => _app.SuspendHotkeys();
        box.LostFocus += (_, _) => _app.RegisterHotkeys();
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if (Recorded(e.Key) is not { } text) return;
            if (!Hotkey.TryParse(text, out Hotkey _)) return; // needs a modifier
            box.Text = text;
            Change(a => set(text));
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { box } };
        if (allowNone)
        {
            var clear = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 } };
            ToolTipService.SetToolTip(clear, Loc.T("Settings.Clear"));
            AutomationName(clear, Loc.T("Settings.Clear"));
            clear.Click += (_, _) =>
            {
                box.Text = Loc.T("Settings.HotkeyNone");
                Change(a => set(null));
                _app.RegisterHotkeys();
            };
            panel.Children.Add(clear);
        }
        return panel;
    }

    private static string? Recorded(VirtualKey key)
    {
        string? name = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => ((char)key).ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)key).ToString(),
            >= VirtualKey.F1 and <= VirtualKey.F24 => $"F{key - VirtualKey.F1 + 1}",
            VirtualKey.Space => "Space",
            VirtualKey.Pause => "Pause",
            VirtualKey.Insert => "Insert",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            _ => null, // modifiers alone, or keys we don't support
        };
        if (name == null) return null;
        static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);
        var parts = new List<string>();
        if (Down(VirtualKey.Control)) parts.Add("Ctrl");
        if (Down(VirtualKey.Menu)) parts.Add("Alt");
        if (Down(VirtualKey.Shift)) parts.Add("Shift");
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) parts.Add("Win");
        parts.Add(name);
        return string.Join("+", parts);
    }
}
