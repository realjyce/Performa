using System.Text.Json.Serialization;

namespace Performa.Prefs;

public enum OutputFormat { Markdown, Text }
public enum Verbosity { Terse, Normal, Detailed }
public enum Grouping { Area, Kind, Flat }
public enum Tone { Plain, Friendly }

/// <summary>Carbon is the identity, so it stays the default.</summary>
public enum AppTheme { Dark, Light }

/// <summary>
/// Which vendor answers when the optional AI layer is on. Gemini leads because
/// it is the only one of the three with a free tier, so a fresh install can
/// try the feature without a card on file.
/// </summary>
public enum AiProvider { Gemini, Claude, OpenAi }

public sealed class Preferences
{
    public bool Initialised { get; set; }

    /// <summary>Set once the launch walkthrough has been seen, so it never nags again.</summary>
    public bool OnboardingDone { get; set; }

    /// <summary>What to call the user. Asked once, never inferred from an SSO profile.</summary>
    public string? UserName { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Markdown;
    public Verbosity Verbosity { get; set; } = Verbosity.Normal;
    public Grouping Grouping { get; set; } = Grouping.Area;
    public Tone Tone { get; set; } = Tone.Plain;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int RejectStreak { get; set; }
    public string? WorkspacePath { get; set; }

    // Stored only. The CLI never reads these; the desktop uses them for the
    // optional GitHub and Google calls. Core makes no network requests.
    public string? GitHubToken { get; set; }

    /// <summary>OAuth App client id for GitHub's device flow. No secret needed,
    /// which is why the device flow is used rather than the web flow.</summary>
    public string? GitHubClientId { get; set; }
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }

    // Keys for the optional AI enricher, one per vendor so switching provider
    // does not make you re-paste the key you were using before.
    /// <summary>Gemini key. Free tier, user supplied.</summary>
    public string? GeminiApiKey { get; set; }

    /// <summary>Anthropic key. Billed per token.</summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>OpenAI key. Billed per token.</summary>
    public string? OpenAiApiKey { get; set; }

    /// <summary>Which vendor gets the question when AI is on.</summary>
    public AiProvider AiProvider { get; set; } = AiProvider.Gemini;

    /// <summary>
    /// Gates every outbound model call. On by default so a fresh install answers
    /// in prose without setup; turning it off in Settings keeps the machine
    /// silent, and the deterministic answers are unchanged either way.
    /// </summary>
    public bool AiEnabled { get; set; } = true;
}

public sealed class StateFile
{
    public Dictionary<string, DateTimeOffset> LastStandup { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Preferences))]
[JsonSerializable(typeof(StateFile))]
public sealed partial class PerformaJsonContext : JsonSerializerContext;
