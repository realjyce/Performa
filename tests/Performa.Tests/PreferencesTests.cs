using System.Text.Json;
using Performa.Prefs;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// Preferences go through a source-generated serializer. A property the
/// generator does not see is silently dropped on save, which looks to the user
/// like "my token never saved", so every stored field is asserted here.
/// </summary>
public class PreferencesTests
{
    private static Preferences RoundTrip(Preferences prefs)
    {
        var json = JsonSerializer.Serialize(prefs, PerformaJsonContext.Default.Preferences);
        return JsonSerializer.Deserialize(json, PerformaJsonContext.Default.Preferences)!;
    }

    [Fact]
    public void Round_trip_keeps_every_stored_credential()
    {
        var back = RoundTrip(new Preferences
        {
            GitHubToken = "ghp_example",
            GitHubClientId = "Iv1.example",
            GoogleClientId = "example.apps.googleusercontent.com",
            GoogleClientSecret = "GOCSPX-example",
            GeminiApiKey = "key-example",
            AiEnabled = true,
        });

        Assert.Equal("ghp_example", back.GitHubToken);
        Assert.Equal("Iv1.example", back.GitHubClientId);
        Assert.Equal("example.apps.googleusercontent.com", back.GoogleClientId);
        Assert.Equal("GOCSPX-example", back.GoogleClientSecret);
        Assert.Equal("key-example", back.GeminiApiKey);
        Assert.True(back.AiEnabled);
    }

    [Fact]
    public void Round_trip_keeps_workspace_name_and_output_choices()
    {
        var back = RoundTrip(new Preferences
        {
            Initialised = true,
            UserName = "Jason",
            WorkspacePath = @"C:\repos",
            Verbosity = Verbosity.Detailed,
            Grouping = Grouping.Kind,
            Tone = Tone.Friendly,
            Format = OutputFormat.Text,
            Theme = AppTheme.Light,
        });

        Assert.True(back.Initialised);
        Assert.Equal("Jason", back.UserName);
        Assert.Equal(@"C:\repos", back.WorkspacePath);
        Assert.Equal(Verbosity.Detailed, back.Verbosity);
        Assert.Equal(Grouping.Kind, back.Grouping);
        Assert.Equal(Tone.Friendly, back.Tone);
        Assert.Equal(OutputFormat.Text, back.Format);
        Assert.Equal(AppTheme.Light, back.Theme);
    }

    [Fact]
    public void Carbon_is_the_default_theme()
        => Assert.Equal(AppTheme.Dark, new Preferences().Theme);

    [Fact]
    public void Ai_is_off_and_no_credentials_exist_by_default()
    {
        var fresh = new Preferences();

        Assert.False(fresh.AiEnabled);
        Assert.Null(fresh.GeminiApiKey);
        Assert.Null(fresh.GitHubToken);
        Assert.Null(fresh.GitHubClientId);
        Assert.Null(fresh.GoogleClientSecret);
    }

    [Fact]
    public void Unknown_fields_from_a_newer_build_do_not_break_loading()
    {
        const string json = """
            { "UserName": "Jason", "SomeFutureSetting": 42 }
            """;

        var back = JsonSerializer.Deserialize(json, PerformaJsonContext.Default.Preferences)!;

        Assert.Equal("Jason", back.UserName);
    }
}
