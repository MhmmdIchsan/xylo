using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Networking.Connectivity;

namespace Xylo;

internal sealed record WifiState(bool Connected, string Ssid);

internal sealed class ConnectivityService : IDisposable
{
    private readonly Action<NotificationPreview> _received;
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _devices = new();
    private DeviceWatcher? _watcher;
    private WifiState? _wifi;
    private bool _networkSubscribed, _enumerated, _disposed;
    public bool NetworkMonitoring => _networkSubscribed;
    public bool BluetoothMonitoring { get; private set; }

    public ConnectivityService(Action<NotificationPreview> received) => _received = received;

    public void Start()
    {
        try
        {
            _wifi = ReadWifi();
            NetworkInformation.NetworkStatusChanged += OnNetworkChanged;
            _networkSubscribed = true;
        }
        catch { /* Network status is not available on every installation. */ }
        try
        {
            _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
            _watcher.Added += OnAdded;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += OnEnumerated;
            _watcher.Start();
            BluetoothMonitoring = true;
        }
        catch { /* Bluetooth may be unavailable or blocked by Windows permissions. */ }
    }

    private static WifiState ReadWifi()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null || !profile.IsWlanConnectionProfile || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
            return new(false, "");
        string name = "";
        try { name = profile.WlanConnectionProfileDetails?.GetConnectedSsid() ?? ""; }
        catch { /* SSID may require Windows location permission. */ }
        return new(true, name);
    }

    internal static NotificationPreview? WifiAlert(WifiState previous, WifiState current)
    {
        if (previous == current) return null;
        if (current.Connected && (!previous.Connected || current.Ssid != previous.Ssid))
            return new("Connectivity", "Wi-Fi connected", current.Ssid);
        if (previous.Connected && !current.Connected)
            return new("Connectivity", "Wi-Fi disconnected", previous.Ssid);
        return null;
    }

    private void OnNetworkChanged(object sender)
    {
        try
        {
            var current = ReadWifi();
            NotificationPreview? alert;
            lock (_sync)
            {
                if (_disposed) return;
                alert = _wifi == null ? null : WifiAlert(_wifi, current);
                _wifi = current;
            }
            if (alert != null) _received(alert);
        }
        catch { }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        NotificationPreview? alert = null;
        lock (_sync)
        {
            if (_disposed || _devices.ContainsKey(device.Id)) return;
            string name = string.IsNullOrWhiteSpace(device.Name) ? "Bluetooth device" : device.Name;
            _devices[device.Id] = name;
            if (_enumerated) alert = new("Connectivity", "Bluetooth connected", name);
        }
        if (alert != null) _received(alert);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        NotificationPreview? alert = null;
        lock (_sync)
        {
            if (_disposed || !_devices.Remove(device.Id, out var name)) return;
            if (_enumerated) alert = new("Connectivity", "Bluetooth disconnected", name);
        }
        if (alert != null) _received(alert);
    }

    private void OnEnumerated(DeviceWatcher sender, object args) { lock (_sync) if (!_disposed) _enumerated = true; }

    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        if (_networkSubscribed) NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        if (_watcher != null)
        {
            _watcher.Added -= OnAdded;
            _watcher.Removed -= OnRemoved;
            _watcher.EnumerationCompleted -= OnEnumerated;
            try { _watcher.Stop(); } catch { }
            BluetoothMonitoring = false;
        }
    }
}
