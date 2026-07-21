using System.Text.Json;

namespace Performa.Desktop.Services;

public sealed class DailyTask
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class DailyData
{
    public List<DailyTask> Tasks { get; set; } = [];
    public string Notes { get; set; } = "";
}

/// <summary>Local, on-disk store for the Daily module. No network, just JSON in %APPDATA%.</summary>
public sealed class DailyStore
{
    private readonly string _path;

    public DailyStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "performa");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "daily.json");
    }

    public DailyData Load()
    {
        if (!File.Exists(_path)) return new DailyData();
        try
        {
            return JsonSerializer.Deserialize<DailyData>(File.ReadAllText(_path))
                ?? new DailyData();
        }
        catch (JsonException)
        {
            return new DailyData();
        }
    }

    public void Save(DailyData data)
        => File.WriteAllText(_path,
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
}
