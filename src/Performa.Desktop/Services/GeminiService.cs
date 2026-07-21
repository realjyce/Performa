using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Performa.Desktop.Services;

/// <summary>
/// Optional AI layer behind the same seam the deterministic enricher uses.
/// Strictly opt-in: nothing is sent anywhere unless AiEnabled is true and a key
/// exists. Every call degrades to null on failure so the deterministic output
/// remains the source of truth.
/// </summary>
public sealed class GeminiService
{
    // Model choice matters more than it looks: the 2.x flash models are legacy
    // and carry zero free-tier allocation, so a perfectly valid key returns 429
    // limit:0 against them. These two are current and free-tier eligible. The
    // second is a named fallback because the "latest" alias returns 503 under
    // load often enough to be worth surviving.
    private static readonly string[] Models = ["gemini-flash-lite-latest", "gemini-3.1-flash-lite"];

    private static string Endpoint(string model)
        => $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string?> AskAsync(string apiKey, string systemContext, string question)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        foreach (var model in Models)
        {
            var answer = await TryAskAsync(model, apiKey, systemContext, question);
            if (answer is { Length: > 0 }) return answer;
        }
        return null;
    }

    private static async Task<string?> TryAskAsync(
        string model, string apiKey, string systemContext, string question)
    {

        var prompt =
            $"{systemContext}\n\nQuestion: {question}\n\n" +
            "Answer in plain, concrete prose. Use only the facts given above; " +
            "if the facts do not cover it, say so rather than guessing. " +
            "Keep it under 120 words and never invent numbers, names or dates.";

        var payload = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } },
            },
            generationConfig = new { temperature = 0.3, maxOutputTokens = 400 },
        };

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint(model)) { Content = content };
            req.Headers.Add("x-goog-api-key", apiKey);

            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()?.Trim();
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>
    /// A short prose read of one email. The structured extraction stays on the
    /// card either way, so this only ever adds; it never replaces the facts.
    /// </summary>
    public Task<string?> SummariseEmailAsync(string apiKey, string from, string subject, string body)
    {
        var trimmed = body.Length > 6000 ? body[..6000] : body;
        return AskAsync(apiKey,
            $"Email from {from}, subject \"{subject}\":\n{trimmed}",
            "In two sentences, what is this asking of me and by when? If nothing is asked, say what it is telling me.");
    }
}
