namespace PulseIsland.Core.Wellness;

public enum PomodoroPhase { Work, ShortBreak, LongBreak }

/// <summary>Pomodoro timer. Pure state machine: the caller supplies the clock. UI thread only.</summary>
public sealed class PomodoroEngine
{
    private DateTime _endsAt;
    private TimeSpan _pausedRemaining;

    public TimeSpan Work { get; set; } = TimeSpan.FromMinutes(25);
    public TimeSpan ShortBreak { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan LongBreak { get; set; } = TimeSpan.FromMinutes(15);
    public int CyclesBeforeLongBreak { get; set; } = 4;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Work;
    /// <summary>Started and not stopped (may be paused).</summary>
    public bool IsActive { get; private set; }
    public bool IsRunning { get; private set; }
    public int CompletedWork { get; private set; }

    public TimeSpan PhaseDuration => Phase switch
    {
        PomodoroPhase.ShortBreak => ShortBreak,
        PomodoroPhase.LongBreak => LongBreak,
        _ => Work,
    };

    public void Start(DateTime now)
    {
        IsActive = true;
        IsRunning = true;
        Phase = PomodoroPhase.Work;
        CompletedWork = 0;
        _endsAt = now + Work;
    }

    public void Pause(DateTime now)
    {
        if (!IsRunning) return;
        _pausedRemaining = Remaining(now);
        IsRunning = false;
    }

    public void Resume(DateTime now)
    {
        if (!IsActive || IsRunning) return;
        _endsAt = now + _pausedRemaining;
        IsRunning = true;
    }

    public void Stop()
    {
        IsActive = false;
        IsRunning = false;
        Phase = PomodoroPhase.Work;
        CompletedWork = 0;
    }

    /// <summary>Ends the current phase now and starts the next one.</summary>
    public void Skip(DateTime now)
    {
        if (IsActive) Advance(now);
    }

    public TimeSpan Remaining(DateTime now)
    {
        if (!IsActive) return Work;
        if (!IsRunning) return _pausedRemaining;
        var left = _endsAt - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>0 at the start of the phase, 1 when it ends.</summary>
    public double Progress(DateTime now) =>
        IsActive ? Math.Clamp(1 - Remaining(now) / PhaseDuration, 0, 1) : 0;

    /// <summary>Returns the phase that just finished (the next one has already started), else null.</summary>
    public PomodoroPhase? Tick(DateTime now)
    {
        if (!IsRunning || now < _endsAt) return null;
        var finished = Phase;
        Advance(_endsAt); // chain from the exact end, so a late tick doesn't shorten the next phase
        return finished;
    }

    private void Advance(DateTime from)
    {
        if (Phase == PomodoroPhase.Work)
        {
            CompletedWork++;
            Phase = CompletedWork % Math.Max(1, CyclesBeforeLongBreak) == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else Phase = PomodoroPhase.Work;
        _endsAt = from + PhaseDuration;
        IsRunning = true;
    }
}

/// <summary>Daily water intake with interval nudges. Resets at midnight.</summary>
public sealed class HydrationTracker
{
    private DateTime _lastActivity;

    public int GoalMl { get; set; } = 2000;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(60);
    public DateOnly Day { get; private set; }
    public int ConsumedMl { get; private set; }
    public bool GoalReached => ConsumedMl >= GoalMl;

    /// <summary>Restores today's total after a restart (ignored if it is from another day).</summary>
    public void Restore(DateOnly day, int consumedMl, DateTime now)
    {
        Day = DateOnly.FromDateTime(now);
        ConsumedMl = day == Day ? consumedMl : 0;
        _lastActivity = now;
    }

    public void Log(int ml, DateTime now)
    {
        RollDay(now);
        ConsumedMl += ml;
        _lastActivity = now;
    }

    /// <summary>Undo a mistaken log; never goes below zero. Doesn't restart the reminder interval.</summary>
    public void Remove(int ml, DateTime now)
    {
        RollDay(now);
        ConsumedMl = Math.Max(0, ConsumedMl - ml);
    }

    /// <summary>Back to zero for today.</summary>
    public void Reset(DateTime now)
    {
        RollDay(now);
        ConsumedMl = 0;
    }

    /// <summary>True when a reminder is due (then the interval starts again).</summary>
    public bool ReminderDue(DateTime now)
    {
        RollDay(now);
        if (GoalReached || now - _lastActivity < Interval) return false;
        _lastActivity = now;
        return true;
    }

    private void RollDay(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (today == Day) return;
        Day = today;
        ConsumedMl = 0;
        _lastActivity = now;
    }
}

/// <summary>
/// Counts continuous time at the computer; being idle at least <see cref="IdleReset"/> counts as a break.
/// Used for the 20-20-20 eye rest and the stand-up reminder.
/// </summary>
public sealed class ActivityTimer(TimeSpan threshold, TimeSpan idleReset)
{
    private TimeSpan _active;
    private DateTime _last;

    public TimeSpan Threshold { get; set; } = threshold;
    public TimeSpan IdleReset { get; set; } = idleReset;
    public TimeSpan Active => _active;

    /// <summary>Call periodically with the time since the last keyboard/mouse input. True = remind now.</summary>
    public bool Tick(DateTime now, TimeSpan idle)
    {
        var elapsed = _last == default ? TimeSpan.Zero : now - _last;
        _last = now;
        if (idle >= IdleReset)
        {
            _active = TimeSpan.Zero;
            return false;
        }
        _active += elapsed;
        if (_active < Threshold) return false;
        _active = TimeSpan.Zero;
        return true;
    }

    /// <summary>Remind again after <paramref name="delay"/> of activity.</summary>
    public void Snooze(TimeSpan delay) => _active = Threshold - delay;
}
