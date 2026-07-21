using System.Text.Json;
using Performa.Prefs;

namespace Performa.Desktop.Services;

public sealed record GoogleCredentials(string ClientId, string ClientSecret);

/// <summary>
/// Resolves the OAuth client the app signs in with. Users never need to see
/// these: a shipped build carries its own credentials next to the binary, so
/// the end user only ever clicks "Sign in with Google". The prefs override
/// exists for developers pointing the app at their own Google project.
///
/// For installed apps Google treats the client secret as non-secret; PKCE is
/// what actually secures the flow. The credentials file is gitignored so it
/// never reaches the public repository.
/// </summary>
public static class GoogleCredentialStore
{
    private const string FileName = "google-credentials.json";

    public static GoogleCredentials? Load(Preferences prefs)
    {
        // 1. Developer override in preferences.
        if (prefs.GoogleClientId is { Length: > 0 } id
            && prefs.GoogleClientSecret is { Length: > 0 } secret)
            return new GoogleCredentials(id, secret);

        // 2. Shipped alongside the binary (the product path).
        var bundled = FromFile(Path.Combine(AppContext.BaseDirectory, FileName));
        if (bundled is not null) return bundled;

        // 3. This machine only, outside the repository.
        return FromFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "performa", FileName));
    }

    public static bool HasBuiltIn()
        => FromFile(Path.Combine(AppContext.BaseDirectory, FileName)) is not null
           || FromFile(Path.Combine(
               Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
               "performa", FileName)) is not null;

    private static GoogleCredentials? FromFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // Accept both a flat file and Google's own downloaded client_secret json.
            if (root.TryGetProperty("installed", out var installed)) root = installed;

            var id = root.TryGetProperty("client_id", out var i) ? i.GetString() : null;
            var secret = root.TryGetProperty("client_secret", out var s) ? s.GetString() : null;
            return id is { Length: > 0 } && secret is { Length: > 0 }
                ? new GoogleCredentials(id, secret)
                : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}
