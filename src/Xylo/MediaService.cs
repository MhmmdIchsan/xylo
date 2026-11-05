using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Xylo;

internal sealed record MediaSnapshot(string Title, string Artist, string Album, bool Playing,
    double Position, double Duration, DateTimeOffset Updated, byte[]? Artwork = null, bool CanSeek = false)
{
    public double CurrentPosition => Math.Clamp(Position + (Playing ? Math.Max(0, (DateTimeOffset.UtcNow - Updated).TotalSeconds) : 0), 0, Duration > 0 ? Duration : double.MaxValue);
}

internal sealed class MediaService : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _changed;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private volatile MediaSnapshot? _snapshot;
    private string _artKey = "";
    private byte[]? _artwork;
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
                    {
                        string key = session.SourceAppUserModelId + "\n" + properties.Title + "\n" + properties.Artist + "\n" + properties.AlbumTitle;
                        if (key != _artKey)
                        {
                            _artKey = key;
                            _artwork = await ReadArtworkAsync(properties.Thumbnail);
                        }
                        next = new(properties.Title, properties.Artist ?? "", properties.AlbumTitle ?? "",
                            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                            timeline.Position.TotalSeconds, timeline.EndTime.TotalSeconds,
                            timeline.LastUpdatedTime == default ? DateTimeOffset.UtcNow : timeline.LastUpdatedTime,
                            _artwork, playback.Controls?.IsPlaybackPositionEnabled == true);
                    }
                }
                if (next == null) { _artKey = ""; _artwork = null; }
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

    private static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 1024 * 1024) return null;
            using var reader = new DataReader(stream);
            uint size = (uint)stream.Size;
            if (await reader.LoadAsync(size) != size) return null;
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch { return null; }
    }

    public async void Seek(double fraction)
    {
        try
        {
            var snapshot = _snapshot;
            var session = _manager?.GetCurrentSession();
            if (snapshot is { CanSeek: true, Duration: > 0 } && session != null)
                await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(snapshot.Duration * Math.Clamp(fraction, 0, 1)).Ticks);
        }
        catch { }
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
