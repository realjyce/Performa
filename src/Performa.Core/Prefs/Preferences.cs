using System.Text.Json.Serialization;

namespace Performa.Prefs;

public enum OutputFormat { Markdown, Text }
public enum Verbosity { Terse, Normal, Detailed }
public enum Grouping { Area, Kind, Flat }
public enum Tone { Plain, Friendly }

public sealed class Preferences
{
    public bool Initialised { get; set; }

    /// <summary>What to call the user. Asked once, never inferred from an SSO profile.</summary>
    public string? UserName { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Markdown;
    public Verbosity Verbosity { get; set; } = Verbosity.Normal;
    public Grouping Grouping { get; set; } = Grouping.Area;
    public Tone Tone { get; set; } = Tone.Plain;
    public int RejectStreak { get; set; }
    public string? WorkspacePath { get; set; }

    // Stored only. The CLI never reads these; the desktop uses them for the
    // optional GitHub and Google calls. Core makes no network requests.
    public string? GitHubToken { get; set; }
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }

    /// <summary>Gemini key for the optional AI enricher. Free tier, user supplied.</summary>
    public string? GeminiApiKey { get; set; }

    /// <summary>Opt-in: nothing leaves the machine unless this is true.</summary>
    public bool AiEnabled { get; set; }
}

public sealed class StateFile
{
    public Dictionary<string, DateTimeOffset> LastStandup { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Preferences))]
[JsonSerializable(typeof(StateFile))]
public sealed partial class PerformaJsonContext : JsonSerializerContext;
