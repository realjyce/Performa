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

    /// <summary>The automated end-of-day write-up, with a stamp saying when and
    /// why it was written. Auto-generated content always says so.</summary>
    public string Closeout { get; set; } = "";
    public string CloseoutStamp { get; set; } = "";

    /// <summary>Which day the close-out belongs to (yyyy-MM-dd). Yesterday's
    /// close-out is history, not today's page, so the date has to be stored
    /// rather than inferred from the stamp text.</summary>
    public string CloseoutDate { get; set; } = "";

    /// <summary>Email asks harvested into task suggestions, awaiting a click.
    /// Dismissals are remembered so a suggestion never comes back.</summary>
    public List<string> Suggested { get; set; } = [];
    public List<string> Dismissed { get; set; } = [];
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
