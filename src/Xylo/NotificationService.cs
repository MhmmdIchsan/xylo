using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Xylo;

internal sealed record NotificationPreview(string App, string Title, string Body, float? Progress = null, bool IsHud = false, string HudSource = "", string Kind = "");

internal sealed class NotificationService : IDisposable
{
    private readonly Action<NotificationPreview> _received;
    private readonly Action _statusChanged;
    private CancellationTokenSource? _stop;
    private volatile bool _active;
    private volatile bool _requesting;
    private volatile bool _disposed;
    private int _unreadCount;
    private readonly object _snapshotGate = new();
    private NotificationPreview[] _activeNotifications = Array.Empty<NotificationPreview>();
    public int UnreadCount => Volatile.Read(ref _unreadCount);
    public NotificationPreview[] ActiveNotifications => Volatile.Read(ref _activeNotifications);
    public bool Active => _active;
    public bool Requesting => _requesting;
    public string? LastError { get; private set; }

    public NotificationService(Action<NotificationPreview> received, Action statusChanged)
    {
        _received = received;
        _statusChanged = statusChanged;
    }

    // Must be invoked on the window thread: Windows requires a UI-thread access request.
    public void Start(bool prompt)
    {
        if (_disposed || _active || _requesting) return;
        LastError = null;
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed) Attach(listener);
            else if (prompt) { _requesting = true; _ = RequestAsync(listener); }
            else { LastError = "Notification access is not enabled in Windows settings."; _statusChanged(); }
        }
        catch
        {
            LastError = "Windows notification access is unavailable for this installation.";
            _statusChanged();
        }
    }

    private async Task RequestAsync(UserNotificationListener listener)
    {
        try
        {
            var status = await listener.RequestAccessAsync();
            if (status == UserNotificationListenerAccessStatus.Allowed && !_disposed) Attach(listener);
            else LastError = "Notification access was not granted. Check Windows notification privacy settings.";
        }
        catch { LastError = "Windows notification access is unavailable for this installation."; }
        finally { _requesting = false; _statusChanged(); }
    }

    private void Attach(UserNotificationListener listener)
    {
        var run = new CancellationTokenSource();
        _stop = run;
        _active = true;
        _statusChanged();
        _ = Task.Run(() => PollAsync(listener, run));
    }

    private async Task PollAsync(UserNotificationListener listener, CancellationTokenSource run)
    {
        var token = run.Token;
        var seen = new HashSet<uint>();
        var cache = new Dictionary<uint, NotificationPreview>();
        bool initialized = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                {
                    LastError = "Notification access was revoked in Windows settings.";
                    break;
                }
                var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                token.ThrowIfCancellationRequested();
                var current = new HashSet<uint>();
                var display = new List<NotificationPreview>(3);
                var keep = new HashSet<uint>();
                foreach (var notification in notifications.OrderByDescending(n => n.CreationTime))
                {
                    current.Add(notification.Id);
                    bool isNew = initialized && !seen.Contains(notification.Id);
                    bool show = display.Count < 3;
                    if (!isNew && !show) continue;
                    if (show && cache.TryGetValue(notification.Id, out var cached))
                    {
                        display.Add(cached); keep.Add(notification.Id);
                        if (isNew) _received(cached);
                        continue;
                    }
                    string app = "Notification", title = "", body = "";
                    try { app = notification.AppInfo?.DisplayInfo?.DisplayName ?? app; }
                    catch { /* Some desktop apps do not expose AppInfo. */ }
                    try
                    {
                        var elements = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                        title = elements is { Count: > 0 } ? elements[0].Text ?? "" : "";
                        body = elements is { Count: > 1 } ? string.Join(" ", elements.Skip(1).Select(t => t.Text)) : "";
                    }
                    catch { /* Some apps provide a toast without readable text. */ }
                    var preview = CreatePreview(app, title, body);
                    if (show) { display.Add(preview); cache[notification.Id] = preview; keep.Add(notification.Id); }
                    if (isNew && !token.IsCancellationRequested) _received(preview);
                }
                foreach (var id in cache.Keys.Where(id => !keep.Contains(id)).ToArray()) cache.Remove(id);
                bool changed = false;
                lock (_snapshotGate)
                {
                    if (ReferenceEquals(_stop, run) && !token.IsCancellationRequested)
                    {
                        changed = _unreadCount != notifications.Count || !_activeNotifications.SequenceEqual(display);
                        if (changed)
                        {
                            Volatile.Write(ref _unreadCount, notifications.Count);
                            Volatile.Write(ref _activeNotifications, display.ToArray());
                        }
                    }
                }
                if (changed) _statusChanged();
                seen = current;
                initialized = true;
                await Task.Delay(1500, token);
            }
        }
        catch (OperationCanceledException) { }
        catch { LastError = "Could not read Windows notifications."; }
        finally
        {
            bool stopped = false;
            lock (_snapshotGate)
            {
                if (ReferenceEquals(_stop, run))
                {
                    _stop = null; _active = false; stopped = true;
                    Volatile.Write(ref _unreadCount, 0);
                    Volatile.Write(ref _activeNotifications, Array.Empty<NotificationPreview>());
                }
            }
            if (stopped) _statusChanged();
            run.Dispose();
        }
    }

    internal static NotificationPreview CreatePreview(string app, string title, string body)
    {
        bool whatsapp = app.Contains("whatsapp", StringComparison.OrdinalIgnoreCase) || title.Contains("web.whatsapp.com", StringComparison.OrdinalIgnoreCase);
        bool call = whatsapp && new[] { "incoming call", "calling", "panggilan", "video call", "voice call" }.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase) || body.Contains(word, StringComparison.OrdinalIgnoreCase));
        return new NotificationPreview(Short(app, 100), Short(string.IsNullOrEmpty(title) ? app : title, 200), Short(body, 500), Kind: call ? "whatsapp-call" : whatsapp ? "whatsapp" : "notification");
    }
    internal static string SourceLabel(NotificationPreview item)
    {
        string app = item.Kind.StartsWith("whatsapp", StringComparison.Ordinal) ? "WhatsApp" : item.App;
        app = SingleLine(app);
        string sender = SingleLine(item.Title);
        if (string.IsNullOrWhiteSpace(sender) || sender.Equals(app, StringComparison.OrdinalIgnoreCase) || sender.Equals("web.whatsapp.com", StringComparison.OrdinalIgnoreCase)) return app;
        return app + " / " + sender;
    }
    internal static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
    private static string Short(string text, int limit) => text.Length <= limit ? text : text[..limit] + "…";
    public void Stop()
    {
        lock (_snapshotGate) { var run = _stop; _stop = null; run?.Cancel(); Volatile.Write(ref _unreadCount, 0); Volatile.Write(ref _activeNotifications, Array.Empty<NotificationPreview>()); }
        _active = false;
    }
    public void Dispose() { _disposed = true; Stop(); }
}
