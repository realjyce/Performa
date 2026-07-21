using System.Text.Json;
using Performa.Prefs;

namespace Performa.Desktop.Services;

/// <summary>
/// The credentials a shipped build carries so the end user never types one.
/// Same three-step resolution as <see cref="GoogleCredentialStore"/>: a prefs
/// override for developers, a file beside the binary for the product, then this
/// machine's AppData. The file is gitignored, so nothing reaches the public repo.
///
/// The GitHub client id is safe to ship: the device flow is designed around a
/// public client id and no secret. The Gemini key is a different matter and is
/// documented at <see cref="GeminiKey"/>.
/// </summary>
public static class AppCredentialStore
{
    private const string FileName = "app-credentials.json";

    /// <summary>OAuth App client id for the GitHub device flow. Public by design.</summary>
    public static string? GitHubClientId(Preferences prefs)
        => prefs.GitHubClientId is { Length: > 0 } id ? id : Read("github_client_id");

    /// <summary>
    /// Shared Gemini key for a build that must work without setup.
    ///
    /// Unlike the GitHub client id this is a real secret, and anyone with the
    /// binary can extract it and spend the quota it belongs to. It is here
    /// because a test build has to run with no setup, not because it is safe.
    /// A key entered in Settings always wins, and the durable fix is a small
    /// server that holds the key and proxies the call.
    /// </summary>
    public static string? GeminiKey(Preferences prefs)
        => prefs.GeminiApiKey is { Length: > 0 } key ? key : Read("gemini_api_key");

    private static string? Read(string property)
        => FromFile(Path.Combine(AppContext.BaseDirectory, FileName), property)
           ?? FromFile(Path.Combine(
               Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
               "performa", FileName), property);

    private static string? FromFile(string path, string property)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty(property, out var v)
                && v.GetString() is { Length: > 0 } s ? s : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}
