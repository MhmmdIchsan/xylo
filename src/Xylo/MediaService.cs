using Windows.Media.Control;

namespace Xylo;

internal sealed record MediaSnapshot(string Title, string Artist, string Album, bool Playing,
    double Position, double Duration, DateTimeOffset Updated)
{
    public double CurrentPosition => Math.Clamp(Position + (Playing ? Math.Max(0, (DateTimeOffset.UtcNow - Updated).TotalSeconds) : 0), 0, Duration > 0 ? Duration : double.MaxValue);
}

internal sealed class MediaService : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _changed;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private volatile MediaSnapshot? _snapshot;
    public MediaSnapshot? Snapshot => _snapshot;

    public MediaService(Action changed) => _changed = changed;
    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = _manager.GetCurrentSession();
                MediaSnapshot? next = null;
                if (session != null)
                {
                    var properties = await session.TryGetMediaPropertiesAsync();
                    var playback = session.GetPlaybackInfo();
                    var timeline = session.GetTimelineProperties();
                    if (!string.IsNullOrWhiteSpace(properties.Title))
                        next = new(properties.Title, properties.Artist ?? "", properties.AlbumTitle ?? "",
                            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                            timeline.Position.TotalSeconds, timeline.EndTime.TotalSeconds,
                            timeline.LastUpdatedTime == default ? DateTimeOffset.UtcNow : timeline.LastUpdatedTime);
                }
                var previous = _snapshot;
                _snapshot = next;
                if (previous != next || next?.Playing == true) _changed();
            }
            catch (Exception) when (!_stop.IsCancellationRequested)
            {
                if (_snapshot != null) { _snapshot = null; _changed(); }
                _manager = null;
            }
            try { await Task.Delay(1000, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async void Control(string action)
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session == null) return;
            if (action == "previous") await session.TrySkipPreviousAsync();
            else if (action == "toggle") await session.TryTogglePlayPauseAsync();
            else if (action == "next") await session.TrySkipNextAsync();
        }
        catch { /* The media player may close or reject a command. */ }
    }

    public void Dispose() => _stop.Cancel();
}
