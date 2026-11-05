using System.Globalization;
using System.Text.Json;

namespace Xylo;

internal sealed record WeatherSnapshot(string City, double Temperature, int Code)
{
    public string Condition => Code switch
    {
        0 => "Clear", <= 3 => "Cloudy", 45 or 48 => "Fog", >= 51 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow", >= 80 and <= 82 => "Rain", 85 or 86 => "Snow",
        >= 95 => "Storm", _ => "Weather"
    };
    public string Summary => $"{Math.Round(Temperature):0}°C {Condition}";
}

internal sealed class WeatherService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _changed;
    private volatile WeatherSnapshot? _snapshot;
    public WeatherSnapshot? Snapshot => _snapshot;

    public WeatherService(Action changed) => _changed = changed;
    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            bool success = false;
            try
            {
                var result = await FetchAsync(_stop.Token);
                if (_stop.IsCancellationRequested) break;
                _snapshot = result;
                success = true;
                _changed();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch { /* Retain the last reading while offline. */ }
            try { await Task.Delay(success ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5), _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal static async Task<WeatherSnapshot> FetchAsync(CancellationToken token)
    {
        using var location = await FetchJsonAsync("https://ipwho.is/", token);
        var root = location.RootElement;
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Location unavailable.");
        double lat = root.GetProperty("latitude").GetDouble(), lon = root.GetProperty("longitude").GetDouble();
        if (lat is < -90 or > 90 || lon is < -180 or > 180) throw new InvalidOperationException("Invalid coordinates.");
        string city = root.TryGetProperty("city", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";
        if (city.Length > 80) city = city[..80];
        string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(CultureInfo.InvariantCulture) +
            "&longitude=" + lon.ToString(CultureInfo.InvariantCulture) + "&current=temperature_2m,weather_code";
        using var weather = await FetchJsonAsync(url, token);
        var current = weather.RootElement.GetProperty("current");
        double temperature = current.GetProperty("temperature_2m").GetDouble();
        if (temperature is < -100 or > 80) throw new InvalidOperationException("Invalid temperature.");
        return new WeatherSnapshot(city, temperature, current.GetProperty("weather_code").GetInt32());
    }

    private static async Task<JsonDocument> FetchJsonAsync(string url, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidOperationException("Weather response too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream);
        char[] buffer = new char[32769];
        int count = 0, read;
        while (count < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(count), token)) > 0) count += read;
        if (count == buffer.Length) throw new InvalidOperationException("Weather response too large.");
        return JsonDocument.Parse(new string(buffer, 0, count));
    }

    public void Dispose() => _stop.Cancel();
}
