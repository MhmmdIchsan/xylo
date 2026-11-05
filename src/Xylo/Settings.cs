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
    public bool BatteryAlertsEnabled { get; set; } = true;
    public bool ConnectivityAlertsEnabled { get; set; } = true;
    public bool VolumeHudEnabled { get; set; } = true;
    public bool BrightnessHudEnabled { get; set; }
    public bool LyricsEnabled { get; set; } = true;
    public int DashboardSlide { get; set; }
    public bool ShowBatteryPercent { get; set; } = true;
    public int SizePreset { get; set; } = 1;
    public int MonitorIndex { get; set; }
    public List<int> TabOrder { get; set; } = Enumerable.Range(0, 7).ToList();
    public List<int> HiddenTabs { get; set; } = new();
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
            settings.NormalizeDisplay();
            return settings;
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public void NormalizeDisplay()
    {
        TabOrder = (TabOrder ?? new()).Where(i => i is >= 0 and <= 6).Distinct().Concat(Enumerable.Range(0, 7)).Distinct().ToList();
        HiddenTabs = (HiddenTabs ?? new()).Where(i => i is >= 1 and <= 6).Distinct().ToList();
        SizePreset = Math.Clamp(SizePreset, 0, 2);
        MonitorIndex = Math.Max(0, MonitorIndex);
        if (HiddenTabs.Contains(DashboardSlide) || DashboardSlide is < 0 or > 6) DashboardSlide = 0;
    }
    public int[] VisibleTabs() => TabOrder.Where(i => !HiddenTabs.Contains(i)).ToArray();

    public void Save()
    {
        if (Transient) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
