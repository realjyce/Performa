using System.Text.Json;
using Performa.Desktop.Services;
using Performa.Prefs;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// The shipped build has to work for someone who types nothing, which means the
/// credentials file beside the binary must be found. These tests write that file
/// into the test host's own base directory, which is the same location the
/// published exe reads from.
/// </summary>
public class CredentialStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(AppContext.BaseDirectory, "app-credentials.json");

    private void WriteFile(string? gitHub, string? gemini)
    {
        var doc = new Dictionary<string, string>();
        if (gitHub is not null) doc["github_client_id"] = gitHub;
        if (gemini is not null) doc["gemini_api_key"] = gemini;
        File.WriteAllText(_path, JsonSerializer.Serialize(doc));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_user_who_types_nothing_still_gets_the_shipped_credentials()
    {
        WriteFile("Ov23example", "shipped-key");
        var empty = new Preferences();

        Assert.Equal("Ov23example", AppCredentialStore.GitHubClientId(empty));
        Assert.Equal("shipped-key", AppCredentialStore.GeminiKey(empty));
    }

    [Fact]
    public void A_key_entered_in_settings_beats_the_shipped_one()
    {
        WriteFile("Ov23example", "shipped-key");
        var mine = new Preferences
        {
            GitHubClientId = "Ov23mine",
            GeminiApiKey = "my-key",
        };

        Assert.Equal("Ov23mine", AppCredentialStore.GitHubClientId(mine));
        Assert.Equal("my-key", AppCredentialStore.GeminiKey(mine));
    }

    [Fact]
    public void A_half_filled_file_resolves_what_it_has_and_nothing_more()
    {
        WriteFile("Ov23example", gemini: null);
        var empty = new Preferences();

        Assert.Equal("Ov23example", AppCredentialStore.GitHubClientId(empty));
        // Falls through to the AppData file on a real machine; either way the
        // point is that a missing entry never returns an empty string.
        Assert.True(AppCredentialStore.GeminiKey(empty) is null or { Length: > 0 });
    }

    [Fact]
    public void A_corrupt_file_does_not_throw()
    {
        File.WriteAllText(_path, "{ not json");
        var empty = new Preferences();

        var ex = Record.Exception(() => AppCredentialStore.GitHubClientId(empty));
        Assert.Null(ex);
    }
}
