using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace PulseIsland.Island;

/// <summary>A row of level bars. Driven from code (no bindings) so a 30 fps update stays cheap.</summary>
public sealed partial class SpectrumBars : Grid
{
    private const double Rest = 0.12;
    private readonly List<ScaleTransform> _scales = [];
    private int _count;

    public SpectrumBars() => ColumnSpacing = 2;

    public Brush? BarBrush { get; set; }

    public int BarCount
    {
        get => _count;
        set
        {
            _count = value;
            Children.Clear();
            ColumnDefinitions.Clear();
            _scales.Clear();
            for (var i = 0; i < value; i++)
            {
                ColumnDefinitions.Add(new ColumnDefinition());
                var scale = new ScaleTransform { ScaleY = Rest };
                var bar = new Rectangle
                {
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = BarBrush,
                    RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                    RenderTransform = scale,
                };
                SetColumn(bar, i);
                Children.Add(bar);
                _scales.Add(scale);
            }
        }
    }

    public void SetBrush(Brush brush)
    {
        BarBrush = brush;
        foreach (var child in Children) ((Rectangle)child).Fill = brush;
    }

    /// <summary>Maps the analyzer bands onto the bars (each bar = the loudest band in its slice).</summary>
    public void Update(ReadOnlySpan<float> bands)
    {
        if (_count == 0 || bands.Length == 0) return;
        for (var i = 0; i < _count; i++)
        {
            int lo = i * bands.Length / _count, hi = Math.Max(lo + 1, (i + 1) * bands.Length / _count);
            float v = 0;
            for (var b = lo; b < hi; b++) v = Math.Max(v, bands[b]);
            _scales[i].ScaleY = Rest + (1 - Rest) * v;
        }
    }

    public void Settle()
    {
        foreach (var s in _scales) s.ScaleY = Rest;
    }
}
