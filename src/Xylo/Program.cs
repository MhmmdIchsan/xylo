using System.Diagnostics;
using System.Text.Json;

namespace Xylo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--test-task-dialog"))
        {
            var result = TaskDialog.Edit(IntPtr.Zero, new TaskItem { Title = "old", Done = true });
            return result is { Title: "synthetic task", Done: true, HasTime: true, Due.Year: 2030, Due.Month: 12, Due.Day: 24, Due.Hour: 13, Due.Minute: 30 } ? 0 : 1;
        }
        using var instance = new Mutex(true, @"Local\Xylo.Desktop", out bool created);
        if (!created) return 0;
        try
        {
            using var app = new IslandApp(args.Contains("--smoke-test"));
            app.Run();
            return 0;
        }
        catch (Exception error)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Xylo");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "last-error.txt"), error.ToString());
            if (!args.Contains("--smoke-test"))
                Native.MessageBox(IntPtr.Zero, $"Xylo could not start. Details: {directory}\\last-error.txt", "Xylo", 0x10);
            return 1;
        }
    }
}

internal sealed record PerformanceSample(long WorkingSetBytes, long PrivateBytes, uint GdiObjects, double CpuMilliseconds)
{
    public static PerformanceSample Capture()
    {
        using var process = Process.GetCurrentProcess();
        return new(process.WorkingSet64, process.PrivateMemorySize64,
            Native.GetGuiResources(process.Handle, 0), process.TotalProcessorTime.TotalMilliseconds);
    }
}

internal sealed record SmokeReport(PerformanceSample Initial, PerformanceSample BeforeIdle,
    PerformanceSample AfterIdle, long RenderCount, long IdleRenderCount, bool HotkeyRegistered, bool TrayRegistered)
{
    public void Save() => File.WriteAllText(Path.Combine(Path.GetTempPath(), "xylo-smoke-test.json"),
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
