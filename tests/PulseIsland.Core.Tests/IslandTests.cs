using PulseIsland.Core.Audio;
using PulseIsland.Core.Island;
using PulseIsland.Core.Media;

namespace PulseIsland.Core.Tests;

public class AlertQueueTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void PlanScenario_AlarmThenOsdThenToast()
    {
        var q = new AlertQueue();
        q.Post(new IslandAlert("pomodoro", AlertPriority.Alarm, TimeSpan.FromSeconds(8)), At(0));
        Assert.Equal("pomodoro", q.Update(At(0))?.Key);

        // Volume OSD 50 ms later preempts the alarm.
        q.Post(new IslandAlert("volume", AlertPriority.Osd, TimeSpan.FromSeconds(1.5)), At(0.05));
        Assert.Equal("volume", q.Update(At(0.05))?.Key);

        // Track change queues behind both.
        q.Post(new IslandAlert("track", AlertPriority.Toast, TimeSpan.FromSeconds(3)), At(0.08));
        Assert.Equal("volume", q.Update(At(0.08))?.Key);

        // OSD expires → alarm resumes with its time paused while it was hidden (8 − 0.05 left).
        var alarm = q.Update(At(1.6));
        Assert.Equal("pomodoro", alarm?.Key);
        Assert.Equal(TimeSpan.FromSeconds(7.95), alarm!.Remaining);

        // User acknowledges the alarm → the still-fresh toast plays.
        q.Dismiss("pomodoro");
        Assert.Equal("track", q.Update(At(2))?.Key);
        Assert.Null(q.Update(At(5.1)));
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void SameKeyCoalescesAndExtends()
    {
        var q = new AlertQueue();
        q.Post(new IslandAlert("volume", AlertPriority.Osd, TimeSpan.FromSeconds(1.5), 40), At(0));
        q.Update(At(0));
        q.Post(new IslandAlert("volume", AlertPriority.Osd, TimeSpan.FromSeconds(1.5), 45), At(1));
        var shown = q.Update(At(1));
        Assert.Equal(45, shown?.Payload);
        Assert.NotNull(q.Update(At(2.4))); // would have expired at 1.5 without the repeat
        Assert.Null(q.Update(At(2.6)));
    }

    [Fact]
    public void StaleToastIsDropped()
    {
        var q = new AlertQueue();
        q.Post(new IslandAlert("alarm", AlertPriority.Alarm, TimeSpan.FromSeconds(30)), At(0));
        q.Post(new IslandAlert("track", AlertPriority.Toast, TimeSpan.FromSeconds(3)), At(0));
        q.Update(At(0));
        q.Dismiss("alarm");
        Assert.Null(q.Update(At(11))); // toast waited >10 s
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void FullscreenOnlyShowsOsdAndModal()
    {
        var q = new AlertQueue { Fullscreen = true };
        q.Post(new IslandAlert("track", AlertPriority.Toast, TimeSpan.FromSeconds(3)), At(0));
        q.Post(new IslandAlert("pomodoro", AlertPriority.Alarm, TimeSpan.FromSeconds(8)), At(0));
        Assert.Null(q.Update(At(0)));
        q.Post(new IslandAlert("volume", AlertPriority.Osd, TimeSpan.FromSeconds(1)), At(0.5));
        Assert.Equal("volume", q.Update(At(0.5))?.Key);

        q.Fullscreen = false; // game closed: the waiting alarm appears, with its full time
        var alarm = q.Update(At(2));
        Assert.Equal("pomodoro", alarm?.Key);
        Assert.Equal(TimeSpan.FromSeconds(8), alarm!.Remaining);
    }

    [Fact]
    public void InteractionHoldsToastsButNotOsd()
    {
        var q = new AlertQueue { UserInteracting = true };
        q.Post(new IslandAlert("track", AlertPriority.Toast, TimeSpan.FromSeconds(3)), At(0));
        Assert.Null(q.Update(At(0)));
        q.UserInteracting = false;
        Assert.Equal("track", q.Update(At(1))?.Key);
    }
}

public class SpectrumTests
{
    [Fact]
    public void SineLightsUpItsBandOnly()
    {
        const int rate = 48000;
        var s = new SpectrumAnalyzer(rate, bands: 24);
        var samples = new float[SpectrumAnalyzer.FftSize * 2]; // stereo
        for (var round = 0; round < 6; round++) // a few frames so smoothing settles
        {
            for (var i = 0; i < SpectrumAnalyzer.FftSize; i++)
                samples[2 * i] = samples[2 * i + 1] = 0.8f * MathF.Sin(2 * MathF.PI * 1000 * (round * SpectrumAnalyzer.FftSize + i) / rate);
            s.AddInterleaved(samples, 2);
            s.Compute();
        }
        var levels = new float[24];
        s.CopyLevels(levels);

        var loudest = Array.IndexOf(levels, levels.Max());
        // 1 kHz on a 40 Hz–16 kHz log scale with 24 bands → band 12 or 13.
        Assert.InRange(loudest, 11, 14);
        Assert.True(levels[loudest] > 0.8f, $"peak {levels[loudest]}");
        Assert.True(levels[0] < 0.4f && levels[23] < 0.4f, "far bands should stay low");
    }

    [Fact]
    public void SilenceStaysAtZero()
    {
        var s = new SpectrumAnalyzer(48000);
        s.AddSilence(4096);
        s.Compute();
        var levels = new float[s.Bands];
        s.CopyLevels(levels);
        Assert.All(levels, l => Assert.Equal(0f, l, 3));
    }
}

public class MediaMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExtrapolatesWhilePlayingAndClamps()
    {
        var t = new MediaTimeline(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(200), T0, IsPlaying: true);
        Assert.Equal(TimeSpan.FromSeconds(15), t.PositionAt(T0.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(200), t.PositionAt(T0.AddSeconds(500)));
        Assert.Equal(TimeSpan.FromSeconds(10), (t with { IsPlaying = false }).PositionAt(T0.AddSeconds(5)));
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    [InlineData(0, "0:00")]
    public void FormatsTime(int seconds, string expected) =>
        Assert.Equal(expected, MediaTimeline.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void AccentFromRedArtIsRed()
    {
        var px = Enumerable.Repeat(new byte[] { 20, 20, 180, 255 }, 64).SelectMany(x => x).ToArray(); // BGRA
        var c = AccentColor.Pick(px)!.Value;
        Assert.True(c.R > 200 && c.G < 60 && c.B < 60, $"{c}");
    }

    [Fact]
    public void GreyArtGivesNoAccent()
    {
        var px = Enumerable.Repeat(new byte[] { 128, 128, 128, 255 }, 64).SelectMany(x => x).ToArray();
        Assert.Null(AccentColor.Pick(px));
    }
}
