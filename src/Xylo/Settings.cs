using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xylo;

internal sealed class TaskItem
{
    public string Title { get; set; } = "";
    public DateTime? Due { get; set; }
    public bool HasTime { get; set; }
    public bool Done { get; set; }
}

internal sealed class Settings
{
    public bool Detached { get; set; }
    public bool ClipboardEnabled { get; set; }
    public bool NotificationsEnabled { get; set; }
    public bool WeatherEnabled { get; set; }
    public List<TaskItem> Tasks { get; set; } = new();
    [JsonIgnore]
    internal bool Transient { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Xylo", "settings.json");

    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            settings.Tasks ??= new();
            settings.Tasks = settings.Tasks.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Title)).Take(30).ToList();
            return settings;
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        if (Transient) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
