using System.Management;

namespace Xylo;

internal sealed class BrightnessHudService : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<NotificationPreview> _received;
    private ManagementEventWatcher? _watcher;
    private volatile bool _disposed, _initialized;
    public bool Initialized => _initialized;
    public bool Monitoring { get { lock (_gate) return _watcher != null; } }

    public BrightnessHudService(Action<NotificationPreview> received) => _received = received;
    public void Start() => _ = Task.Run(() =>
    {
        ManagementEventWatcher? watcher = null;
        try
        {
            watcher = new ManagementEventWatcher(new ManagementScope(@"\\.\root\WMI"),
                new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += OnBrightnessChanged;
            watcher.Start();
            lock (_gate)
            {
                if (_disposed) return;
                _watcher = watcher;
                watcher = null;
            }
        }
        catch { /* Brightness events may be unavailable on external displays. */ }
        finally
        {
            if (watcher != null) { try { watcher.Stop(); } catch { } watcher.Dispose(); }
            _initialized = true;
        }
    });

    private void OnBrightnessChanged(object sender, EventArrivedEventArgs args)
    {
        try
        {
            int value = Convert.ToInt32(args.NewEvent["Brightness"]);
            if (!_disposed && value is >= 0 and <= 100) _received(Preview(value));
        }
        catch { }
    }

    internal static NotificationPreview Preview(int brightness)
    {
        int value = Math.Clamp(brightness, 0, 100);
        return new NotificationPreview("System", "Brightness", $"{value}%", value / 100f, true, "brightness");
    }

    public void Dispose()
    {
        ManagementEventWatcher? watcher;
        lock (_gate) { if (_disposed) return; _disposed = true; watcher = _watcher; _watcher = null; }
        if (watcher == null) return;
        watcher.EventArrived -= OnBrightnessChanged;
        _ = Task.Run(() => { try { watcher.Stop(); } catch { } watcher.Dispose(); });
    }
}
