using System.Windows.Threading;
using DeskGrid.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace DeskGrid.Island;

/// <summary>
/// Tells when a Bluetooth device (headphones, a mouse) connects, with its battery if Windows knows it. Whatever was
/// connected when it started is not news. Everything stays on this computer.
/// </summary>
internal sealed class BluetoothWatcher : IDisposable
{
    // The property Windows keeps for Bluetooth devices that report a charge (absent for the others).
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private DeviceWatcher? _watcher;
    private bool _ready; // The first listing (what is connected already) is over.

    public BluetoothWatcher()
    {
        try
        {
            var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _watcher = DeviceInformation.CreateWatcher(selector, [BatteryProperty]);
            _watcher.Added += (_, device) => _dispatcher.BeginInvoke(() => OnAdded(device));
            _watcher.EnumerationCompleted += (_, _) => _dispatcher.BeginInvoke(() => _ready = true);
            _watcher.Start();
        }
        catch (Exception exception)
        {
            Log.Error("Bluetooth devices cannot be watched; the island will not tell of them", exception);
            _watcher = null;
        }
    }

    /// <summary>Raised on the dispatcher thread with the device's name and its charge in percent, if known.</summary>
    public event Action<string, int?>? Connected;

    public void Dispose()
    {
        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
        {
            _watcher.Stop();
        }

        _watcher = null;
    }

    private void OnAdded(DeviceInformation device)
    {
        // _watcher is null once disposed: a device that was on its way when the option was turned off is not told of.
        if (!_ready || _watcher is null || string.IsNullOrWhiteSpace(device.Name))
        {
            return;
        }

        int? battery = device.Properties.TryGetValue(BatteryProperty, out var value) && value is byte percent ? percent : null;
        Connected?.Invoke(device.Name, battery);
    }
}
