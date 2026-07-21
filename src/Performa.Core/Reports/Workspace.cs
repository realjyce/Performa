using Performa.Git;

namespace Performa.Reports;

public sealed record RepoSnapshot(
    string Name,
    string Path,
    string Branch,
    IReadOnlyList<Commit> Recent,
    int UncommittedFiles,
    int UnpushedCommits);

public sealed record VelocityFacts(
    int ThisWeek,
    int LastWeek,
    int StreakDays,
    string? BusiestRepo);

public sealed record WorkspaceFacts(
    string Root,
    IReadOnlyList<RepoSnapshot> Repos,
    VelocityFacts Velocity);

public static class WorkspaceBuilder
{
    public static List<string> DiscoverRepos(string root)
    {
        if (!Directory.Exists(root)) return [];
        var repos = new List<string>();

        // The workspace folder itself may be a single repo.
        if (Directory.Exists(Path.Combine(root, ".git")))
            repos.Add(root);

        foreach (var dir in SafeSubdirs(root))
            if (Directory.Exists(Path.Combine(dir, ".git")))
                repos.Add(dir);

        return [.. repos.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> SafeSubdirs(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
    }

    /// <summary>
    /// Finds the best workspace when none is set: the common dev folder under
    /// the user profile holding the most git repositories.
    /// </summary>
    public static string? AutoDetectWorkspace()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            "IdeaProjects", "source/repos", "source", "repos", "Projects", "GitHub",
            "Documents/GitHub", "dev", "code", "src", "work", "Documents", home,
        ];

        string? best = null;
        var bestCount = 0;
        foreach (var rel in candidates)
        {
            var dir = rel == home ? home
                : Path.Combine(home, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;
            var count = DiscoverRepos(dir).Count;
            if (count > bestCount) { bestCount = count; best = dir; }
        }
        return best;
    }

    public static WorkspaceFacts Build(string root, DateTimeOffset now)
    {
        var since = now.Date.AddDays(-1);
        var weekAgo = now.AddDays(-7);
        var twoWeeksAgo = now.AddDays(-14);

        var snapshots = new List<RepoSnapshot>();
        var activeDays = new HashSet<DateOnly>();
        int thisWeek = 0, lastWeek = 0, busiestCount = 0;
        string? busiest = null;

        foreach (var dir in DiscoverRepos(root))
        {
            var repo = new RepoQueries(new GitRunner(dir));
            var branch = repo.Git.TryRun("rev-parse", "--abbrev-ref", "HEAD")?.Trim() ?? "?";
            var ahead = repo.Branches().FirstOrDefault(b => b.Name == branch)?.Ahead ?? 0;
            snapshots.Add(new RepoSnapshot(
                repo.RepoName,
                dir,
                branch,
                [.. repo.CommitsSince(since, onlyMine: true).Take(2)],
                repo.Working().Total,
                ahead));

            var dates = repo.CommitDatesSince(twoWeeksAgo, onlyMine: true);
            var week = dates.Count(d => d >= weekAgo);
            thisWeek += week;
            lastWeek += dates.Count(d => d < weekAgo);
            foreach (var d in dates)
                activeDays.Add(DateOnly.FromDateTime(d.LocalDateTime));
            if (week > busiestCount)
            {
                busiestCount = week;
                busiest = repo.RepoName;
            }
        }

        var streak = 0;
        var day = DateOnly.FromDateTime(now.LocalDateTime);
        if (!activeDays.Contains(day)) day = day.AddDays(-1);
        while (activeDays.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        var rootName = Path.GetFileName(Path.GetFullPath(root).TrimEnd('\\', '/'));
        return new WorkspaceFacts(
            rootName, snapshots, new VelocityFacts(thisWeek, lastWeek, streak, busiest));
    }
}
