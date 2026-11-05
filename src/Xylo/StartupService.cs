using Microsoft.Win32;

namespace Xylo;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Xylo";

    internal static string CommandFor(string path) => "\"" + path + "\"";

    public static bool Enabled
    {
        get
        {
            try
            {
                string? executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable)) return false;
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return string.Equals(key?.GetValue(ValueName) as string, CommandFor(executable), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) throw new IOException("Executable path is unavailable.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true) ?? throw new IOException("Startup settings are unavailable.");
        if (enabled) key.SetValue(ValueName, CommandFor(executable), RegistryValueKind.String);
        else if (string.Equals(key.GetValue(ValueName) as string, CommandFor(executable), StringComparison.OrdinalIgnoreCase)) key.DeleteValue(ValueName, false);
    }
}
