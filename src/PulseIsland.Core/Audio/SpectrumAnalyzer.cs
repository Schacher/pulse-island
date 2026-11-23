namespace PulseIsland.Core.Audio;

/// <summary>
/// Turns audio samples into smoothed, log-spaced band levels (0..1) for the visualizer.
/// Feed and compute from one thread; read <see cref="CopyLevels"/> from any thread.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int FftSize = 1024;
    private const float FloorDb = -70f;

    private readonly float[] _ring = new float[FftSize];
    private int _ringPos;
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize], _im = new float[FftSize];
    private readonly (int Lo, int Hi)[] _bandBins;
    private readonly float[] _levels;
    private readonly Lock _lock = new();

    public int Bands => _levels.Length;

    public SpectrumAnalyzer(int sampleRate, int bands = 24, float minHz = 40, float maxHz = 16000)
    {
        _levels = new float[bands];
        for (var i = 0; i < FftSize; i++) _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (FftSize - 1))); // Hann
        maxHz = Math.Min(maxHz, sampleRate / 2f);
        var binHz = (float)sampleRate / FftSize;
        _bandBins = new (int, int)[bands];
        for (var b = 0; b < bands; b++)
        {
            // Log spacing: bass gets few bins per band, treble many — like the ear.
            var lo = minHz * MathF.Pow(maxHz / minHz, (float)b / bands);
            var hi = minHz * MathF.Pow(maxHz / minHz, (float)(b + 1) / bands);
            var loBin = Math.Clamp((int)(lo / binHz), 1, FftSize / 2 - 1);
            var hiBin = Math.Clamp((int)MathF.Ceiling(hi / binHz), loBin + 1, FftSize / 2);
            _bandBins[b] = (loBin, hiBin);
        }
    }

    /// <summary>Adds interleaved float samples (any channel count), mixed down to mono.</summary>
    public void AddInterleaved(ReadOnlySpan<float> samples, int channels)
    {
        for (var i = 0; i + channels <= samples.Length; i += channels)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++) sum += samples[i + c];
            _ring[_ringPos] = sum / channels;
            _ringPos = (_ringPos + 1) % FftSize;
        }
    }

    public void AddSilence(int frames)
    {
        for (var i = 0; i < Math.Min(frames, FftSize); i++)
        {
            _ring[_ringPos] = 0;
            _ringPos = (_ringPos + 1) % FftSize;
        }
    }

    /// <summary>Runs the FFT over the latest window and updates the smoothed levels.</summary>
    public void Compute()
    {
        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _ring[(_ringPos + i) % FftSize] * _window[i];
            _im[i] = 0;
        }
        Fft(_re, _im);

        lock (_lock)
        {
            for (var b = 0; b < _levels.Length; b++)
            {
                var (lo, hi) = _bandBins[b];
                float peak = 0;
                for (var k = lo; k < hi; k++) peak = MathF.Max(peak, _re[k] * _re[k] + _im[k] * _im[k]);
                // Full-scale sine ≈ FftSize/4 magnitude with a Hann window.
                var db = 10 * MathF.Log10(peak / (FftSize / 4f * (FftSize / 4f)) + 1e-12f);
                var target = Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
                // Fast attack, slow release: bars jump up and fall gently.
                _levels[b] = target > _levels[b] ? target * 0.7f + _levels[b] * 0.3f : _levels[b] * 0.82f + target * 0.18f;
            }
        }
    }

    public void CopyLevels(Span<float> target)
    {
        lock (_lock) _levels.AsSpan(0, Math.Min(target.Length, _levels.Length)).CopyTo(target);
    }

    public void Reset()
    {
        Array.Clear(_ring);
        lock (_lock) Array.Clear(_levels);
    }

    /// <summary>In-place iterative radix-2 FFT.</summary>
    internal static void Fft(float[] re, float[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}
