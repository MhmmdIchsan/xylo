using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Xylo;

internal sealed record LyricLine(double At, string Text);
internal sealed record LyricsSnapshot(string Key, LyricLine[] Lines, string? Plain, string Status)
{
    public string Current(double position)
    {
        if (Lines.Length == 0) return Plain?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? Status;
        string result = "♪";
        foreach (var line in Lines)
        {
            if (line.At > position) break;
            result = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;
        }
        return result;
    }

    public string[] Visible(double position)
    {
        if (Lines.Length == 0)
            return Plain?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3).ToArray() ?? new[] { Status };
        int current = Array.FindLastIndex(Lines, line => line.At <= position);
        return Lines.Skip(Math.Max(0, current)).Take(3).Select(line => string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text).ToArray();
    }
}

internal sealed class LyricsService : IDisposable
{
    private static readonly Regex Timestamp = new(@"\[(\d{1,3}):(\d{2}(?:[.,]\d{1,3})?)\]");
    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Action _changed;
    private CancellationTokenSource? _request;
    private volatile LyricsSnapshot? _snapshot;
    private string _key = "";
    private DateTimeOffset _nextAllowed;
    private bool _disposed;
    public LyricsSnapshot? Snapshot => _snapshot;

    public LyricsService(Action changed) => _changed = changed;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Xylo/0.1 (https://github.com/MhmmdIchsan/xylo)");
        return client;
    }

    public void Update(MediaSnapshot? media)
    {
        string key = media == null || string.IsNullOrWhiteSpace(media.Artist) ? "" : media.Title + "\n" + media.Artist + "\n" + media.Album;
        if (key == _key) return;
        _key = key;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        if (key.Length == 0) { _snapshot = null; return; }
        _snapshot = new(key, Array.Empty<LyricLine>(), null, "Looking for lyrics…");
        _request = new CancellationTokenSource();
        _ = FetchAsync(key, media!, _request.Token);
    }

    private async Task FetchAsync(string key, MediaSnapshot media, CancellationToken token)
    {
        bool acquired = false;
        try
        {
            await _serial.WaitAsync(token);
            acquired = true;
            TimeSpan wait = _nextAllowed - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(media.Title) +
                "&artist_name=" + Uri.EscapeDataString(media.Artist);
            if (!string.IsNullOrWhiteSpace(media.Album)) url += "&album_name=" + Uri.EscapeDataString(media.Album);
            if (media.Duration is >= 1 and <= 3600) url += "&duration=" + Math.Round(media.Duration).ToString(CultureInfo.InvariantCulture);
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            _nextAllowed = DateTimeOffset.UtcNow.AddMilliseconds(350);
            if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter;
                TimeSpan delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(30);
                _nextAllowed = DateTimeOffset.UtcNow.Add(delay < TimeSpan.Zero ? TimeSpan.Zero : delay).AddSeconds(Random.Shared.NextDouble());
            }
            if (!response.IsSuccessStatusCode) { Publish(key, null, null, "Lyrics unavailable", token); return; }
            if (response.Content.Headers.ContentLength > 65536) { Publish(key, null, null, "Lyrics unavailable", token); return; }
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            char[] buffer = new char[32769];
            int count = 0, read;
            while (count < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(count), token)) > 0) count += read;
            if (count == buffer.Length) { Publish(key, null, null, "Lyrics unavailable", token); return; }
            using var json = JsonDocument.Parse(new string(buffer, 0, count));
            var root = json.RootElement;
            string? synced = GetString(root, "syncedLyrics");
            string? plain = GetString(root, "plainLyrics");
            bool instrumental = root.TryGetProperty("instrumental", out var flag) && flag.ValueKind == JsonValueKind.True;
            var lines = Parse(synced);
            Publish(key, lines, plain, instrumental ? "Instrumental" : "Lyrics unavailable", token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Publish(key, null, null, "Lyrics unavailable", token); }
        finally { if (acquired) _serial.Release(); }
    }

    private void Publish(string key, LyricLine[]? lines, string? plain, string status, CancellationToken token)
    {
        if (token.IsCancellationRequested || _disposed || key != _key) return;
        _snapshot = new(key, lines ?? Array.Empty<LyricLine>(), plain, status);
        _changed();
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static LyricLine[] Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<LyricLine>();
        var lines = new List<LyricLine>();
        foreach (string line in text.Split('\n'))
        {
            var matches = Timestamp.Matches(line);
            string content = Timestamp.Replace(line, "").Trim();
            foreach (Match match in matches)
                if (int.TryParse(match.Groups[1].Value, out int minutes) &&
                    double.TryParse(match.Groups[2].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds))
                    lines.Add(new LyricLine(minutes * 60 + seconds, content));
        }
        return lines.OrderBy(line => line.At).ToArray();
    }

    public void Dispose() { _disposed = true; _request?.Cancel(); _request?.Dispose(); }
}
