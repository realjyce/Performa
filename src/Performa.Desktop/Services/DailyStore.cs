using System.Text.Json;

namespace Performa.Desktop.Services;

public sealed class DailyTask
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }

    /// <summary>yyyy-MM-dd, or null for "sometime". Null is the honest default:
    /// most tasks genuinely have no deadline, and inventing one to make sorting
    /// easier would make the ranking a lie.</summary>
    public string? Due { get; set; }

    /// <summary>yyyy-MM-dd the task was ticked, or null while it is open.
    /// Without it "yesterday's finished work" is unanswerable: Done alone says
    /// that it happened, never when.</summary>
    public string? DoneAt { get; set; }

    /// <summary>How many times this has been pushed to another day. Not used to
    /// sort, only to say so: a task deferred four times is not urgent, it is
    /// something the user does not want to do, and the wording should admit
    /// that rather than keep presenting it as next.</summary>
    public int Deferred { get; set; }
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

    /// <summary>Tasks finished on an earlier day, moved off the live list so it
    /// stays a list of what is left. Kept rather than deleted: the close-out
    /// counts them, and "what did I get done last week" is a fair question.</summary>
    public List<DailyTask> Completed { get; set; } = [];
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
