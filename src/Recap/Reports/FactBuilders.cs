using Recap.Git;
using Recap.Prefs;

namespace Recap.Reports;

public static class FactBuilders
{
    public static StandupFacts BuildStandup(
        RepoQueries repo, Preferences prefs, DateTimeOffset since, DateTimeOffset now)
    {
        var commits = repo.CommitsSince(since, onlyMine: true);
        var branches = repo.Branches();
        var current = repo.CurrentBranch();
        var unpushed = branches.FirstOrDefault(b => b.Name == current)?.Ahead ?? 0;
        return new StandupFacts(
            repo.RepoName,
            since,
            now,
            GroupCommits(commits, prefs.Grouping),
            unpushed,
            repo.Working().Total);
    }

    public static ChangelogFacts BuildChangelog(
        RepoQueries repo, string? from, string to)
    {
        var resolvedFrom = from ?? repo.LastTag();
        var range = resolvedFrom is null
            ? to
            : $"{resolvedFrom}..{to}";
        var commits = repo.CommitsInRange(range);

        var heading = to != "HEAD"
            ? to
            : $"Unreleased ({DateTime.Now:yyyy-MM-dd})";

        var order = new[]
        {
            ChangeKind.Feature, ChangeKind.Fix, ChangeKind.Refactor,
            ChangeKind.Docs, ChangeKind.Chore, ChangeKind.Other,
        };
        var byKind = commits.GroupBy(c => Classification.Classify(c.Subject))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Commit>)[.. g]);
        var sections = order
            .Where(byKind.ContainsKey)
            .Select(k => (Classification.SectionTitle(k), byKind[k]))
            .ToList();

        return new ChangelogFacts(heading, resolvedFrom ?? "start", to, sections);
    }

    public static SummaryFacts BuildSummary(
        RepoQueries repo, Preferences prefs, string target)
    {
        string range;
        string baseRef;
        if (target.Contains(".."))
        {
            range = target;
            baseRef = target[..target.IndexOf("..", StringComparison.Ordinal)];
        }
        else
        {
            baseRef = repo.DefaultBranch();
            range = baseRef == target ? target : $"{baseRef}..{target}";
        }

        var commits = repo.CommitsInRange(range);
        var reasons = commits
            .Select(c => c.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(lines => lines.Length > 0)
            .Select(lines => lines[0])
            .Where(line => line.Length > 10)
            .Distinct()
            .Take(5)
            .ToList();

        var (files, ins, del) = repo.DiffStat(range);
        return new SummaryFacts(
            target, baseRef, GroupCommits(commits, prefs.Grouping), reasons, files, ins, del);
    }

    public static LooseEndsFacts BuildLooseEnds(RepoQueries repo, int staleDays = 21)
    {
        var branches = repo.Branches();
        var defaultBranch = repo.DefaultBranch();
        var cutoff = DateTimeOffset.Now.AddDays(-staleDays);

        var stale = branches
            .Where(b => b.Name != defaultBranch && b.LastCommit < cutoff)
            .OrderBy(b => b.LastCommit)
            .ToList();
        var unpushed = branches
            .Where(b => b.Ahead > 0 || b.Upstream is null)
            .Where(b => b.Upstream is null ? b.Name != defaultBranch : true)
            .ToList();

        var (todos, todoTotal) = repo.Todos(limit: 8);
        return new LooseEndsFacts(stale, unpushed, repo.Working(), todos, todoTotal);
    }

    public static IReadOnlyList<ReportGroup> GroupCommits(List<Commit> commits, Grouping grouping)
    {
        if (commits.Count == 0) return [];
        return grouping switch
        {
            Grouping.Flat => [new ReportGroup("all", commits)],
            Grouping.Kind => [.. commits
                .GroupBy(c => Classification.Classify(c.Subject))
                .OrderBy(g => g.Key)
                .Select(g => new ReportGroup(Classification.SectionTitle(g.Key), [.. g]))],
            _ => [.. commits
                .GroupBy(Classification.AreaOf)
                .OrderByDescending(g => g.Count())
                .Select(g => new ReportGroup(g.Key, [.. g]))],
        };
    }
}
