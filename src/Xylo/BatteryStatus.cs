using System.Runtime.InteropServices;

namespace Xylo;

internal readonly record struct BatteryStatus(int? Percent, bool Charging, bool PluggedIn)
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
        if (!GetSystemPowerStatus(out var status) || status.BatteryFlag == 255 ||
            (status.BatteryFlag & 128) != 0 || status.BatteryLifePercent > 100)
            return new(null, false, status.AcLineStatus == 1);
        return new(status.BatteryLifePercent, (status.BatteryFlag & 8) != 0, status.AcLineStatus == 1);
    }
}
