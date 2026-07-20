using System.Globalization;

namespace Recap.Git;

public sealed class RepoQueries(GitRunner git)
{
    public GitRunner Git { get; } = git;

    public string RepoName => Path.GetFileName(Git.RepoPath.TrimEnd('\\', '/'));

    public string? UserEmail() => Git.TryRun("config", "user.email")?.Trim();

    public List<Commit> Log(IEnumerable<string> extraArgs)
    {
        var args = new List<string>
        {
            "log", "--no-merges", $"--pretty=format:{GitLogParser.LogFormat}", "--name-only",
        };
        args.AddRange(extraArgs);
        var raw = Git.TryRun([.. args]);
        return raw is null ? [] : GitLogParser.Parse(raw);
    }

    public List<Commit> CommitsSince(DateTimeOffset since, bool onlyMine)
    {
        var args = new List<string> { $"--since={since:O}" };
        if (onlyMine && UserEmail() is { Length: > 0 } email)
            args.Add($"--author={email}");
        return Log(args);
    }

    public List<Commit> CommitsInRange(string range) => Log([range]);

    public string? LastTag()
        => Git.TryRun("describe", "--tags", "--abbrev=0")?.Trim();

    public string FirstCommit()
        => Git.Run("rev-list", "--max-parents=0", "HEAD").Trim().Split('\n')[0];

    public string CurrentBranch()
        => Git.Run("rev-parse", "--abbrev-ref", "HEAD").Trim();

    public string DefaultBranch()
    {
        var head = Git.TryRun("symbolic-ref", "refs/remotes/origin/HEAD")?.Trim();
        if (head is not null)
            return head["refs/remotes/origin/".Length..];
        foreach (var candidate in new[] { "main", "master" })
            if (Git.TryRun("rev-parse", "--verify", candidate) is not null)
                return candidate;
        return CurrentBranch();
    }

    public List<BranchInfo> Branches()
    {
        var raw = Git.TryRun(
            "for-each-ref", "refs/heads",
            "--format=%(refname:short)\x1f%(committerdate:iso8601-strict)\x1f%(upstream:short)\x1f%(upstream:track)");
        if (raw is null) return [];

        var branches = new List<BranchInfo>();
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\x1f');
            if (f.Length < 4) continue;
            var ahead = 0;
            var track = f[3];
            var aheadIdx = track.IndexOf("ahead ", StringComparison.Ordinal);
            if (aheadIdx >= 0)
            {
                var rest = track[(aheadIdx + 6)..];
                var end = rest.IndexOfAny([',', ']']);
                if (end > 0 && int.TryParse(rest[..end], out var n)) ahead = n;
            }
            branches.Add(new BranchInfo(
                Name: f[0],
                LastCommit: DateTimeOffset.Parse(f[1], CultureInfo.InvariantCulture),
                Upstream: f[2].Length > 0 ? f[2] : null,
                Ahead: ahead));
        }
        return branches;
    }

    public WorkingState Working()
    {
        var raw = Git.TryRun("status", "--porcelain") ?? "";
        int staged = 0, unstaged = 0, untracked = 0;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("??")) { untracked++; continue; }
            if (line.Length < 2) continue;
            if (line[0] is not ' ' and not '?') staged++;
            if (line[1] is not ' ' and not '?') unstaged++;
        }
        return new WorkingState(staged, unstaged, untracked);
    }

    public (List<TodoMarker> Markers, int Total) Todos(int limit)
    {
        var raw = Git.TryRun("grep", "-nIE", @"(TODO|FIXME|HACK)[: (]", "--", ".") ?? "";
        var markers = new List<TodoMarker>();
        var total = 0;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 3);
            if (parts.Length < 3) continue;
            total++;
            if (markers.Count >= limit) continue;
            var text = parts[2].Trim();
            var kind = text.Contains("FIXME") ? "FIXME" : text.Contains("HACK") ? "HACK" : "TODO";
            markers.Add(new TodoMarker(
                parts[0],
                int.TryParse(parts[1], out var n) ? n : 0,
                kind,
                text.Length > 100 ? text[..100] + "…" : text));
        }
        return (markers, total);
    }

    public (int Files, int Insertions, int Deletions) DiffStat(string range)
    {
        var raw = Git.TryRun("diff", "--shortstat", range) ?? "";
        int files = 0, ins = 0, del = 0;
        foreach (var part in raw.Split(','))
        {
            var p = part.Trim();
            var num = int.TryParse(p.Split(' ')[0], out var n) ? n : 0;
            if (p.Contains("file")) files = num;
            else if (p.Contains("insertion")) ins = num;
            else if (p.Contains("deletion")) del = num;
        }
        return (files, ins, del);
    }
}
