using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Performa.Desktop.Services;

public sealed record DeviceCodePrompt(string UserCode, string VerificationUri);

/// <summary>
/// GitHub sign-in via the Device Flow. Chosen deliberately over the web flow:
/// it needs no client secret at all, so a distributed build carries nothing
/// worth stealing. The user sees a short code, types it on github.com, and we
/// poll until it is approved.
/// </summary>
public sealed class GitHubAuthService
{
    private const string DeviceCodeEndpoint = "https://github.com/login/device/code";
    private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string Scopes = "repo read:user";

    private static readonly HttpClient Http = CreateClient();
    private readonly string _tokenPath;

    public GitHubAuthService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "performa");
        Directory.CreateDirectory(dir);
        _tokenPath = Path.Combine(dir, "github.json");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("User-Agent", "Performa");
        return client;
    }

    public string? LoadToken()
    {
        if (!File.Exists(_tokenPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_tokenPath));
            return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public bool IsSignedIn => LoadToken() is { Length: > 0 };

    public void SignOut()
    {
        try { File.Delete(_tokenPath); } catch (IOException) { }
    }

    /// <summary>Step one: ask GitHub for a code to show the user.</summary>
    public async Task<DeviceCodePrompt?> StartAsync(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = Scopes,
            });
            using var res = await Http.PostAsync(DeviceCodeEndpoint, content);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            _deviceCode = root.TryGetProperty("device_code", out var d) ? d.GetString() : null;
            _interval = root.TryGetProperty("interval", out var i) ? i.GetInt32() : 5;
            var userCode = root.TryGetProperty("user_code", out var u) ? u.GetString() : null;
            var uri = root.TryGetProperty("verification_uri", out var v)
                ? v.GetString() : "https://github.com/login/device";

            if (_deviceCode is null || userCode is null) return null;

            try
            {
                Process.Start(new ProcessStartInfo(uri!) { UseShellExecute = true });
            }
            catch (Exception) { /* the user can still open it by hand */ }

            return new DeviceCodePrompt(userCode, uri!);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    private string? _deviceCode;
    private int _interval = 5;

    /// <summary>Step two: poll until the user approves, or it expires.</summary>
    public async Task<string> CompleteAsync(string clientId)
    {
        if (_deviceCode is null) return "Start the sign-in first.";

        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(_interval));
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["device_code"] = _deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                });
                using var res = await Http.PostAsync(TokenEndpoint, content);
                await using var stream = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("access_token", out var tok)
                    && tok.GetString() is { Length: > 0 } token)
                {
                    File.WriteAllText(_tokenPath,
                        JsonSerializer.Serialize(new { access_token = token }));
                    _deviceCode = null;
                    return "Connected to GitHub.";
                }

                var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                switch (error)
                {
                    case "authorization_pending": continue;      // user hasn't finished yet
                    case "slow_down": _interval += 5; continue;
                    case "expired_token": return "That code expired. Try again.";
                    case "access_denied": return "Sign-in was declined.";
                    case null: continue;
                    default: return $"GitHub refused the sign-in ({error}).";
                }
            }
            catch (HttpRequestException) { return "Could not reach GitHub."; }
            catch (JsonException) { return "GitHub sent something unexpected."; }
        }
        return "Sign-in timed out.";
    }
}
