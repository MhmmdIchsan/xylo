// Core Audio interop adapted from WinIsland. See third-party/WinIsland.LICENSE.
using System.Runtime.InteropServices;

namespace Xylo;

internal sealed class VolumeHudService : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<NotificationPreview> _received;
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceCallback _deviceCallback;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private bool _registered;
    private volatile bool _disposed;
    private volatile bool _initialized;
    public bool Initialized => _initialized;
    public bool Monitoring { get { lock (_gate) return _endpoint != null; } }

    public VolumeHudService(Action<NotificationPreview> received)
    {
        _received = received;
        _volumeCallback = new VolumeCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    public void Start() => _ = Task.Run(() =>
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                _registered = _enumerator.RegisterEndpointNotificationCallback(_deviceCallback) >= 0;
                RebindLocked();
            }
            catch { /* No audio endpoint or Core Audio access. */ }
            finally { _initialized = true; }
        }
    });

    private void Rebind()
    {
        lock (_gate) { if (!_disposed) RebindLocked(); }
    }

    private void RebindLocked()
    {
        if (_endpoint != null)
        {
            try { _endpoint.UnregisterControlChangeNotify(_volumeCallback); } catch { }
            Marshal.ReleaseComObject(_endpoint);
            _endpoint = null;
        }
        if (_enumerator == null || _enumerator.GetDefaultAudioEndpoint(0, 1, out var device) < 0 || device == null) return;
        try
        {
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 0x17, IntPtr.Zero, out var obj) < 0) return;
            _endpoint = (IAudioEndpointVolume)obj;
            if (_endpoint.RegisterControlChangeNotify(_volumeCallback) < 0)
            { Marshal.ReleaseComObject(_endpoint); _endpoint = null; }
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    internal static NotificationPreview Preview(float volume, bool muted)
    {
        float value = muted ? 0 : Math.Clamp(volume, 0, 1);
        return new NotificationPreview("System", muted ? "Muted" : "Volume", $"{Math.Round(value * 100):0}%", value, true);
    }

    private void Publish(float volume, bool muted)
    {
        if (!_disposed) _received(Preview(volume, muted));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_enumerator != null && _registered)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback); } catch { }
                _registered = false;
            }
            if (_endpoint != null)
            {
                try { _endpoint.UnregisterControlChangeNotify(_volumeCallback); } catch { }
                Marshal.ReleaseComObject(_endpoint);
                _endpoint = null;
            }
            if (_enumerator != null) { Marshal.ReleaseComObject(_enumerator); _enumerator = null; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeNotification { public Guid Context; public int Muted; public float Volume; public uint Channels; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    }
    [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback { [PreserveSig] int OnNotify(IntPtr data); }
    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class VolumeCallback(VolumeHudService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            try { var value = Marshal.PtrToStructure<VolumeNotification>(data); owner.Publish(value.Volume, value.Muted != 0); }
            catch { }
            return 0;
        }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DeviceCallback(VolumeHudService owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, uint state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        { if (flow == 0 && role == 1) _ = Task.Run(owner.Rebind); return 0; }
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }
}
