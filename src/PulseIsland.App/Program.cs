using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PulseIsland.Platform;

namespace PulseIsland;

public static class Program
{
    private const string MutexName = @"Local\PulseIsland.Instance";
    private const string ShowEventName = @"Local\PulseIsland.Show";

    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirst)
        {
            // Already running: let it take the foreground and open its mixer, then quit.
            Native.AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            showSignal.Set();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        App.ShowSignal = showSignal;
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
