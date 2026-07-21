using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Performa.Desktop.Services;

public sealed record GoogleTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool NeedsRefresh => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-2);
}

/// <summary>
/// Google sign-in for an installed app: loopback redirect + PKCE, the flow
/// Google recommends for desktop. Read-only scopes only. Tokens live in
/// %APPDATA%/performa/google.json; the client secret stays in preferences and
/// never leaves this machine.
/// </summary>
public sealed class GoogleAuthService
{
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar.readonly";
    public const string GmailScope = "https://www.googleapis.com/auth/gmail.readonly";

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _tokenPath;

    public GoogleAuthService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "performa");
        Directory.CreateDirectory(dir);
        _tokenPath = Path.Combine(dir, "google.json");
    }

    public bool IsSignedIn => LoadTokens() is not null;

    public GoogleTokens? LoadTokens()
    {
        if (!File.Exists(_tokenPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<GoogleTokens>(File.ReadAllText(_tokenPath));
        }
        catch (JsonException) { return null; }
    }

    private void SaveTokens(GoogleTokens tokens)
        => File.WriteAllText(_tokenPath,
            JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true }));

    public void SignOut()
    {
        try { File.Delete(_tokenPath); } catch (IOException) { }
    }

    /// <summary>Runs the browser sign-in. Returns a human-readable result.</summary>
    public async Task<string> SignInAsync(string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return "Enter your Google Client ID and Secret first.";

        var verifier = RandomUrlSafe(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirect = $"http://127.0.0.1:{port}/";

        var url =
            $"{AuthEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString($"{CalendarScope} {GmailScope}")}" +
            $"&code_challenge={challenge}&code_challenge_method=S256" +
            "&access_type=offline&prompt=consent";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            listener.Stop();
            return "Could not open your browser.";
        }

        string? code;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            if (await Task.WhenAny(accept, Task.Delay(TimeSpan.FromMinutes(3))) != accept)
                return "Sign-in timed out. Try again.";

            using var client = await accept;
            code = await ReadCodeAndRespondAsync(client);
        }
        finally
        {
            listener.Stop();
        }

        if (code is null) return "Google did not return an authorisation code.";

        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };

        var tokens = await ExchangeAsync(form, keepRefresh: null);
        if (tokens is null) return "Google rejected the exchange. Check the Client ID and Secret.";

        SaveTokens(tokens);
        return "Connected to Google.";
    }

    /// <summary>Valid access token, refreshing when needed. Null if not signed in.</summary>
    public async Task<string?> GetAccessTokenAsync(string clientId, string clientSecret)
    {
        var tokens = LoadTokens();
        if (tokens is null) return null;
        if (!tokens.NeedsRefresh) return tokens.AccessToken;
        if (tokens.RefreshToken is not { Length: > 0 } refresh) return null;

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refresh,
            ["grant_type"] = "refresh_token",
        };

        var refreshed = await ExchangeAsync(form, keepRefresh: refresh);
        if (refreshed is null) return null;
        SaveTokens(refreshed);
        return refreshed.AccessToken;
    }

    private static async Task<GoogleTokens?> ExchangeAsync(
        Dictionary<string, string> form, string? keepRefresh)
    {
        try
        {
            using var res = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form));
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (access is null) return null;

            var refresh = root.TryGetProperty("refresh_token", out var r)
                ? r.GetString() : keepRefresh;
            var seconds = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

            return new GoogleTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(seconds));
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    private static async Task<string?> ReadCodeAndRespondAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer);
        var request = Encoding.UTF8.GetString(buffer, 0, read);

        string? code = null;
        var firstLine = request.Split('\n')[0];
        var parts = firstLine.Split(' ');
        if (parts.Length > 1 && parts[1].IndexOf('?') is var q && q >= 0)
        {
            foreach (var pair in parts[1][(q + 1)..].Split('&'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "code") code = Uri.UnescapeDataString(kv[1]);
            }
        }

        var body = Encoding.UTF8.GetBytes(code is null
            ? "<html><body style='font-family:sans-serif;background:#1C1E22;color:#ECEDF0;display:grid;place-items:center;height:100vh'><h2>Sign-in failed. You can close this tab.</h2></body></html>"
            : "<html><body style='font-family:sans-serif;background:#1C1E22;color:#ECEDF0;display:grid;place-items:center;height:100vh'><div style='text-align:center'><h2>Performa is connected.</h2><p style='color:#9A9DA8'>You can close this tab and return to the app.</p></div></body></html>");

        var header = Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(header);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
        return code;
    }

    private static string RandomUrlSafe(int bytes)
        => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
