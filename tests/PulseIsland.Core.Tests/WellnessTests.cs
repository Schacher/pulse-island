using PulseIsland.Core.Settings;
using PulseIsland.Core.Wellness;

namespace PulseIsland.Core.Tests;

public class PomodoroTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0);
    private static DateTime At(double minutes) => T0.AddMinutes(minutes);

    [Fact]
    public void RunsWorkBreakCyclesWithLongBreak()
    {
        var p = new PomodoroEngine { CyclesBeforeLongBreak = 2 };
        p.Start(At(0));
        Assert.Null(p.Tick(At(24)));
        Assert.Equal(TimeSpan.FromMinutes(1), p.Remaining(At(24)));

        Assert.Equal(PomodoroPhase.Work, p.Tick(At(25)));
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase);
        Assert.Equal(PomodoroPhase.ShortBreak, p.Tick(At(30)));
        Assert.Equal(PomodoroPhase.Work, p.Tick(At(55)));
        Assert.Equal(PomodoroPhase.LongBreak, p.Phase); // 2nd work done
        Assert.Equal(2, p.CompletedWork);
    }

    [Fact]
    public void LateTickDoesNotShortenNextPhase()
    {
        var p = new PomodoroEngine();
        p.Start(At(0));
        p.Tick(At(27)); // checked 2 min late (e.g. PC was asleep briefly)
        Assert.Equal(TimeSpan.FromMinutes(3), p.Remaining(At(27))); // break began at 25
    }

    [Fact]
    public void PauseFreezesTime()
    {
        var p = new PomodoroEngine();
        p.Start(At(0));
        p.Pause(At(10));
        Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(At(40)));
        Assert.Null(p.Tick(At(40)));
        p.Resume(At(40));
        Assert.Equal(PomodoroPhase.Work, p.Tick(At(55)));
        Assert.Equal(0.6, p.Progress(At(58)), 3); // 3 of 5 break minutes
    }

    [Fact]
    public void SkipAndStop()
    {
        var p = new PomodoroEngine();
        p.Start(At(0));
        p.Skip(At(3));
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase);
        p.Stop();
        Assert.False(p.IsActive);
        Assert.Equal(0, p.Progress(At(4)));
    }
}

public class HydrationTests
{
    [Fact]
    public void NudgesOnIntervalUntilGoal()
    {
        var t0 = new DateTime(2026, 1, 1, 9, 0, 0);
        var h = new HydrationTracker { GoalMl = 500, Interval = TimeSpan.FromMinutes(60) };
        h.Restore(default, 0, t0);
        Assert.False(h.ReminderDue(t0.AddMinutes(59)));
        Assert.True(h.ReminderDue(t0.AddMinutes(60)));
        Assert.False(h.ReminderDue(t0.AddMinutes(61))); // interval restarts after a nudge

        h.Log(250, t0.AddMinutes(90));
        Assert.False(h.ReminderDue(t0.AddMinutes(149))); // drinking also restarts it
        h.Log(250, t0.AddMinutes(150));
        Assert.True(h.GoalReached);
        Assert.False(h.ReminderDue(t0.AddHours(8)));
    }

    [Fact]
    public void RemoveNeverGoesNegativeAndResetZeroes()
    {
        var t0 = new DateTime(2026, 1, 1, 9, 0, 0);
        var h = new HydrationTracker();
        h.Restore(default, 0, t0);
        h.Log(250, t0);
        h.Log(250, t0);
        h.Remove(250, t0);
        Assert.Equal(250, h.ConsumedMl);
        h.Remove(500, t0);
        Assert.Equal(0, h.ConsumedMl);
        h.Log(330, t0);
        h.Reset(t0);
        Assert.Equal(0, h.ConsumedMl);
    }

    [Fact]
    public void ResetsAtMidnightAndRestoresSameDay()
    {
        var evening = new DateTime(2026, 1, 1, 23, 0, 0);
        var h = new HydrationTracker();
        h.Restore(DateOnly.FromDateTime(evening), 1500, evening);
        Assert.Equal(1500, h.ConsumedMl);
        h.Log(250, evening.AddHours(2)); // 01:00 next day
        Assert.Equal(250, h.ConsumedMl);

        var other = new HydrationTracker();
        other.Restore(new DateOnly(2025, 12, 31), 1500, evening);
        Assert.Equal(0, other.ConsumedMl);
    }
}

public class ActivityTimerTests
{
    [Fact]
    public void RemindsAfterContinuousActivityAndIdleCountsAsBreak()
    {
        var t0 = new DateTime(2026, 1, 1, 9, 0, 0);
        var eye = new ActivityTimer(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(1));
        for (var m = 0; m < 20; m++) Assert.False(eye.Tick(t0.AddMinutes(m), TimeSpan.Zero));
        Assert.True(eye.Tick(t0.AddMinutes(20), TimeSpan.Zero));

        // 15 min of work, then 2 min away → counter resets.
        for (var m = 21; m <= 35; m++) eye.Tick(t0.AddMinutes(m), TimeSpan.Zero);
        eye.Tick(t0.AddMinutes(37), TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.Zero, eye.Active);
    }

    [Fact]
    public void SnoozeRemindsSooner()
    {
        var t0 = new DateTime(2026, 1, 1, 9, 0, 0);
        var stand = new ActivityTimer(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        stand.Tick(t0, TimeSpan.Zero);
        stand.Snooze(TimeSpan.FromMinutes(10));
        Assert.False(stand.Tick(t0.AddMinutes(9), TimeSpan.Zero));
        Assert.True(stand.Tick(t0.AddMinutes(10), TimeSpan.Zero));
    }
}

public class WellnessSettingsTests
{
    [Fact]
    public void ClampsInsaneDurations()
    {
        var dir = Directory.CreateTempSubdirectory("pulse-test-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, """{ "Wellness": { "PomodoroWorkMinutes": 0, "WaterIntervalMinutes": 1, "GlassMl": -5 }, "Cleaning": { "Minutes": 999 } }""");
            var s = new SettingsStore(path).Load();
            Assert.Equal(1, s.Wellness.PomodoroWorkMinutes);
            Assert.Equal(10, s.Wellness.WaterIntervalMinutes);
            Assert.Equal(50, s.Wellness.GlassMl);
            Assert.Equal(15, s.Cleaning.Minutes);
            Assert.NotNull(s.Osd);
        }
        finally { Directory.Delete(dir, true); }
    }
}
