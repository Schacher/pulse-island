namespace PulseIsland.Core.Media;

/// <summary>Playback position as last reported by the player. Players report rarely, so we extrapolate.</summary>
public readonly record struct MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTimeOffset UpdatedAt, bool IsPlaying, double Rate = 1)
{
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var p = IsPlaying ? Position + (now - UpdatedAt) * Rate : Position;
        if (p < TimeSpan.Zero) return TimeSpan.Zero;
        return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
    }

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

/// <summary>Picks a vivid accent color from album art for the island glow.</summary>
public static class AccentColor
{
    /// <summary>
    /// From BGRA8 pixels: a saturation-weighted average, brightened to read well on black.
    /// Returns null for grey/black art (then the caller keeps its default accent).
    /// </summary>
    public static (byte R, byte G, byte B)? Pick(ReadOnlySpan<byte> bgra)
    {
        double r = 0, g = 0, b = 0, weight = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            double pb = bgra[i] / 255.0, pg = bgra[i + 1] / 255.0, pr = bgra[i + 2] / 255.0;
            double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            var sat = max == 0 ? 0 : (max - min) / max;
            var w = sat * sat * max; // favour colourful, non-dark pixels
            r += pr * w; g += pg * w; b += pb * w; weight += w;
        }
        if (weight < 1e-3 * (bgra.Length / 4.0)) return null;
        r /= weight; g /= weight; b /= weight;

        // Scale up so the brightest channel is near full: dark art still gives a visible glow.
        var peak = Math.Max(r, Math.Max(g, b));
        var k = peak > 0 ? 0.95 / peak : 1;
        return ((byte)Math.Round(Math.Min(1, r * k) * 255), (byte)Math.Round(Math.Min(1, g * k) * 255), (byte)Math.Round(Math.Min(1, b * k) * 255));
    }
}
