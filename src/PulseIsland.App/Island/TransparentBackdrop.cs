using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PulseIsland.Platform;

namespace PulseIsland.Island;

/// <summary>
/// Fully transparent window background, so only the pill we draw is visible (per-pixel alpha).
/// WinUI has no built-in transparent backdrop: we give the system backdrop a transparent brush and
/// switch on DWM blur-behind with an empty, off-screen region, which makes DWM honour alpha.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private static nint s_systemDispatcherQueue;
    private static Windows.UI.Composition.Compositor? s_compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        target.SystemBackdrop = SystemCompositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        var hwnd = Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        var empty = Native.CreateRectRgn(-2, -2, -1, -1);
        Native.DwmEnableBlurBehindWindow(hwnd, new Native.DWM_BLURBEHIND
        {
            dwFlags = 0x1 | 0x2, // DWM_BB_ENABLE | DWM_BB_BLURREGION
            fEnable = 1,
            hRgnBlur = empty,
        });
        Native.DeleteObject(empty);

        // No Windows 11 frame: its 1px border and rounded corners would outline the whole transparent canvas.
        int none = unchecked((int)0xFFFFFFFE), square = 1;
        Native.DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref none, sizeof(int));          // DWMWA_COLOR_NONE
        Native.DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref square, sizeof(int)); // DWMWCP_DONOTROUND
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        target.SystemBackdrop = null;
    }

    /// <summary>The backdrop brush must come from the system (Windows.UI) compositor, which needs a system dispatcher queue.</summary>
    private static Windows.UI.Composition.Compositor SystemCompositor()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() == null && s_systemDispatcherQueue == 0)
        {
            Native.CreateDispatcherQueueController(new Native.DispatcherQueueOptions
            {
                dwSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.DispatcherQueueOptions>(),
                threadType = 2,    // DQTYPE_THREAD_CURRENT
                apartmentType = 2, // DQTAT_COM_STA
            }, out s_systemDispatcherQueue); // kept for the app's lifetime
        }
        return s_compositor ??= new Windows.UI.Composition.Compositor();
    }
}
