using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Performa.Desktop.Services;

/// <summary>
/// One email, compressed by structure rather than by summarising. Every
/// concrete item is pulled out into its own list and the untouched body is
/// carried alongside, so the card is short but nothing is discarded.
/// </summary>
public sealed record EmailDigest(
    string From,
    string Subject,
    DateTimeOffset? Received,
    IReadOnlyList<string> Dates,
    IReadOnlyList<string> Links,
    IReadOnlyList<string> Amounts,
    IReadOnlyList<string> Actions,
    string FullBody,
    string? Html);

public sealed partial class GmailService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<List<EmailDigest>> GetRecentAsync(string token, int max = 12)
    {
        var list = await GetJsonAsync(
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={max}&q=newer_than:3d",
            token);
        if (list is null) return [];
        using var _ = list;

        if (!list.RootElement.TryGetProperty("messages", out var msgs)) return [];

        var digests = new List<EmailDigest>();
        foreach (var m in msgs.EnumerateArray())
        {
            if (m.TryGetProperty("id", out var idEl) && idEl.GetString() is { } id
                && await GetMessageAsync(token, id) is { } digest)
                digests.Add(digest);
        }
        return digests;
    }

    private async Task<EmailDigest?> GetMessageAsync(string token, string id)
    {
        var doc = await GetJsonAsync(
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=full", token);
        if (doc is null) return null;
        using var _ = doc;

        var root = doc.RootElement;
        if (!root.TryGetProperty("payload", out var payload)) return null;

        string from = "", subject = "(no subject)";
        DateTimeOffset? received = null;

        if (payload.TryGetProperty("headers", out var headers))
        {
            foreach (var h in headers.EnumerateArray())
            {
                var name = h.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = h.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (name is null || value is null) continue;
                if (name.Equals("From", StringComparison.OrdinalIgnoreCase)) from = value;
                else if (name.Equals("Subject", StringComparison.OrdinalIgnoreCase)) subject = value;
                else if (name.Equals("Date", StringComparison.OrdinalIgnoreCase)
                    && DateTimeOffset.TryParse(value, out var d)) received = d;
            }
        }

        var body = ExtractBody(payload);
        var html = FindPart(payload, "text/html");
        return new EmailDigest(
            From: CleanSender(from),
            Subject: subject,
            Received: received,
            Dates: Distinct(DateRegex().Matches(body).Select(m => m.Value)),
            Links: Distinct(LinkRegex().Matches(body).Select(m => m.Value.TrimEnd('.', ',', ')'))),
            Amounts: Distinct(AmountRegex().Matches(body).Select(m => m.Value)),
            Actions: ExtractActions(body),
            FullBody: body.Trim(),
            Html: html);
    }

    private static string CleanSender(string from)
    {
        var angle = from.IndexOf('<');
        return angle > 0 ? from[..angle].Trim().Trim('"') : from.Trim();
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> items)
        => [.. items.Select(i => i.Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)];

    /// <summary>Sentences that ask for something. Kept whole, never paraphrased.</summary>
    private static IReadOnlyList<string> ExtractActions(string body)
    {
        string[] cues =
        [
            "please", "could you", "can you", "need", "required", "deadline", "due",
            "by ", "confirm", "review", "sign", "rsvp", "reply", "submit", "action",
        ];

        var sentences = SentenceRegex().Split(body);
        var hits = new List<string>();
        foreach (var raw in sentences)
        {
            var s = WhitespaceRegex().Replace(raw, " ").Trim();
            if (s.Length is < 12 or > 240) continue;
            if (cues.Any(c => s.Contains(c, StringComparison.OrdinalIgnoreCase)))
                hits.Add(s);
            if (hits.Count == 6) break;
        }
        return hits;
    }

    private static string ExtractBody(JsonElement payload)
    {
        var plain = FindPart(payload, "text/plain");
        if (plain is { Length: > 0 }) return plain;

        var html = FindPart(payload, "text/html");
        if (html is { Length: > 0 }) return WhitespaceRegex().Replace(HtmlTagRegex().Replace(html, " "), " ");

        return "";
    }

    private static string? FindPart(JsonElement node, string mime)
    {
        if (node.TryGetProperty("mimeType", out var mt)
            && mt.GetString() == mime
            && node.TryGetProperty("body", out var body)
            && body.TryGetProperty("data", out var data)
            && data.GetString() is { } encoded)
            return DecodeBase64Url(encoded);

        if (node.TryGetProperty("parts", out var parts))
            foreach (var part in parts.EnumerateArray())
                if (FindPart(part, mime) is { Length: > 0 } found)
                    return found;

        return null;
    }

    private static string DecodeBase64Url(string data)
    {
        try
        {
            var s = data.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch (FormatException) { return ""; }
    }

    private static async Task<JsonDocument?> GetJsonAsync(string url, string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            await using var stream = await res.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    [GeneratedRegex(@"https?://[^\s<>""]+")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\b(\d{4}-\d{2}-\d{2}|\d{1,2}[/.]\d{1,2}[/.]\d{2,4}|(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)[a-z]*,?\s+\d{1,2}(?:st|nd|rd|th)?|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2}|\d{1,2}:\d{2}\s?(?:am|pm|AM|PM)?|\d{1,2}\s?(?:am|pm|AM|PM))\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?:[$£€¥]\s?\d[\d,]*(?:\.\d{2})?|\b\d[\d,]*(?:\.\d{2})?\s?(?:USD|EUR|GBP|KRW|IDR|JPY)\b)")]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"(?<=[.!?\n])\s+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
