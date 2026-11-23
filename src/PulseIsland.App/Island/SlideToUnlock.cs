using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace PulseIsland.Island;

/// <summary>
/// iPhone-style "slide to unlock": a white knob dragged along a track. Past 85 % it unlocks; letting go
/// earlier springs back. The label fades as the knob covers it.
/// </summary>
public sealed partial class SlideToUnlock : Grid
{
    private const double Knob = 40, Inset = 3, UnlockAt = 0.85;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SlideToUnlock),
        new PropertyMetadata("", (d, e) => ((SlideToUnlock)d)._label.Text = (string)e.NewValue));

    private readonly TextBlock _label;
    private readonly TranslateTransform _offset = new();

    public event Action? Unlocked;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public SlideToUnlock()
    {
        Height = Knob + 2 * Inset;
        CornerRadius = new CornerRadius((Knob + 2 * Inset) / 2);
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(38, 255, 255, 255));

        _label = new TextBlock
        {
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Knob, 0, 0, 0),
        };
        var knob = new Border
        {
            Width = Knob,
            Height = Knob,
            Margin = new Thickness(Inset),
            CornerRadius = new CornerRadius(Knob / 2),
            Background = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _offset,
            ManipulationMode = ManipulationModes.TranslateX,
            Child = new FontIcon { Glyph = "", FontSize = 16, Foreground = new SolidColorBrush(Colors.Black) },
        };
        AutomationProperties.SetName(knob, "Unlock");
        knob.ManipulationDelta += OnDrag;
        knob.ManipulationCompleted += OnRelease;
        Children.Add(_label);
        Children.Add(knob);
    }

    private double Travel => Math.Max(1, ActualWidth - Knob - 2 * Inset);

    private void OnDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        _offset.X = Math.Clamp(_offset.X + e.Delta.Translation.X, 0, Travel);
        _label.Opacity = 1 - _offset.X / Travel;
    }

    private void OnRelease(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (_offset.X >= Travel * UnlockAt)
        {
            _offset.X = Travel;
            Unlocked?.Invoke();
        }
        SpringBack();
    }

    /// <summary>Knob back to the start (also used each time the lock screen appears).</summary>
    public void SpringBack()
    {
        var back = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(back, _offset);
        Storyboard.SetTargetProperty(back, "X");
        var board = new Storyboard { Children = { back } };
        board.Completed += (_, _) => _label.Opacity = 1;
        board.Begin();
    }
}
