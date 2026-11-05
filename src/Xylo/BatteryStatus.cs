using System.Runtime.InteropServices;

namespace Xylo;

internal readonly record struct BatteryStatus(int? Percent, bool Charging, bool? PluggedIn)
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static BatteryStatus Read()
    {
        if (!GetSystemPowerStatus(out var status)) return new(null, false, null);
        bool? plugged = status.AcLineStatus switch { 0 => false, 1 => true, _ => null };
        if (status.BatteryFlag == 255 ||
            (status.BatteryFlag & 128) != 0 || status.BatteryLifePercent > 100)
            return new(null, false, plugged);
        return new(status.BatteryLifePercent, (status.BatteryFlag & 8) != 0, plugged);
    }

    internal static NotificationPreview? Alert(BatteryStatus previous, BatteryStatus current)
    {
        if (previous.Percent is not int before || current.Percent is not int now) return null;
        if (current.PluggedIn == false && before > 10 && now <= 10)
            return new("Battery", "Battery critical", $"{now}% remaining · connect power");
        if (current.PluggedIn == false && before > 20 && now <= 20)
            return new("Battery", "Battery low", $"{now}% remaining");
        if (previous.PluggedIn != null && current.PluggedIn != null && previous.PluggedIn != current.PluggedIn)
            return new("Battery", current.PluggedIn == true ? "Power connected" : "Power disconnected", $"{now}% battery");
        return null;
    }
}
