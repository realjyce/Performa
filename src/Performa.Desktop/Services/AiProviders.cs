using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Performa.Desktop.Services;

/// <summary>
/// One question, one answer, and the name of whatever produced it. The model
/// name is carried so the UI can say which model spoke rather than crediting
/// "AI" generically.
/// </summary>
public sealed record AiAnswer(string Text, string Model);

/// <summary>
/// What every model vendor has to look like from the app's side.
///
/// Providers never throw. A failed call returns null and the deterministic
/// answer stands, which is what keeps the AI layer genuinely optional: pull
/// the key and Performa still works, it just stops writing prose.
/// </summary>
public interface IAiProvider
{
    /// <summary>Shown in Settings and used as the preference value.</summary>
    string Name { get; }

    /// <summary>Where the user gets a key, shown next to the field.</summary>
    string KeyUrl { get; }

    Task<AiAnswer?> AskAsync(string apiKey, string systemContext, string question);
}

/// <summary>
/// Shared plumbing. The prompt is identical across vendors so switching
/// provider changes who answers, not what was asked.
/// </summary>
public abstract class AiProviderBase : IAiProvider
{
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public abstract string Name { get; }
    public abstract string KeyUrl { get; }

    protected static string BuildPrompt(string systemContext, string question) =>
        $"{systemContext}\n\nQuestion: {question}\n\n" +
        "Answer in plain, concrete prose. Use only the facts given above; " +
        "if the facts do not cover it, say so rather than guessing. " +
        "Keep it under 120 words and never invent numbers, names or dates.";

    public async Task<AiAnswer?> AskAsync(string apiKey, string systemContext, string question)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        try
        {
            return await SendAsync(apiKey, BuildPrompt(systemContext, question));
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    protected abstract Task<AiAnswer?> SendAsync(string apiKey, string prompt);

    protected static async Task<JsonDocument?> PostJsonAsync(HttpRequestMessage req)
    {
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;
        await using var stream = await res.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

/// <summary>
/// Google Gemini. Free tier, which is why it stays the default.
/// </summary>
public sealed class GeminiProvider : AiProviderBase
{
    // Model choice matters more than it looks: the 2.x flash models are legacy
    // and carry zero free-tier allocation, so a perfectly valid key returns 429
    // limit:0 against them. These two are current and free-tier eligible. The
    // second is a named fallback because the "latest" alias returns 503 under
    // load often enough to be worth surviving.
    private static readonly string[] Models = ["gemini-flash-lite-latest", "gemini-3.1-flash-lite"];

    public override string Name => "Gemini";
    public override string KeyUrl => "https://aistudio.google.com/apikey";

    protected override async Task<AiAnswer?> SendAsync(string apiKey, string prompt)
    {
        foreach (var model in Models)
        {
            var payload = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.3, maxOutputTokens = 400 },
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
            { Content = content };
            req.Headers.Add("x-goog-api-key", apiKey);

            using var doc = await PostJsonAsync(req);
            var text = doc?.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()?.Trim();

            if (text is { Length: > 0 }) return new AiAnswer(text, model);
        }
        return null;
    }
}

/// <summary>
/// Anthropic Claude, spoken to over the Messages API directly rather than
/// through the SDK, so this provider carries the same zero dependencies as
/// its neighbours.
/// </summary>
public sealed class AnthropicProvider : AiProviderBase
{
    private const string Model = "claude-opus-4-8";
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    public override string Name => "Claude";
    public override string KeyUrl => "https://console.anthropic.com/settings/keys";

    protected override async Task<AiAnswer?> SendAsync(string apiKey, string prompt)
    {
        var payload = new
        {
            model = Model,
            max_tokens = 400,
            messages = new[] { new { role = "user", content = prompt } },
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var doc = await PostJsonAsync(req);
        if (doc is null) return null;

        // A safety decline comes back as a perfectly good 200 with no content,
        // so read stop_reason before touching the array.
        if (doc.RootElement.TryGetProperty("stop_reason", out var stop)
            && stop.GetString() == "refusal") return null;

        // content carries thinking blocks as well as text on some models; take
        // the first text block rather than assuming index zero is one.
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                && block.GetProperty("text").GetString()?.Trim() is { Length: > 0 } text)
                return new AiAnswer(text, Model);
        }
        return null;
    }
}

/// <summary>
/// OpenAI, over the Chat Completions API.
/// </summary>
public sealed class OpenAiProvider : AiProviderBase
{
    private const string Model = "gpt-4o-mini";
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    public override string Name => "OpenAI";
    public override string KeyUrl => "https://platform.openai.com/api-keys";

    protected override async Task<AiAnswer?> SendAsync(string apiKey, string prompt)
    {
        var payload = new
        {
            model = Model,
            max_completion_tokens = 400,
            temperature = 0.3,
            messages = new[] { new { role = "user", content = prompt } },
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        req.Headers.Add("Authorization", "Bearer " + apiKey);

        using var doc = await PostJsonAsync(req);
        var text = doc?.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim();

        return text is { Length: > 0 } ? new AiAnswer(text, Model) : null;
    }
}
