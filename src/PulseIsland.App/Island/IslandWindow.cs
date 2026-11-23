using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using PulseIsland.Platform;
using Windows.Graphics;

namespace PulseIsland.Island;

/// <summary>
/// The island: a transparent, topmost, never-activated window at the top of a monitor. The pill is a
/// composition shape whose size/corners are animated by springs on the compositor thread, so morphs
/// run at the display refresh rate without any work on our UI thread. Outside the pill, clicks fall
/// through to the windows below (the window region is kept to the pill's bounds).
/// </summary>
internal sealed class IslandWindow : Window
{
    /// <summary>Largest pill plus room for the glow. All island sizes are in DIPs at Scale = 1.</summary>
    public const float CanvasWidth = 460, CanvasHeight = 230, Glow = 26;

    private readonly Grid _scaleRoot;
    private readonly Border _hit;
    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionColorBrush _fill;
    private readonly DropShadow _shadow;
    private readonly DispatcherQueueTimer _regionTimer;
    private Vector2 _size;
    private float _userScale = 1;
    private double _dpiScale = 1;
    private bool _animate = true;

    public nint Hwnd { get; }

    public event Action? PillEntered;
    public event Action? PillExited;
    public event Action? PillTapped;

    public IslandWindow(UIElement content)
    {
        Title = "Pulse Island — island";
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SystemBackdrop = new TransparentBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, e) => e.Cancel = true;
        // Never take focus from the app the user is in; clicks still reach our buttons.
        const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        const nint WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        Native.SetWindowLongPtr(Hwnd, GWL_EXSTYLE, Native.GetWindowLongPtr(Hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        // Plain popup: no caption/frame bits at all (a borderless overlapped window still shows a 1px top line).
        Native.SetWindowLongPtr(Hwnd, GWL_STYLE, unchecked((nint)0x80000000)); // WS_POPUP

        // Visual tree: [scaleRoot (user scale)] → [shape host (composition pill)] + [content host]
        // The content host spans the whole canvas and is clipped to the animated pill shape, so panels keep
        // their own size while they fade and never spill outside the pill during a morph.
        var shapeHost = new Grid();
        _hit = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent), // hover/tap area = the target pill
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var contentHost = new Grid { Children = { _hit, content } };
        // Events on the host, not the hit area: moving onto a button (a sibling) must not count as leaving.
        contentHost.PointerEntered += (_, _) => PillEntered?.Invoke();
        contentHost.PointerExited += (_, _) => PillExited?.Invoke();
        contentHost.Tapped += (_, _) => PillTapped?.Invoke();
        _scaleRoot = new Grid
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { shapeHost, contentHost },
        };
        Content = new Grid { Children = { _scaleRoot } };

        _compositor = ElementCompositionPreview.GetElementVisual(shapeHost).Compositor;
        _geometry = _compositor.CreateRoundedRectangleGeometry();
        // Not pure black: with the blur-behind transparency trick DWM treats #000000 as see-through.
        _fill = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 13, 13, 15));
        var shape = _compositor.CreateSpriteShape(_geometry);
        shape.FillBrush = _fill;
        _shape = shape;
        var shapes = _compositor.CreateShapeVisual();
        shapes.Size = new Vector2(CanvasWidth, CanvasHeight);
        shapes.Shapes.Add(shape);
        // The glow is a coloured drop shadow masked by a live copy of the pill (a visual surface),
        // so it follows every morph. (A LayerVisual shadow doesn't render in this transparent window.)
        var mirror = _compositor.CreateVisualSurface();
        mirror.SourceVisual = shapes;
        mirror.SourceSize = shapes.Size;
        _shadow = _compositor.CreateDropShadow();
        _shadow.Mask = _compositor.CreateSurfaceBrush(mirror);
        _shadow.BlurRadius = 22;
        _shadow.Offset = new Vector3(0, 3, 0);
        _shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        _shadow.Opacity = 0.45f;
        var glow = _compositor.CreateSpriteVisual();
        glow.Size = shapes.Size;
        glow.Shadow = _shadow;
        var container = _compositor.CreateContainerVisual();
        container.Size = shapes.Size;
        container.Children.InsertAtBottom(glow);
        container.Children.InsertAtTop(shapes);
        ElementCompositionPreview.SetElementChildVisual(shapeHost, container);
        // Same geometry as the fill: the clip follows every spring frame on the compositor.
        ElementCompositionPreview.GetElementVisual(contentHost).Clip = _compositor.CreateGeometricClip(_geometry);

        _regionTimer = DispatcherQueue.CreateTimer();
        _regionTimer.Interval = TimeSpan.FromMilliseconds(400); // springs have settled by then
        _regionTimer.IsRepeating = false;
        _regionTimer.Tick += (_, _) => SetRegion(_size, _size);
    }

    /// <summary>Off in Lite Mode: no springs or glow, shapes just snap.</summary>
    public bool Animate
    {
        get => _animate;
        set
        {
            if (_animate == value) return;
            _animate = value;
            SetGlow(_accent); // re-applies (or drops) the glow for the new mode
        }
    }

    private Windows.UI.Color? _accent;
    private CompositionSpriteShape? _shape;

    /// <summary>Glass: translucent dark fill (what's behind shows through, without blur) and a faint light edge.</summary>
    public void SetGlass(bool glass)
    {
        _fill.Color = glass ? Windows.UI.Color.FromArgb(200, 30, 30, 36) : Windows.UI.Color.FromArgb(255, 13, 13, 15);
        _shape!.StrokeBrush = glass ? _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255)) : null;
        _shape.StrokeThickness = glass ? 1 : 0;
    }

    public bool IsShown => AppWindow.IsVisible;

    /// <summary>Morphs the pill to a new size. Interrupting a running morph re-targets it smoothly.</summary>
    public void Morph(float width, float height, float radius)
    {
        var previous = _size;
        _size = new Vector2(width, height);
        _hit.Width = width;
        _hit.Height = height;
        var offset = new Vector2((CanvasWidth - width) / 2, 0);
        var corner = new Vector2(radius, radius);

        if (_animate && previous != Vector2.Zero && IsShown)
        {
            Spring(_geometry, "Size", _size);
            Spring(_geometry, "Offset", offset);
            Spring(_geometry, "CornerRadius", corner);
            SetRegion(previous, _size); // cover both shapes while it moves
            _regionTimer.Stop();
            _regionTimer.Start();
        }
        else
        {
            _geometry.Size = _size;
            _geometry.Offset = offset;
            _geometry.CornerRadius = corner;
            SetRegion(_size, _size);
        }
    }

    public void SetGlow(Windows.UI.Color? accent)
    {
        _accent = accent;
        var color = accent ?? Windows.UI.Color.FromArgb(255, 0, 0, 0);
        var opacity = !_animate ? 0f : accent == null ? 0.45f : 0.75f;
        if (_animate && IsShown)
        {
            var anim = _compositor.CreateColorKeyFrameAnimation();
            anim.InsertKeyFrame(1, color);
            anim.Duration = TimeSpan.FromMilliseconds(300);
            _shadow.StartAnimation("Color", anim);
        }
        else _shadow.Color = color;
        _shadow.Opacity = opacity;
    }

    /// <summary>Positions the island at the top-center of a monitor.</summary>
    public void Place(nint monitor, int offsetXDip, int offsetYDip, double userScale)
    {
        _userScale = (float)userScale;
        _scaleRoot.RenderTransform = new ScaleTransform { ScaleX = userScale, ScaleY = userScale };
        _dpiScale = Native.MonitorDpi(monitor) / 96.0;
        var rc = Native.MonitorInfo(monitor).rcMonitor;
        var k = _dpiScale * userScale;
        int w = (int)Math.Ceiling(CanvasWidth * k), h = (int)Math.Ceiling(CanvasHeight * k);
        int x = (rc.Left + rc.Right) / 2 - w / 2 + (int)(offsetXDip * _dpiScale);
        int y = rc.Top + (int)(offsetYDip * _dpiScale);
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        SetRegion(_size, _size);
    }

    public void ShowIsland()
    {
        if (!IsShown) AppWindow.Show(activateWindow: false);
    }

    public void HideIsland()
    {
        if (IsShown) AppWindow.Hide();
    }

    private void Spring(CompositionObject target, string property, Vector2 to)
    {
        var spring = _compositor.CreateSpringVector2Animation();
        spring.FinalValue = to;
        spring.DampingRatio = 0.78f;           // a touch of overshoot, settles fast
        spring.Period = TimeSpan.FromMilliseconds(30);
        target.StartAnimation(property, spring);
    }

    /// <summary>Clicks only hit the window inside this rectangle (pill + glow); elsewhere they pass through.</summary>
    private void SetRegion(Vector2 a, Vector2 b)
    {
        var w = Math.Max(a.X, b.X) + 2 * Glow;
        var h = Math.Max(a.Y, b.Y) + Glow;
        var k = _dpiScale * _userScale;
        int left = (int)((CanvasWidth - w) / 2 * k), right = (int)Math.Ceiling((CanvasWidth + w) / 2 * k);
        var region = Native.CreateRectRgn(left, 0, right, (int)Math.Ceiling(h * k));
        Native.SetWindowRgn(Hwnd, region, true); // the window owns the region afterwards
    }
}
