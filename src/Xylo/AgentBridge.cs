using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Xylo;

internal sealed record AgentApproval(string Agent, string Session, string Tool, string Project, string Details, TaskCompletionSource<string> Completion);
internal sealed record AgentActivity(string Agent, string Session, string Project, bool Working, DateTimeOffset Updated, bool Interrupted = false, long WindowHandle = 0, int WindowPid = 0);
internal sealed record AgentNotice(string Agent, string Session, string RequestId, string Tool, string Project, DateTimeOffset Updated, bool InputRequired = false);

// The pipe accepts only the current Windows user. Hook messages and approvals are never persisted.
internal sealed class AgentBridge : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private readonly List<AgentApproval> _pending = new();
    private readonly Dictionary<string, AgentActivity> _sessions = new();
    private readonly Dictionary<string, AgentNotice> _notices = new();
    private readonly Action _changed;
    private readonly Action<AgentActivity> _finished;
    public AgentBridge(Action changed, Action<AgentActivity> finished) { _changed = changed; _finished = finished; }
    public AgentApproval? Pending { get { lock (_lock) return _pending.FirstOrDefault(); } }
    public int PendingCount { get { lock (_lock) return _pending.Count; } }
    public AgentNotice? Notice
    {
        get { lock (_lock) return _notices.Values.Where(n => DateTimeOffset.UtcNow - n.Updated < TimeSpan.FromMinutes(n.InputRequired ? 30 : 2)).OrderBy(n => n.Updated).FirstOrDefault(); }
    }
    public AgentActivity? Activity
    {
        get
        {
            lock (_lock)
                return _sessions.Values.Where(s => DateTimeOffset.UtcNow - s.Updated < TimeSpan.FromMinutes(30))
                    .OrderByDescending(s => s.Working).ThenByDescending(s => s.Updated).FirstOrDefault();
        }
    }
    public AgentActivity[] Activities
    {
        get { lock (_lock) return _sessions.Values.Where(s => DateTimeOffset.UtcNow - s.Updated < TimeSpan.FromMinutes(30)).OrderByDescending(s => s.Working).ThenByDescending(s => s.Updated).Take(8).ToArray(); }
    }
    public AgentActivity? FindActivity(string session) { lock (_lock) return _sessions.GetValueOrDefault(session); }
    public void Start() => _ = Task.Run(AcceptAsync);

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream("xylo-agent", PipeDirection.InOut, 16,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                _ = HandleAsync(pipe);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); break; }
            catch { pipe?.Dispose(); if (!_stop.IsCancellationRequested) await Task.Delay(1000); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            AgentApproval? approval = null;
            try
            {
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[1024];
                while (bytes.Length <= 16384)
                {
                    int count = await pipe.ReadAsync(buffer, timeout.Token);
                    if (count == 0) return;
                    int newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
                    bytes.Write(buffer, 0, newline < 0 ? count : newline);
                    if (newline >= 0) break;
                }
                if (bytes.Length > 16384) return;
                using var json = JsonDocument.Parse(bytes.ToArray());
                var root = json.RootElement;
                string kind = Read(root, "event");
                string agent = Read(root, "agent");
                if (agent is not ("Codex" or "Claude Code" or "Cursor" or "OpenCode" or "Antigravity")) return;
                if (kind != "PermissionRequest")
                {
                    string session = agent + ":" + Read(root, "session");
                    if (session.Length > 0 && session.Length <= 200)
                    {
                        AgentActivity? finished = null;
                        lock (_lock)
                        {
                            string requestId = Read(root, "requestId");
                            if (kind == "PermissionNotice" && requestId.Length is > 0 and <= 200)
                            {
                                if (_notices.Count >= 16 && !_notices.ContainsKey(requestId))
                                    _notices.Remove(_notices.MinBy(pair => pair.Value.Updated).Key);
                                _notices[requestId] = new(agent, session, requestId, Read(root, "tool"), Read(root, "project"), DateTimeOffset.UtcNow);
                            }
                            else if (kind == "PermissionResolved") _notices.Remove(requestId);
                            else if (kind == "InputRequired" && requestId.Length is > 0 and <= 200)
                            {
                                string key = "input:" + session + ":" + requestId;
                                if (_notices.Count >= 16 && !_notices.ContainsKey(key)) _notices.Remove(_notices.MinBy(pair => pair.Value.Updated).Key);
                                _notices[key] = new(agent, session, requestId, "User input", Read(root, "project"), DateTimeOffset.UtcNow, true);
                                if (_sessions.TryGetValue(session, out var waiting)) _sessions[session] = waiting with { Working = false, Updated = DateTimeOffset.UtcNow };
                            }
                            else if (kind == "InputResolved" && requestId.Length is > 0 and <= 200)
                            {
                                string key = "input:" + session + ":" + requestId;
                                if (_notices.Remove(key) && !_notices.Values.Any(n => n.InputRequired && n.Session == session) && _sessions.TryGetValue(session, out var waiting))
                                    _sessions[session] = waiting with { Working = true, Interrupted = false, Updated = DateTimeOffset.UtcNow };
                            }
                            else if (kind == "SessionEnd")
                            {
                                _sessions.Remove(session);
                                foreach (string key in _notices.Where(n => n.Value.Session == session).Select(n => n.Key).ToArray()) _notices.Remove(key);
                            }
                            else if (kind is "UserPromptSubmit" or "Stop" or "Interrupt")
                            {
                                bool wasWorking = _sessions.TryGetValue(session, out var previous) && previous.Working;
                                foreach (string key in _notices.Where(n => n.Value.Session == session && n.Value.InputRequired).Select(n => n.Key).ToArray()) _notices.Remove(key);
                                if (_sessions.Count >= 32 && !_sessions.ContainsKey(session))
                                    _sessions.Remove(_sessions.MinBy(pair => pair.Value.Updated).Key);
                                long handle = ReadInteger(root, "windowHandle"), pid = ReadInteger(root, "windowPid");
                                _sessions[session] = ActivityForEvent(agent, session, Read(root, "project"), kind, previous,
                                    handle > 0 && pid is > 0 and <= int.MaxValue ? handle : 0, pid is > 0 and <= int.MaxValue ? (int)pid : 0);
                                if (kind == "Stop" && wasWorking) finished = _sessions[session];
                            }
                        }
                        if (finished != null) _finished(finished);
                        _changed();
                    }
                    await RespondAsync(pipe, "{}", timeout.Token);
                    return;
                }
                string tool = Read(root, "tool");
                string details = Read(root, "details");
                if (tool.Length == 0 || details.Length == 0 || details.Length > 4096) return;
                approval = new(agent, Read(root, "session"), tool, Read(root, "project"), details,
                    new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
                lock (_lock) { if (_pending.Count >= 8) return; _pending.Add(approval); }
                _changed();
                string decision = await approval.Completion.Task.WaitAsync(timeout.Token);
                string response = decision == "allow"
                    ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "allow" } } })
                    : decision == "deny"
                        ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "deny", message = "Denied in Xylo." } } })
                        : "{}";
                await RespondAsync(pipe, response, timeout.Token);
            }
            catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or ObjectDisposedException) { }
            finally
            {
                if (approval != null) { lock (_lock) _pending.Remove(approval); _changed(); }
            }
        }
    }

    private static string Read(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long ReadInteger(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : 0;
    internal static AgentActivity ActivityForEvent(string agent, string session, string project, string kind, AgentActivity? previous = null, long windowHandle = 0, int windowPid = 0) =>
        new(agent, session, project, kind == "UserPromptSubmit", DateTimeOffset.UtcNow, kind == "Interrupt", windowHandle > 0 ? windowHandle : previous?.WindowHandle ?? 0, windowHandle > 0 ? windowPid : previous?.WindowPid ?? 0);
    private static async Task RespondAsync(NamedPipeServerStream pipe, string json, CancellationToken token)
    {
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), token);
        await pipe.FlushAsync(token);
    }
    public void Dispose() { _stop.Cancel(); lock (_lock) foreach (var item in _pending) item.Completion.TrySetResult("defer"); }
}
