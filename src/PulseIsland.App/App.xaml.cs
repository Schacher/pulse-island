using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using PulseIsland.Core.Input;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Settings;
using PulseIsland.Island;
using PulseIsland.Mixer;
using PulseIsland.Platform;
using PulseIsland.Platform.Audio;
using PulseIsland.Platform.Media;
using PulseIsland.Platform.Shell;
using Windows.Storage.Pickers;

namespace PulseIsland;

/// <summary>Composition root: owns settings, audio, the tray/hotkey window and the mixer flyout.</summary>
public partial class App : Application
{
    private const int HotkeyMixer = 1, HotkeyMic = 2;
    private const int MenuOpen = 1, MenuExit = 2, MenuIsland = 3, MenuCleaning = 4;

    internal static string AppVersion { get; } = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    internal static string WindowsBuild { get; } = ReadWindowsBuild();

    /// <summary>Set by Program: signalled when the user launches a second instance.</summary>
    internal static EventWaitHandle? ShowSignal { get; set; }

    private SettingsStore _store = null!;
    private DispatcherQueue _ui = null!;
    private DispatcherQueueTimer _saveTimer = null!;
    private AudioService _audio = null!;
    private MixerViewModel _vm = null!;
    private MixerWindow? _mixerWindow;
    private ForegroundWatcher _foreground = null!;
    private MediaService _media = null!;
    private IslandController _island = null!;
    private WellnessController _wellness = null!;
    private SystemOsdController _osd = null!;
    private ShellWindow _shell = null!;
    private RegisteredWaitHandle? _showWait;

    internal AppSettings Settings { get; private set; } = null!;

    /// <summary>Created on first open: a never-opened flyout costs no XAML memory (autostart sits in the tray).</summary>
    private MixerWindow Mixer => _mixerWindow ??= new MixerWindow(_vm, this);

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("unhandled", e.Exception);
            e.Handled = true; // a tray utility should survive a bad event handler
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown; // lives in the tray with no window
        _ui = DispatcherQueue.GetForCurrentThread();

        _store = new SettingsStore(SettingsStore.DefaultPath);
        Settings = _store.Load();
        Loc.SetLanguage(Loc.Resolve(Settings.Language, CultureInfo.CurrentUICulture));

        _saveTimer = _ui.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();

        _audio = new AudioService(_ui);
        _vm = new MixerViewModel(_audio, this);

        _shell = new ShellWindow(Path.Combine(AppContext.BaseDirectory, "Assets", "PulseIsland.ico"));
        _shell.TrayClicked += () => Mixer.Toggle();
        _shell.TrayMenuRequested += ShowTrayMenu;
        _shell.HotkeyPressed += OnHotkey;
        _shell.ShowTrayIcon(Loc.T("Tray.Tooltip"));

        RegisterHotkeys();
        ProbeFeatures();
        AutoStart.RepairPath();

        if (ShowSignal != null)
            _showWait = ThreadPool.RegisterWaitForSingleObject(ShowSignal, (_, _) => _ui.TryEnqueue(() => Mixer.Show()), null, -1, false);

        if (!Settings.FirstRunHintShown)
        {
            _shell.ShowBalloon(Loc.T("App.Name"), Loc.T("Tray.Running", Settings.MixerHotkey));
            Settings.FirstRunHintShown = true;
            SaveSettings();
        }
        _foreground = new ForegroundWatcher();
        _shell.RegisterAppBar();
        _shell.FullscreenAppChanged += _foreground.Evaluate;
        _media = new MediaService(_ui);
        _island = new IslandController(this, _media, _vm, _foreground, _ui);
        _shell.DisplayChanged += _island.Reposition;
        _island.Start();
        _wellness = new WellnessController(this, _island, _ui);
        _osd = new SystemOsdController(this, _island, _audio, _ui);
        _osd.VolumeChangedByKeys += _vm.SyncAll;
        _ = _media.StartAsync();

        if (!Environment.GetCommandLineArgs().Contains(AutoStart.Argument)) Mixer.Show();
        Log.Info($"started {AppVersion} on Windows {WindowsBuild}");
    }

    // ---- settings ----

    /// <summary>Debounced save (slider drags, profile edits).</summary>
    internal void SaveSettings()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        try { _store.Save(Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("save settings", e); }
    }

    internal void SetLanguage(string? code)
    {
        Settings.Language = code;
        SaveSettings();
        Loc.SetLanguage(Loc.Resolve(code, CultureInfo.CurrentUICulture));
        _vm.RefreshTexts();
        _island.RefreshTexts();
        _wellness.RefreshTexts();
        _osd.ApplySettings();
        _mixerWindow?.Rebuild();
        _shell.SetTooltip(Loc.T("Tray.Tooltip"));
    }

    internal async Task ExportSettingsAsync()
    {
        var picker = new FileSavePicker { SuggestedFileName = "PulseIsland-settings" };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Mixer.Hwnd);
        Mixer.SuppressAutoHide = true;
        try
        {
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            _store.Export(Settings, file.Path);
            _vm.ShowMessage(Loc.T("Export.Done"));
        }
        catch (Exception e)
        {
            Log.Error("export", e);
            _vm.ShowMessage(e.Message, warning: true);
        }
        finally { Mixer.SuppressAutoHide = false; }
    }

    internal async Task ImportSettingsAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Mixer.Hwnd);
        Mixer.SuppressAutoHide = true;
        try
        {
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var imported = SettingsStore.TryImport(file.Path);
            if (imported == null)
            {
                _vm.ShowMessage(Loc.T("Import.Failed"), warning: true);
                return;
            }
            imported.BrokenFeatures = Settings.BrokenFeatures; // machine-specific, never taken from a backup
            imported.FirstRunHintShown = true;
            Settings = imported;
            SaveNow();
            _vm.ReloadProfiles();
            RegisterHotkeys();
            SetLanguage(imported.Language);
            ApplyIslandSettings();
            _vm.ShowMessage(Loc.T("Import.Done"));
        }
        catch (Exception e)
        {
            Log.Error("import", e);
            _vm.ShowMessage(e.Message, warning: true);
        }
        finally { Mixer.SuppressAutoHide = false; }
    }

    // ---- undocumented features ----

    private void ProbeFeatures()
    {
        if (FeatureGate.IsBroken(Settings, FeatureGate.DefaultDevice, WindowsBuild, AppVersion))
            _vm.CanSwitchDevices = false; // user was already told on this build
        else if (AudioService.ProbeDefaultDeviceApi() is { } error)
            ReportBroken(FeatureGate.DefaultDevice, error);
        SaveSettings(); // IsBroken may have dropped a stale entry
    }

    /// <summary>Turns the feature off, tells the user once, and remembers it until Windows or the app updates.</summary>
    internal void ReportBroken(string feature, string error)
    {
        Log.Info($"feature {feature} broken on {WindowsBuild}: {error}");
        FeatureGate.MarkBroken(Settings, feature, WindowsBuild, AppVersion, error);
        SaveSettings();
        if (feature == FeatureGate.DefaultDevice) _vm.CanSwitchDevices = false;
        var message = Loc.T($"Feature.Broken.{feature}");
        _shell.ShowBalloon(Loc.T("App.Name"), message, warning: true);
        _vm.ShowMessage(message, warning: true);
    }

    // ---- tray & hotkeys ----

    private void RegisterHotkeys()
    {
        _shell.UnregisterHotkey(HotkeyMixer);
        _shell.UnregisterHotkey(HotkeyMic);
        Register(HotkeyMixer, Settings.MixerHotkey);
        if (!string.IsNullOrWhiteSpace(Settings.MicMuteHotkey)) Register(HotkeyMic, Settings.MicMuteHotkey);
    }

    private void Register(int id, string text)
    {
        if (!Hotkey.TryParse(text, out var hotkey))
            _vm.ShowMessage(Loc.T("Hotkey.Invalid", text), warning: true);
        else if (!_shell.RegisterHotkey(id, hotkey))
            _vm.ShowMessage(Loc.T("Hotkey.Failed", text), warning: true);
    }

    private void OnHotkey(int id)
    {
        if (id == HotkeyMixer) Mixer.Toggle();
        else if (id == HotkeyMic && _vm.ToggleMicMute() is { } muted) _osd.ShowMic(muted);
    }

    private void ShowTrayMenu(int x, int y)
    {
        var island = Loc.T(Settings.Island.Enabled ? "Menu.HideIsland" : "Menu.ShowIsland");
        var chosen = _shell.ShowMenu(x, y,
            [(MenuOpen, Loc.T("Tray.OpenMixer")), (MenuIsland, island), (MenuCleaning, Loc.T("Cleaning.Button", Settings.Cleaning.Minutes)),
             (0, ""), (MenuExit, Loc.T("Tray.Exit"))]);
        if (chosen == MenuOpen) Mixer.Show();
        else if (chosen == MenuIsland) SetIsland(s => s.Enabled = !s.Enabled);
        else if (chosen == MenuCleaning) _osd.StartCleaning();
        else if (chosen == MenuExit) Shutdown();
    }

    /// <summary>Changes an island setting, saves, and applies it right away.</summary>
    internal void SetIsland(Action<IslandSettings> change)
    {
        change(Settings.Island);
        SaveSettings();
        ApplyIslandSettings();
    }

    /// <summary>Changes a Pulse setting group (island, wellness, OSD, cleaning), saves and applies it.</summary>
    internal void ChangeSettings(Action<AppSettings> change)
    {
        change(Settings);
        SaveSettings();
        ApplyIslandSettings();
    }

    private void ApplyIslandSettings()
    {
        _island.ApplySettings();
        _wellness.ApplySettings();
        _osd.ApplySettings();
    }

    internal void Shutdown()
    {
        _saveTimer.Stop();
        SaveNow();
        _showWait?.Unregister(null);
        _mixerWindow?.Hide();
        _osd.Dispose(); // releases the keyboard if a cleaning lock is active
        _wellness.Dispose();
        _island.Dispose();
        _foreground.Dispose();
        _shell.Dispose();
        _audio.Dispose();
        Exit();
    }

    private static string ReadWindowsBuild()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return $"{key?.GetValue("CurrentBuildNumber") ?? Environment.OSVersion.Version.Build}.{key?.GetValue("UBR") ?? 0}";
    }
}
