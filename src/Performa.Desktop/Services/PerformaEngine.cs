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
    public Preferences Prefs { get; private set; }

    public PerformaEngine()
    {
        Prefs = _store.LoadPrefs();
    }

    public string? WorkspacePath => Prefs.WorkspacePath;

    public void SetWorkspace(string path)
    {
        Prefs.WorkspacePath = path;
        Prefs.Initialised = true;
        _store.SavePrefs(Prefs);
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
