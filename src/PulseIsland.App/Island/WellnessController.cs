using Microsoft.UI.Dispatching;
using PulseIsland.Core.Island;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Settings;
using PulseIsland.Core.Wellness;
using PulseIsland.Platform;

namespace PulseIsland.Island;

/// <summary>
/// Pomodoro, water and break reminders. A 30 s timer checks reminders (and idle time, so being away counts
/// as a break and pauses nudges); a 1 s timer runs only while a Pomodoro is counting down.
/// </summary>
internal sealed class WellnessController : IDisposable
{
    private static readonly TimeSpan Away = TimeSpan.FromMinutes(5);

    private readonly App _app;
    private readonly IslandController _island;
    private readonly IslandViewModel _vm;
    private readonly DispatcherQueueTimer _slowTimer, _pomodoroTimer;
    private readonly PomodoroEngine _pomodoro = new();
    private readonly HydrationTracker _water = new();
    private readonly ActivityTimer _eyes = new(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(1));
    private readonly ActivityTimer _stand = new(TimeSpan.FromMinutes(50), Away);

    private WellnessSettings Settings => _app.Settings.Wellness;

    public WellnessController(App app, IslandController island, DispatcherQueue ui)
    {
        _app = app;
        _island = island;
        _vm = island.Vm;

        _slowTimer = ui.CreateTimer();
        _slowTimer.Interval = TimeSpan.FromSeconds(30);
        _slowTimer.Tick += (_, _) => CheckReminders();
        _pomodoroTimer = ui.CreateTimer();
        _pomodoroTimer.Interval = TimeSpan.FromSeconds(1);
        _pomodoroTimer.Tick += (_, _) => PomodoroTick();

        _vm.PomodoroStartPauseRequested = StartPause;
        _vm.PomodoroSkipRequested = () => { _pomodoro.Skip(DateTime.Now); Refresh(); };
        _vm.PomodoroStopRequested = () => { _pomodoro.Stop(); Refresh(); };
        _vm.AddWaterRequested = () => AddWater(showOsd: false);
        _vm.RemoveWaterRequested = () => { _water.Remove(Settings.GlassMl, DateTime.Now); SaveWater(); };
        _vm.ResetWaterRequested = () => { _water.Reset(DateTime.Now); SaveWater(); };
        _vm.PomodoroLengthChangeRequested = delta =>
            _app.ChangeSettings(s => s.Wellness.PomodoroWorkMinutes = Math.Clamp(s.Wellness.PomodoroWorkMinutes + delta, 5, 120));

        _water.Restore(DateOnly.TryParse(Settings.WaterDay, out var day) ? day : default, Settings.WaterTodayMl, DateTime.Now);
        ApplySettings();
        _slowTimer.Start();
    }

    public void ApplySettings()
    {
        _pomodoro.Work = TimeSpan.FromMinutes(Settings.PomodoroWorkMinutes);
        _pomodoro.ShortBreak = TimeSpan.FromMinutes(Settings.PomodoroShortBreakMinutes);
        _pomodoro.LongBreak = TimeSpan.FromMinutes(Settings.PomodoroLongBreakMinutes);
        _pomodoro.CyclesBeforeLongBreak = Settings.PomodoroCycles;
        _water.GoalMl = Settings.WaterGoalMl;
        _water.Interval = TimeSpan.FromMinutes(Settings.WaterIntervalMinutes);
        _eyes.Threshold = TimeSpan.FromMinutes(Settings.EyeRestMinutes);
        _stand.Threshold = TimeSpan.FromMinutes(Settings.StandUpMinutes);
        Refresh();
    }

    public void RefreshTexts() => Refresh();

    private void StartPause()
    {
        var now = DateTime.Now;
        if (!_pomodoro.IsActive) _pomodoro.Start(now);
        else if (_pomodoro.IsRunning) _pomodoro.Pause(now);
        else _pomodoro.Resume(now);
        Refresh();
    }

    private void PomodoroTick()
    {
        var finished = _pomodoro.Tick(DateTime.Now);
        if (finished != null)
        {
            if (Settings.Chime) Native.PlaySystemSound("Notification.Reminder");
            var toWork = finished != PomodoroPhase.Work;
            _island.Post(new IslandAlert("pomodoro", AlertPriority.Alarm, TimeSpan.FromSeconds(12), new CardPayload(
                toWork ? "🎯" : "🍅",
                Loc.T(toWork ? "Pomodoro.BackToWork" : "Pomodoro.BreakTime"),
                toWork ? null : Loc.T("Pomodoro.Completed", _pomodoro.CompletedWork),
                new CardAction(Loc.T("Common.Ok"), () => { }),
                toWork ? null : new CardAction(Loc.T("Pomodoro.Skip"), () => { _pomodoro.Skip(DateTime.Now); Refresh(); }),
                toWork ? Windows.UI.Color.FromArgb(255, 120, 60, 40) : Windows.UI.Color.FromArgb(255, 40, 90, 50))));
        }
        Refresh();
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        _vm.SetPomodoro(_pomodoro.IsActive, _pomodoro.IsRunning, _pomodoro.Phase, _pomodoro.Remaining(now), _pomodoro.Progress(now));
        _vm.SetWater(_water.ConsumedMl, _water.GoalMl, Settings.GlassMl, Settings.WaterUnit);
        if (_pomodoro.IsRunning) _pomodoroTimer.Start(); else _pomodoroTimer.Stop();
        _island.PomodoroChanged();
    }

    private void AddWater(bool showOsd)
    {
        _water.Log(Settings.GlassMl, DateTime.Now);
        SaveWater();
        if (showOsd)
            _island.Post(new IslandAlert("water-log", AlertPriority.Osd, TimeSpan.FromSeconds(1.6), new OsdPayload("",
                Loc.T("Water.Progress", $"{IslandViewModel.FormatVolume(_water.ConsumedMl, Settings.WaterUnit)} / {IslandViewModel.FormatVolume(_water.GoalMl, Settings.WaterUnit)}"),
                Math.Min(1, _water.ConsumedMl / (double)_water.GoalMl))));
    }

    /// <summary>Today's total survives restarts.</summary>
    private void SaveWater()
    {
        Settings.WaterDay = _water.Day.ToString("yyyy-MM-dd");
        Settings.WaterTodayMl = _water.ConsumedMl;
        _app.SaveSettings();
        Refresh();
    }

    private void CheckReminders()
    {
        var now = DateTime.Now;
        var idle = Native.IdleTime();
        var away = idle >= Away;

        if (Settings.Hydration && !away && _water.ReminderDue(now))
            _island.Post(new IslandAlert("water", AlertPriority.Toast, TimeSpan.FromSeconds(12), new CardPayload("💧",
                Loc.T("Water.Reminder"),
                Loc.T("Water.Progress", $"{IslandViewModel.FormatVolume(_water.ConsumedMl, Settings.WaterUnit)} / {IslandViewModel.FormatVolume(_water.GoalMl, Settings.WaterUnit)}"),
                new CardAction("+" + IslandViewModel.FormatVolume(Settings.GlassMl, Settings.WaterUnit), () => AddWater(showOsd: true)),
                new CardAction(Loc.T("Common.Later"), () => { }),
                Windows.UI.Color.FromArgb(255, 30, 80, 110))));

        if (_eyes.Tick(now, idle) && Settings.EyeRest)
            _island.Post(new IslandAlert("eyes", AlertPriority.Toast, TimeSpan.FromSeconds(20), new CardPayload("👀",
                Loc.T("EyeRest.Title"), Loc.T("EyeRest.Text"), new CardAction(Loc.T("Common.Ok"), () => { }),
                Color: Windows.UI.Color.FromArgb(255, 60, 60, 100))));

        if (_stand.Tick(now, idle) && Settings.StandUp)
            _island.Post(new IslandAlert("stand", AlertPriority.Alarm, TimeSpan.FromSeconds(20), new CardPayload("🧍",
                Loc.T("StandUp.Title"), Loc.T("StandUp.Text", Settings.StandUpMinutes),
                new CardAction(Loc.T("Common.Ok"), () => { }),
                new CardAction(Loc.T("StandUp.Snooze"), () => _stand.Snooze(TimeSpan.FromMinutes(10))),
                Windows.UI.Color.FromArgb(255, 90, 70, 30))));
    }

    public void Dispose()
    {
        _slowTimer.Stop();
        _pomodoroTimer.Stop();
    }
}
