using System.Diagnostics;
using System.Text.Json;

namespace Xylo;

internal sealed record CodexLimitWindow(double RemainingPercent, int DurationMinutes, DateTimeOffset? ResetsAt)
{
    public string Label => DurationMinutes == 10080 ? "Weekly" : DurationMinutes % 1440 == 0 ? $"{DurationMinutes / 1440}d" : DurationMinutes % 60 == 0 ? $"{DurationMinutes / 60}h" : $"{DurationMinutes}m";
}
internal sealed record CodexLimits(CodexLimitWindow? Primary, CodexLimitWindow? Secondary, DateTimeOffset Updated);

// Queries the local Codex app-server on demand; no credentials or responses are persisted.
internal sealed class CodexLimitService : IDisposable
{
    private readonly Action _changed;
    private readonly CancellationTokenSource _stop = new();
    private long _nextCheckTicks;
    private int _running;
    private volatile CodexLimits? _snapshot;
    private volatile string _status = "Open Agent to load limits";
    public CodexLimits? Snapshot => _snapshot;
    public string Status => _status;
    public CodexLimitService(Action changed) => _changed = changed;

    public void RefreshIfDue()
    {
        if (_stop.IsCancellationRequested || DateTime.UtcNow.Ticks < Interlocked.Read(ref _nextCheckTicks) || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        Interlocked.Exchange(ref _nextCheckTicks, DateTime.UtcNow.AddMinutes(5).Ticks);
        _status = "Loading Codex limits...";
        _ = Task.Run(FetchAsync);
    }

    private async Task FetchAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        Process? process = null;
        try
        {
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            var start = new ProcessStartInfo(File.Exists(installed) ? installed : "codex.exe", "app-server")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            process = Process.Start(start) ?? throw new InvalidOperationException();
            var drain = DrainAsync(process.StandardError, timeout.Token);
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"xylo\",\"title\":\"Xylo\",\"version\":\"0.1.0\"}}}");
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}");
            await process.StandardInput.FlushAsync(timeout.Token);
            CodexLimits? result = null;
            for (int i = 0; i < 100; i++)
            {
                string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line == null || line.Length > 262144) break;
                using var message = JsonDocument.Parse(line);
                if (message.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int value) && value == 2)
                { result = Parse(message.RootElement); break; }
            }
            if (result == null) throw new InvalidOperationException();
            if (!_stop.IsCancellationRequested) { _snapshot = result; _status = ""; }
            _ = drain;
        }
        catch (Exception)
        {
            if (!_stop.IsCancellationRequested)
            {
                _snapshot = null;
                _status = "Codex limits unavailable";
                Interlocked.Exchange(ref _nextCheckTicks, DateTime.UtcNow.AddMinutes(1).Ticks);
            }
        }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                process.Dispose();
            }
            Interlocked.Exchange(ref _running, 0);
            if (!_stop.IsCancellationRequested) _changed();
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        try { var buffer = new char[1024]; while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { } }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
    }

    internal static CodexLimits? Parse(JsonElement message)
    {
        if (!message.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return null;
        JsonElement limits = default;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty("codex", out var codex)) limits = codex;
        else if (result.TryGetProperty("rateLimits", out var legacy)) limits = legacy;
        if (limits.ValueKind != JsonValueKind.Object) return null;
        var primary = ReadWindow(limits, "primary");
        var secondary = ReadWindow(limits, "secondary");
        return primary == null && secondary == null ? null : new CodexLimits(primary, secondary, DateTimeOffset.Now);
    }

    private static CodexLimitWindow? ReadWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out double percentage) || !double.IsFinite(percentage) ||
            !value.TryGetProperty("windowDurationMins", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out int minutes) || minutes <= 0) return null;
        DateTimeOffset? reset = null;
        if (value.TryGetProperty("resetsAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out long seconds))
            try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime(); } catch (ArgumentOutOfRangeException) { }
        return new CodexLimitWindow(100 - Math.Clamp(percentage, 0, 100), minutes, reset);
    }

    public void Dispose() => _stop.Cancel();
}
