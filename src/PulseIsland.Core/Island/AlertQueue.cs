namespace PulseIsland.Core.Island;

/// <summary>Lower value = shown first.</summary>
public enum AlertPriority
{
    /// <summary>Takes over the island (cleaning mode).</summary>
    Modal = 0,
    /// <summary>Instant feedback (volume, brightness, lock keys): briefly shows over alarms, even in fullscreen.</summary>
    Osd = 1,
    /// <summary>Needs attention (Pomodoro done, stand-up nudge). Paused, not lost, while an OSD is up.</summary>
    Alarm = 2,
    /// <summary>Nice to know (track changed, device connected). Waits while the user interacts.</summary>
    Toast = 3,
}

public sealed class IslandAlert(string key, AlertPriority priority, TimeSpan duration, object? payload = null)
{
    /// <summary>Alerts with the same key coalesce: a repeat updates the payload and restarts the timer.</summary>
    public string Key { get; } = key;
    public AlertPriority Priority { get; } = priority;
    public TimeSpan Duration { get; } = duration;
    public object? Payload { get; internal set; } = payload;

    /// <summary>Display time left. Only runs down while the alert is on screen.</summary>
    public TimeSpan Remaining { get; internal set; } = duration;
    internal DateTime PostedAt { get; set; }
}

/// <summary>
/// Decides which alert the island shows. The highest priority wins (ties: oldest first). A preempted
/// alert is paused, not lost, and resumes with its remaining time. Not thread-safe: UI thread only.
/// </summary>
public sealed class AlertQueue
{
    /// <summary>Toasts that could not be shown within this time are dropped as stale.</summary>
    public static readonly TimeSpan StaleToast = TimeSpan.FromSeconds(10);

    private readonly List<IslandAlert> _alerts = [];
    private DateTime _lastUpdate;

    public IslandAlert? Shown { get; private set; }
    public bool IsEmpty => _alerts.Count == 0;

    /// <summary>While the user hovers/expands the island, toasts wait.</summary>
    public bool UserInteracting { get; set; }

    /// <summary>In fullscreen only Modal and OSD alerts are shown; the rest wait.</summary>
    public bool Fullscreen { get; set; }

    public void Post(IslandAlert alert, DateTime now)
    {
        Advance(now); // charge the time so far to what was on screen before changing anything
        var existing = _alerts.Find(a => a.Key == alert.Key);
        if (existing != null)
        {
            existing.Payload = alert.Payload;
            existing.Remaining = alert.Duration;
            return;
        }
        alert.Remaining = alert.Duration;
        alert.PostedAt = now;
        _alerts.Add(alert);
    }

    public void Dismiss(string key) => _alerts.RemoveAll(a => a.Key == key);

    /// <summary>Advances the shown alert's timer and returns what to show now (null = ambient content).</summary>
    public IslandAlert? Update(DateTime now)
    {
        Advance(now);
        _alerts.RemoveAll(a => a.Priority == AlertPriority.Toast && a != Shown && now - a.PostedAt > StaleToast);

        Shown = _alerts.Where(Eligible).OrderBy(a => a.Priority).ThenBy(a => a.PostedAt).FirstOrDefault();
        return Shown;
    }

    private void Advance(DateTime now)
    {
        var elapsed = _lastUpdate == default ? TimeSpan.Zero : now - _lastUpdate;
        _lastUpdate = now;
        if (Shown == null || !_alerts.Contains(Shown)) return;
        Shown.Remaining -= elapsed;
        if (Shown.Remaining <= TimeSpan.Zero) _alerts.Remove(Shown);
    }

    private bool Eligible(IslandAlert a) =>
        !(Fullscreen && a.Priority > AlertPriority.Osd) &&
        !(UserInteracting && a.Priority == AlertPriority.Toast);
}
