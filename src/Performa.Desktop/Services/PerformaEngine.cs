using Performa.Enrich;
using Performa.Git;
using Performa.Prefs;
using Performa.Reports;

namespace Performa.Desktop.Services;

/// <summary>
/// Desktop-side facade over Performa.Core. Wraps the same fact builders and
/// enricher the CLI uses, and keeps preferences loaded. All git work is
/// synchronous inside; callers run it off the UI thread with Task.Run.
/// </summary>
public sealed class PerformaEngine
{
    private readonly PrefsStore _store = new();
    private readonly GitHubAuthService _gitHubAuth = new();
    public Preferences Prefs { get; private set; }

    /// <summary>
    /// The token to talk to GitHub with. A device-flow sign-in wins; a pasted
    /// personal token is the fallback so the app still works without one.
    /// </summary>
    public string? GitHubAccessToken
        => _gitHubAuth.LoadToken()
           ?? (string.IsNullOrWhiteSpace(Prefs.GitHubToken) ? null : Prefs.GitHubToken.Trim());

    public bool GitHubSignedIn => _gitHubAuth.IsSignedIn;

    /// <summary>Raised when GitHub sign-in succeeds so pages can pull remote data.</summary>
    public event Action? GitHubSignedInChanged;

    public void NotifyGitHubChanged() => GitHubSignedInChanged?.Invoke();

    public PerformaEngine()
    {
        Prefs = _store.LoadPrefs();
        EnsureWorkspace();
    }

    /// <summary>Auto-picks a workspace when none is set or the set one holds no repos.</summary>
    private void EnsureWorkspace()
    {
        var ws = Prefs.WorkspacePath;
        var valid = ws is { Length: > 0 } && Directory.Exists(ws)
            && WorkspaceBuilder.DiscoverRepos(ws).Count > 0;
        if (valid) return;

        var detected = WorkspaceBuilder.AutoDetectWorkspace();
        if (detected is null) return;
        Prefs.WorkspacePath = detected;
        Prefs.Initialised = true;
        _store.SavePrefs(Prefs);
    }

    /// <summary>Re-detects the best workspace and applies it. True if one was found.</summary>
    public bool AutoDetect()
    {
        var detected = WorkspaceBuilder.AutoDetectWorkspace();
        if (detected is null) return false;
        SetWorkspace(detected);
        return true;
    }

    /// <summary>Re-scans the current workspace and reloads every page.</summary>
    public void Rescan() => WorkspaceChanged?.Invoke();

    public IReadOnlySet<string> LocalRepoNames()
        => DiscoverRepos()
            .Select(p => System.IO.Path.GetFileName(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clones a repo into the workspace. Uses git's own credential helper for
    /// private repos so no token is ever written into .git/config.
    /// </summary>
    public string Clone(string cloneUrl, string name)
    {
        var ws = Prefs.WorkspacePath;
        if (ws is not { Length: > 0 } || !Directory.Exists(ws))
            return "Set a workspace folder first.";

        var target = System.IO.Path.Combine(ws, name);
        if (Directory.Exists(target)) return "Already on disk.";

        try
        {
            new GitRunner(ws).Run("clone", cloneUrl, target);
            WorkspaceChanged?.Invoke();
            return "Cloned.";
        }
        catch (GitException e)
        {
            return e.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                ? "Clone failed: git could not authenticate. Sign in via Git Credential Manager."
                : "Clone failed: " + e.Message;
        }
    }

    public string? WorkspacePath => Prefs.WorkspacePath;

    /// <summary>Raised when the workspace folder changes so pages can reload.</summary>
    public event Action? WorkspaceChanged;

    /// <summary>Raised the moment Google sign-in succeeds so pages can pull data.</summary>
    public event Action? GoogleSignedIn;

    public void NotifyGoogleSignedIn() => GoogleSignedIn?.Invoke();

    public void SetWorkspace(string path)
    {
        var changed = !string.Equals(Prefs.WorkspacePath, path, StringComparison.OrdinalIgnoreCase);
        Prefs.WorkspacePath = path;
        Prefs.Initialised = true;
        _store.SavePrefs(Prefs);
        if (changed) WorkspaceChanged?.Invoke();
    }

    public void SavePrefs() => _store.SavePrefs(Prefs);

    public WorkspaceFacts BuildWorkspace()
        => WorkspaceBuilder.Build(Prefs.WorkspacePath!, DateTimeOffset.Now);

    public IReadOnlyList<string> DiscoverRepos()
        => Prefs.WorkspacePath is { Length: > 0 } ws && Directory.Exists(ws)
            ? WorkspaceBuilder.DiscoverRepos(ws)
            : [];

    public RepoQueries Repo(string path) => new(new GitRunner(path));

    public string RenderStandup(string repoPath)
    {
        var repo = Repo(repoPath);
        var state = _store.LoadState();
        var since = state.LastStandup.TryGetValue(repo.Git.RepoPath, out var last)
            ? last
            : DateTimeOffset.Now.Date.AddDays(-1);
        var facts = FactBuilders.BuildStandup(repo, Prefs, since, DateTimeOffset.Now);
        return new DeterministicEnricher(pretty: false).RenderStandup(facts, Prefs);
    }

    public string RenderChangelog(string repoPath)
    {
        var repo = Repo(repoPath);
        var facts = FactBuilders.BuildChangelog(repo, from: null, to: "HEAD");
        return new DeterministicEnricher(pretty: false).RenderChangelog(facts, Prefs);
    }

    public string RenderSummary(string repoPath, string target)
    {
        var repo = Repo(repoPath);
        var facts = FactBuilders.BuildSummary(repo, Prefs, target);
        return new DeterministicEnricher(pretty: false).RenderSummary(facts, Prefs);
    }

    public LooseEndsFacts BuildLooseEnds(string repoPath)
        => FactBuilders.BuildLooseEnds(Repo(repoPath));

    public string CurrentBranch(string repoPath)
        => Repo(repoPath).Git.TryRun("rev-parse", "--abbrev-ref", "HEAD")?.Trim() ?? "?";

    /// <summary>Parses owner/repo from the origin remote, or null if not a GitHub repo.</summary>
    public (string Owner, string Name)? RemoteSlug(string repoPath)
    {
        var url = Repo(repoPath).Git.TryRun("remote", "get-url", "origin")?.Trim();
        if (url is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            url, @"github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?/?$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    public List<(string Repo, DateTimeOffset When, string Subject)> TodayCommits()
    {
        var midnight = new DateTimeOffset(DateTimeOffset.Now.Date, DateTimeOffset.Now.Offset);
        var result = new List<(string, DateTimeOffset, string)>();
        foreach (var path in DiscoverRepos())
        {
            var repo = Repo(path);
            foreach (var c in repo.CommitsSince(midnight, onlyMine: true))
                result.Add((repo.RepoName, c.Date, Classification.CleanSubject(c.Subject)));
        }
        return [.. result.OrderByDescending(x => x.Item2)];
    }
}
