using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PulseIsland.Core.Localization;
using PulseIsland.Island;
using PulseIsland.Platform.Shell;

namespace PulseIsland.Mixer;

public sealed partial class MixerView : UserControl
{
    private readonly App _app;

    public MixerViewModel Vm { get; }

    internal MixerView(MixerViewModel vm, App app)
    {
        Vm = vm;
        _app = app;
        InitializeComponent();
        // Focusable root: MixerWindow focuses it on show so stray keystrokes hit no control.
        IsTabStop = true;
        UseSystemFocusVisuals = false;
    }

    private void FillOutputMenu(object sender, object e) => FillDeviceMenu((MenuFlyout)sender, Vm.Outputs, Vm.OutputId);

    private void FillInputMenu(object sender, object e) => FillDeviceMenu((MenuFlyout)sender, Vm.Inputs, Vm.InputId);

    private void FillDeviceMenu(MenuFlyout menu, IEnumerable<DeviceItem> devices, string? currentId)
    {
        menu.Items.Clear();
        foreach (var d in devices)
        {
            var item = new RadioMenuFlyoutItem { Text = d.Name, GroupName = "device", IsChecked = d.Id == currentId };
            item.Click += (_, _) => Vm.SelectDevice(d.Id);
            menu.Items.Add(item);
        }
    }

    private async void OpenSoundSettings(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:sound"));

    private void ShowMoreMenu(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        var language = new MenuFlyoutSubItem { Text = Loc.T("Menu.Language"), Icon = new FontIcon { Glyph = "" } };
        language.Items.Add(LanguageItem(null, Loc.T("Menu.SystemLanguage")));
        language.Items.Add(new MenuFlyoutSeparator());
        foreach (var code in Loc.Available) language.Items.Add(LanguageItem(code, Loc.NativeName(code)));
        menu.Items.Add(language);

        menu.Items.Add(IslandMenu());
        menu.Items.Add(FeaturesMenu());

        var autostart = new ToggleMenuFlyoutItem { Text = Loc.T("Menu.StartWithWindows"), IsChecked = AutoStart.IsEnabled };
        autostart.Click += (_, _) => AutoStart.IsEnabled = autostart.IsChecked;
        menu.Items.Add(autostart);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Menu.Export", "", () => _ = _app.ExportSettingsAsync()));
        menu.Items.Add(Item("Menu.Import", "", () => _ = _app.ImportSettingsAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Menu.Exit", "", _app.Shutdown));

        menu.ShowAt((FrameworkElement)sender);
    }

    private MenuFlyoutSubItem IslandMenu()
    {
        var s = _app.Settings.Island;
        var sub = new MenuFlyoutSubItem { Text = Loc.T("Menu.Island"), Icon = new FontIcon { Glyph = "" } };

        sub.Items.Add(Toggle("Menu.ShowIsland", s.Enabled, v => _app.SetIsland(i => i.Enabled = v)));
        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(Radio("Menu.IslandPrimary", "monitor", s.Monitor == "primary", () => _app.SetIsland(i => i.Monitor = "primary")));
        sub.Items.Add(Radio("Menu.IslandActive", "monitor", s.Monitor == "active", () => _app.SetIsland(i => i.Monitor = "active")));
        sub.Items.Add(new MenuFlyoutSeparator());

        var fullscreen = new MenuFlyoutSubItem { Text = Loc.T("Menu.Fullscreen") };
        foreach (var mode in new[] { "dot", "hide", "show" })
            fullscreen.Items.Add(Radio($"Menu.Fullscreen.{mode}", "fullscreen", s.Fullscreen == mode, () => _app.SetIsland(i => i.Fullscreen = mode)));
        sub.Items.Add(fullscreen);

        sub.Items.Add(Toggle("Menu.TrackToasts", s.TrackToasts, v => _app.SetIsland(i => i.TrackToasts = v)));
        sub.Items.Add(Toggle("Menu.Visualizer", s.Visualizer, v => _app.SetIsland(i => i.Visualizer = v)));
        return sub;
    }

    /// <summary>Health reminders, on-screen indicators and keyboard cleaning.</summary>
    private MenuFlyoutSubItem FeaturesMenu()
    {
        var s = _app.Settings;
        var root = new MenuFlyoutSubItem { Text = Loc.T("Menu.Wellness"), Icon = new FontIcon { Glyph = "" } };
        var w = s.Wellness;
        string Min(int v) => Loc.T("Menu.CleaningMinutes", v);
        string Vol(int v) => IslandViewModel.FormatVolume(v, w.WaterUnit);

        var pomodoro = new MenuFlyoutSubItem { Text = "Pomodoro" };
        pomodoro.Items.Add(Choice("Menu.FocusLength", [15, 20, 25, 30, 45, 50, 60], w.PomodoroWorkMinutes, Min, (a, v) => a.Wellness.PomodoroWorkMinutes = v));
        pomodoro.Items.Add(Choice("Menu.ShortBreak", [3, 5, 10, 15], w.PomodoroShortBreakMinutes, Min, (a, v) => a.Wellness.PomodoroShortBreakMinutes = v));
        pomodoro.Items.Add(Choice("Menu.LongBreak", [10, 15, 20, 30], w.PomodoroLongBreakMinutes, Min, (a, v) => a.Wellness.PomodoroLongBreakMinutes = v));
        pomodoro.Items.Add(Toggle("Menu.Chime", w.Chime, v => _app.ChangeSettings(a => a.Wellness.Chime = v)));
        root.Items.Add(pomodoro);

        var water = new MenuFlyoutSubItem { Text = Loc.T("Menu.Water") };
        water.Items.Add(Toggle("Menu.Remind", w.Hydration, v => _app.ChangeSettings(a => a.Wellness.Hydration = v)));
        water.Items.Add(Choice("Menu.Glass", [100, 150, 200, 250, 330, 500], w.GlassMl, Vol, (a, v) => a.Wellness.GlassMl = v));
        water.Items.Add(Choice("Menu.WaterGoal", [1500, 2000, 2500, 3000, 3500], w.WaterGoalMl, Vol, (a, v) => a.Wellness.WaterGoalMl = v));
        water.Items.Add(Choice("Menu.Every", [30, 45, 60, 90, 120], w.WaterIntervalMinutes, Min, (a, v) => a.Wellness.WaterIntervalMinutes = v));
        var unit = new MenuFlyoutSubItem { Text = Loc.T("Menu.Unit") };
        unit.Items.Add(Radio("ml", "unit", w.WaterUnit == "ml", () => _app.ChangeSettings(a => a.Wellness.WaterUnit = "ml"), localize: false));
        unit.Items.Add(Radio("oz", "unit", w.WaterUnit == "oz", () => _app.ChangeSettings(a => a.Wellness.WaterUnit = "oz"), localize: false));
        water.Items.Add(unit);
        root.Items.Add(water);

        var eyes = new MenuFlyoutSubItem { Text = Loc.T("Menu.EyeRest") };
        eyes.Items.Add(Toggle("Menu.Remind", w.EyeRest, v => _app.ChangeSettings(a => a.Wellness.EyeRest = v)));
        eyes.Items.Add(Choice("Menu.Every", [20, 30, 45, 60], w.EyeRestMinutes, Min, (a, v) => a.Wellness.EyeRestMinutes = v));
        root.Items.Add(eyes);

        var stand = new MenuFlyoutSubItem { Text = Loc.T("Menu.StandUp") };
        stand.Items.Add(Toggle("Menu.Remind", w.StandUp, v => _app.ChangeSettings(a => a.Wellness.StandUp = v)));
        stand.Items.Add(Choice("Menu.Every", [30, 45, 60, 90], w.StandUpMinutes, Min, (a, v) => a.Wellness.StandUpMinutes = v));
        root.Items.Add(stand);
        root.Items.Add(new MenuFlyoutSeparator());

        var osd = new MenuFlyoutSubItem { Text = Loc.T("Menu.Osd") };
        osd.Items.Add(Toggle("Menu.VolumeOsd", s.Osd.Volume, v => _app.ChangeSettings(a => a.Osd.Volume = v)));
        osd.Items.Add(Toggle("Menu.ReplaceFlyout", s.Osd.ReplaceWindowsVolumeFlyout, v => _app.ChangeSettings(a => a.Osd.ReplaceWindowsVolumeFlyout = v)));
        osd.Items.Add(Toggle("Menu.LockKeys", s.Osd.LockKeys, v => _app.ChangeSettings(a => a.Osd.LockKeys = v)));
        osd.Items.Add(Toggle("Menu.Bluetooth", s.Osd.Bluetooth, v => _app.ChangeSettings(a => a.Osd.Bluetooth = v)));
        root.Items.Add(osd);

        var cleaning = new MenuFlyoutSubItem { Text = Loc.T("Menu.Cleaning") };
        foreach (var minutes in new[] { 1, 2, 5 })
        {
            var item = new RadioMenuFlyoutItem { Text = Loc.T("Menu.CleaningMinutes", minutes), GroupName = "cleaning", IsChecked = s.Cleaning.Minutes == minutes };
            item.Click += (_, _) => _app.ChangeSettings(a => a.Cleaning.Minutes = minutes);
            cleaning.Items.Add(item);
        }
        cleaning.Items.Add(new MenuFlyoutSeparator());
        cleaning.Items.Add(Toggle("Menu.CleaningMouse", s.Cleaning.LockMouse, v => _app.ChangeSettings(a => a.Cleaning.LockMouse = v)));
        root.Items.Add(cleaning);
        return root;
    }

    private static ToggleMenuFlyoutItem Toggle(string key, bool isChecked, Action<bool> onChange)
    {
        var item = new ToggleMenuFlyoutItem { Text = Loc.T(key), IsChecked = isChecked };
        item.Click += (_, _) => onChange(item.IsChecked);
        return item;
    }

    private static RadioMenuFlyoutItem Radio(string key, string group, bool isChecked, Action onClick, bool localize = true)
    {
        var item = new RadioMenuFlyoutItem { Text = localize ? Loc.T(key) : key, GroupName = group, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// Submenu of preset values. A current value that isn't a preset (e.g. set with ±5 on the island)
    /// is listed too, so it shows as selected.
    /// </summary>
    private MenuFlyoutSubItem Choice(string key, int[] presets, int current, Func<int, string> label, Action<Core.Settings.AppSettings, int> set)
    {
        var sub = new MenuFlyoutSubItem { Text = $"{Loc.T(key)}: {label(current)}" };
        var group = key + Guid.NewGuid().ToString("N");
        foreach (var v in presets.Append(current).Distinct().Order())
            sub.Items.Add(Radio(label(v), group, v == current, () => _app.ChangeSettings(a => set(a, v)), localize: false));
        return sub;
    }

    private RadioMenuFlyoutItem LanguageItem(string? code, string text)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = text,
            GroupName = "lang",
            IsChecked = _app.Settings.Language == code,
        };
        item.Click += (_, _) => _app.SetLanguage(code);
        return item;
    }

    private static MenuFlyoutItem Item(string key, string glyph, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = Loc.T(key), Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => onClick();
        return item;
    }
}
