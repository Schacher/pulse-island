using Microsoft.UI.Dispatching;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace PulseIsland.Platform.Devices;

/// <summary>
/// Raises <see cref="Connected"/> when a Bluetooth device (classic or LE) connects, with its battery level
/// when Windows knows it. Event-driven (DeviceWatcher), no polling. Events on the UI thread.
/// </summary>
internal sealed class BluetoothWatcher : IDisposable
{
    // DEVPKEY_Bluetooth_Battery: what Settings → Bluetooth shows for headsets that report battery.
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    // Plain arrays below: a C# collection expression ([x]) is a compiler type WinRT cannot marshal.

    private readonly DispatcherQueue _ui;
    private readonly List<DeviceWatcher> _watchers = [];

    /// <summary>Device name and battery percentage (null if unknown).</summary>
    public event Action<string, int?>? Connected;

    public BluetoothWatcher(DispatcherQueue ui)
    {
        _ui = ui;
        Watch(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
        Watch(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
    }

    private void Watch(string selector)
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(selector, new[] { ContainerKey }, DeviceInformationKind.AssociationEndpoint);
            var ready = false; // the initial enumeration lists devices that were already connected: not news
            watcher.EnumerationCompleted += (_, _) => ready = true;
            watcher.Added += (sender, info) => { if (ready) _ = OnConnected(info); };
            watcher.Removed += (_, _) => { };
            watcher.Updated += (_, _) => { };
            watcher.Start();
            _watchers.Add(watcher);
        }
        catch (Exception e)
        {
            Log.Info($"bluetooth: watcher unavailable: {e.Message}");
        }
    }

    private async Task OnConnected(DeviceInformation info)
    {
        int? battery = null;
        try
        {
            if (info.Properties.TryGetValue(ContainerKey, out var c) && c is Guid container)
            {
                var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"", new[] { BatteryKey }, DeviceInformationKind.Device);
                foreach (var node in nodes)
                    if (node.Properties.TryGetValue(BatteryKey, out var b) && b is byte level) { battery = level; break; }
            }
        }
        catch (Exception e)
        {
            Log.Info($"bluetooth: battery read failed: {e.Message}");
        }
        var name = string.IsNullOrWhiteSpace(info.Name) ? "Bluetooth" : info.Name;
        _ui.TryEnqueue(() => Connected?.Invoke(name, battery));
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
            try { w.Stop(); } catch (InvalidOperationException) { } // already stopped
    }
}
