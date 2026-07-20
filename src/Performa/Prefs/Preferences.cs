using System.Text.Json.Serialization;

namespace Performa.Prefs;

public enum OutputFormat { Markdown, Text }
public enum Verbosity { Terse, Normal, Detailed }
public enum Grouping { Area, Kind, Flat }
public enum Tone { Plain, Friendly }

public sealed class Preferences
{
    public bool Initialised { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Markdown;
    public Verbosity Verbosity { get; set; } = Verbosity.Normal;
    public Grouping Grouping { get; set; } = Grouping.Area;
    public Tone Tone { get; set; } = Tone.Plain;
    public int RejectStreak { get; set; }
}

public sealed class StateFile
{
    public Dictionary<string, DateTimeOffset> LastStandup { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Preferences))]
[JsonSerializable(typeof(StateFile))]
public sealed partial class PerformaJsonContext : JsonSerializerContext;
