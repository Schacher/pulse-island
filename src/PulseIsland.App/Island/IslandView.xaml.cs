using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace PulseIsland.Island;

public sealed partial class IslandView : UserControl
{
    public IslandViewModel Vm { get; }

    internal IslandView(IslandViewModel vm)
    {
        Vm = vm;
        InitializeComponent();
        foreach (var bars in AllBars) bars.SetBrush(vm.AccentBrush);

        // Panels cross-fade on the compositor when their Visibility flips.
        foreach (var panel in new UIElement[] { CompactPanel, PeekPanel, ExpandedPanel, OsdPanel, CardPanel, CleaningPanel }) AddFade(panel);

        UnlockSlider.Unlocked += () => Vm.UnlockRequested?.Invoke();

        // Seeking: stop following the player while the thumb is held, jump on release.
        SeekBar.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => Vm.BeginSeek()), handledEventsToo: true);
        SeekBar.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => Vm.EndSeek(SeekBar.Value)), handledEventsToo: true);
        SeekBar.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => Vm.EndSeek(SeekBar.Value)), handledEventsToo: true);
    }

    /// <summary>
    /// Gives every panel its own fixed pill size, centered at the top of the canvas. They no longer stretch
    /// to the current pill, so an outgoing panel isn't squeezed into the new size while it fades.
    /// </summary>
    internal void SizePanels(Func<IslandMode, (float W, float H, float R)> shape)
    {
        foreach (var (panel, mode) in new (FrameworkElement, IslandMode)[]
                 {
                     (CompactPanel, IslandMode.Compact), (PeekPanel, IslandMode.Peek), (ExpandedPanel, IslandMode.Expanded),
                     (OsdPanel, IslandMode.Osd), (CardPanel, IslandMode.Card), (CleaningPanel, IslandMode.Cleaning),
                 })
        {
            var (w, h, _) = shape(mode);
            panel.Width = w;
            panel.Height = h;
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.VerticalAlignment = VerticalAlignment.Top;
        }
    }

    internal IEnumerable<SpectrumBars> AllBars => [CompactBars, PeekBars, ExpandedBars];

    /// <summary>The bars of the panel on screen, if any.</summary>
    internal SpectrumBars? VisibleBars => Vm.Mode switch
    {
        IslandMode.Compact => CompactBars,
        IslandMode.Peek => PeekBars,
        IslandMode.Expanded => ExpandedBars,
        _ => null,
    };

    private static void AddFade(UIElement panel)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(panel).Compositor;

        var show = compositor.CreateAnimationGroup();
        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.Target = "Opacity";
        fadeIn.InsertKeyFrame(0, 0);
        fadeIn.InsertKeyFrame(1, 1);
        fadeIn.DelayTime = TimeSpan.FromMilliseconds(40); // the pill gets a head start, content follows right away
        fadeIn.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        fadeIn.Duration = TimeSpan.FromMilliseconds(120);
        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.Target = "Scale";
        grow.InsertKeyFrame(0, new Vector3(0.94f, 0.94f, 1));
        grow.InsertKeyFrame(1, Vector3.One);
        grow.DelayTime = fadeIn.DelayTime;
        grow.Duration = TimeSpan.FromMilliseconds(170);
        show.Add(fadeIn);
        show.Add(grow);
        ElementCompositionPreview.SetImplicitShowAnimation(panel, show);

        var fadeOut = compositor.CreateScalarKeyFrameAnimation();
        fadeOut.Target = "Opacity";
        fadeOut.InsertKeyFrame(1, 0);
        fadeOut.Duration = TimeSpan.FromMilliseconds(90); // the pill clip hides the edges, so a softer fade is safe
        ElementCompositionPreview.SetImplicitHideAnimation(panel, fadeOut);

        // Scale from the top-center, where the pill is anchored.
        ((FrameworkElement)panel).SizeChanged += (s, e) =>
            ElementCompositionPreview.GetElementVisual((UIElement)s).CenterPoint = new Vector3((float)e.NewSize.Width / 2, 0, 0);
    }
}
