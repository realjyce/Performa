using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Performa.Desktop.Services;

public sealed record RemoteInfo(int Stars, int OpenIssues, string? Language, DateTimeOffset? PushedAt);

public sealed record RemoteRepo(
    string Name,
    string FullName,
    bool IsPrivate,
    string? Language,
    string CloneUrl,
    DateTimeOffset? PushedAt);

/// <summary>An open PR or issue that involves the signed-in user.</summary>
public sealed record WorkItem(
    string Repo,
    int Number,
    string Title,
    string Url,
    bool IsPull,
    DateTimeOffset? Updated);

/// <summary>
/// Opt-in GitHub remote data. Lives in the desktop layer only; Performa.Core
/// stays network-free. Works unauthenticated for public repos (60 req/hr);
/// a user-supplied token raises the limit and reaches private repos.
/// </summary>
public sealed class GitHubService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Performa", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<RemoteInfo?> GetRepoAsync(string owner, string name, string? token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{name}");
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            DateTimeOffset? pushed = root.TryGetProperty("pushed_at", out var p)
                && p.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(p.GetString(), out var dt) ? dt : null;

            return new RemoteInfo(
                Stars: root.TryGetProperty("stargazers_count", out var s) ? s.GetInt32() : 0,
                OpenIssues: root.TryGetProperty("open_issues_count", out var o) ? o.GetInt32() : 0,
                Language: root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String
                    ? l.GetString() : null,
                PushedAt: pushed);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Lists the repositories the token can see, including private ones.
    /// Returns null when the token is missing or rejected.
    /// </summary>
    public async Task<List<RemoteRepo>?> GetUserReposAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var repos = new List<RemoteRepo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                DateTimeOffset? pushed = item.TryGetProperty("pushed_at", out var p)
                    && p.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(p.GetString(), out var dt) ? dt : null;

                repos.Add(new RemoteRepo(
                    Name: item.GetProperty("name").GetString() ?? "",
                    FullName: item.TryGetProperty("full_name", out var f) ? f.GetString() ?? "" : "",
                    IsPrivate: item.TryGetProperty("private", out var pr) && pr.GetBoolean(),
                    Language: item.TryGetProperty("language", out var l)
                        && l.ValueKind == JsonValueKind.String ? l.GetString() : null,
                    CloneUrl: item.TryGetProperty("clone_url", out var c) ? c.GetString() ?? "" : "",
                    PushedAt: pushed));
            }
            return repos;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    /// <summary>
    /// Open PRs and issues that involve the signed-in user, newest activity
    /// first. "involves" is GitHub's own union of authored, assigned,
    /// mentioned and commented, which matches what a person means by "mine".
    /// </summary>
    public async Task<List<WorkItem>?> GetOpenWorkAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/search/issues?q=is:open+involves:@me&sort=updated&per_page=40");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return null;

            var work = new List<WorkItem>();
            foreach (var item in items.EnumerateArray())
            {
                // repository_url ends with /repos/{owner}/{name}
                var repoUrl = item.TryGetProperty("repository_url", out var r)
                    ? r.GetString() ?? "" : "";
                var slash = repoUrl.LastIndexOf('/');
                var owner = repoUrl[..slash].LastIndexOf('/') is var o and >= 0
                    ? repoUrl[(o + 1)..slash] : "";
                var repo = slash >= 0 ? $"{owner}/{repoUrl[(slash + 1)..]}" : "";

                DateTimeOffset? updated = item.TryGetProperty("updated_at", out var u)
                    && u.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(u.GetString(), out var dt) ? dt : null;

                work.Add(new WorkItem(
                    Repo: repo,
                    Number: item.TryGetProperty("number", out var n) ? n.GetInt32() : 0,
                    Title: item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Url: item.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                    IsPull: item.TryGetProperty("pull_request", out _),
                    Updated: updated));
            }
            return work;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }
}
