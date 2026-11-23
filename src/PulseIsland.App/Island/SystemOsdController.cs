using Microsoft.UI.Dispatching;
using PulseIsland.Core.Island;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Settings;
using PulseIsland.Platform.Audio;
using PulseIsland.Platform.Devices;
using PulseIsland.Platform.Input;

namespace PulseIsland.Island;

/// <summary>Volume, microphone, lock-key and Bluetooth feedback on the island, and the keyboard cleaning lock.</summary>
internal sealed class SystemOsdController : IDisposable
{
    private const float VolumeStep = 0.02f; // same 2 % step as Windows' own volume keys

    private readonly App _app;
    private readonly IslandController _island;
    private readonly AudioService _audio;
    private readonly InputHooks _hooks;
    private readonly BluetoothWatcher _bluetooth;
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _cleaningTimer;
    private TimeSpan _cleaningTotal;

    private OsdSettings Settings => _app.Settings.Osd;

    /// <summary>Raised after we changed the output volume ourselves (the mixer refreshes its sliders).</summary>
    public event Action? VolumeChangedByKeys;

    public SystemOsdController(App app, IslandController island, AudioService audio, DispatcherQueue ui)
    {
        _app = app;
        _island = island;
        _audio = audio;
        _ui = ui;
        _hooks = new InputHooks();
        _bluetooth = new BluetoothWatcher(ui);

        _hooks.VolumeKey += vk => _ui.TryEnqueue(() => OnVolumeKey(vk));
        _hooks.LockKey += vk => _ui.TryEnqueue(() => OnLockKey(vk));
        _hooks.Unlocked += () => _ui.TryEnqueue(FinishCleaning);
        _hooks.UnlockHoldProgress += p => _ui.TryEnqueue(() => _island.Vm.SetCleaningHold(p));
        _audio.OutputVolumeChanged += () => { if (Settings.Volume) ShowVolume(); };
        _bluetooth.Connected += OnBluetooth;

        _island.Vm.CleaningRequested = StartCleaning;
        _island.Vm.UnlockRequested = _hooks.Unlock;
        _cleaningTimer = ui.CreateTimer();
        _cleaningTimer.Interval = TimeSpan.FromMilliseconds(250);
        _cleaningTimer.Tick += (_, _) => _island.Vm.SetCleaning(_hooks.LockRemaining, _cleaningTotal);
        ApplySettings();
    }

    public void ApplySettings()
    {
        _hooks.HandleVolumeKeys = Settings.ReplaceWindowsVolumeFlyout;
        _island.Vm.SetCleaningButton(_app.Settings.Cleaning.Minutes);
    }

    public void ShowMic(bool muted) =>
        _island.Post(new IslandAlert("mic", AlertPriority.Osd, TimeSpan.FromSeconds(1.6),
            new OsdPayload(muted ? "" : "", Loc.T(muted ? "Mic.Muted" : "Mic.Unmuted"))));

    // ---- volume ----

    private void OnVolumeKey(uint vk)
    {
        var output = _audio.Output;
        if (output == null) return;
        if (vk == InputHooks.VolumeMute) output.Muted = !output.Muted;
        else
        {
            output.Muted = false; // like Windows: any volume key unmutes
            var step = vk == InputHooks.VolumeUp ? VolumeStep : -VolumeStep;
            output.Volume = MathF.Round((output.Volume + step) / VolumeStep) * VolumeStep;
        }
        VolumeChangedByKeys?.Invoke();
        if (Settings.Volume) ShowVolume();
    }

    private void ShowVolume()
    {
        var output = _audio.Output;
        if (output == null) return;
        var level = output.Muted ? 0 : output.Volume;
        var glyph = output.Muted || level == 0 ? "" : level < 0.34f ? "" : level < 0.67f ? "" : "";
        _island.Post(new IslandAlert("volume", AlertPriority.Osd, TimeSpan.FromSeconds(1.5), new OsdPayload(glyph, "", level)));
    }

    // ---- lock keys ----

    private void OnLockKey(uint vk)
    {
        if (!Settings.LockKeys) return;
        var name = vk == InputHooks.CapsLock ? "Caps Lock" : vk == InputHooks.NumLock ? "Num Lock" : "Scroll Lock";
        var on = InputHooks.IsToggled(vk);
        _island.Post(new IslandAlert("lockkey", AlertPriority.Osd, TimeSpan.FromSeconds(1.3),
            new OsdPayload("", Loc.T(on ? "LockKey.On" : "LockKey.Off", name))));
    }

    // ---- bluetooth ----

    private void OnBluetooth(string name, int? battery)
    {
        if (!Settings.Bluetooth) return;
        _island.Post(new IslandAlert("bluetooth", AlertPriority.Toast, TimeSpan.FromSeconds(4), new CardPayload(
            "", Loc.T("Bluetooth.Connected", name),
            battery is { } b ? Loc.T("Bluetooth.Battery", b) : null,
            Color: Windows.UI.Color.FromArgb(255, 20, 70, 140))));
    }

    // ---- cleaning ----

    public void StartCleaning()
    {
        if (_hooks.IsLocked) return;
        var c = _app.Settings.Cleaning;
        _cleaningTotal = TimeSpan.FromMinutes(c.Minutes);
        _hooks.Lock(_cleaningTotal, c.LockMouse, _island.ScreenRect);
        // The alert outlives the lock a little; FinishCleaning dismisses it the moment the lock ends.
        _island.Post(new IslandAlert("cleaning", AlertPriority.Modal, _cleaningTotal + TimeSpan.FromSeconds(30)));
        _island.Vm.SetCleaning(_cleaningTotal, _cleaningTotal);
        _island.Vm.SetCleaningHold(0);
        _cleaningTimer.Start();
    }

    private void FinishCleaning()
    {
        _cleaningTimer.Stop();
        _island.Vm.SetCleaningHold(0);
        _island.Dismiss("cleaning");
        _island.Post(new IslandAlert("cleaning-done", AlertPriority.Osd, TimeSpan.FromSeconds(1.8),
            new OsdPayload("", Loc.T("Cleaning.Done"))));
    }

    public void Dispose()
    {
        _cleaningTimer.Stop();
        _hooks.Dispose(); // also releases any active lock
        _bluetooth.Dispose();
    }
}
