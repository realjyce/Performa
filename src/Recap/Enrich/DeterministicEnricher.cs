using System.Text;
using Recap.Prefs;
using Recap.Reports;

namespace Recap.Enrich;

public sealed class DeterministicEnricher : IEnricher
{
    public string RenderStandup(StandupFacts f, Preferences prefs)
    {
        var sb = new StringBuilder();
        var heading = prefs.Tone == Tone.Friendly
            ? $"Standup for {f.Now:ddd d MMM} (here's what you shipped since {f.Since:ddd d MMM})"
            : $"Standup - {f.Now:ddd d MMM} (since {f.Since:ddd d MMM})";
        H1(sb, heading, prefs);

        if (f.Groups.Count == 0)
        {
            sb.AppendLine(prefs.Tone == Tone.Friendly
                ? "No commits in this window. Quiet days count too."
                : "No commits in this window.");
        }

        foreach (var group in f.Groups)
        {
            if (f.Groups.Count > 1 || group.Title != "all")
                H2(sb, Capitalize(group.Title), prefs);
            RenderCommitBullets(sb, group.Commits, prefs);
        }

        var loose = new List<string>();
        if (f.UnpushedCommits > 0) loose.Add($"{f.UnpushedCommits} unpushed commit(s)");
        if (f.UncommittedFiles > 0) loose.Add($"{f.UncommittedFiles} uncommitted file(s)");
        if (loose.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Loose ends: {string.Join(", ", loose)}");
        }
        else if (prefs.Tone == Tone.Friendly && f.Groups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Everything committed and pushed. Nice.");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public string RenderChangelog(ChangelogFacts f, Preferences prefs)
    {
        var sb = new StringBuilder();
        H1(sb, f.Heading, prefs);
        if (f.Sections.Count == 0)
            sb.AppendLine($"No commits between {f.FromRef} and {f.ToRef}.");

        foreach (var (section, commits) in f.Sections)
        {
            H2(sb, section, prefs);
            foreach (var c in commits)
            {
                var line = $"- {Classification.CleanSubject(c.Subject)}";
                if (prefs.Verbosity == Verbosity.Detailed)
                    line += $" ({c.Sha[..7]})";
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public string RenderSummary(SummaryFacts f, Preferences prefs)
    {
        var sb = new StringBuilder();
        H1(sb, $"What changed on {f.Target} (vs {f.BaseRef})", prefs);
        sb.AppendLine($"{f.Files} file(s) changed, +{f.Insertions}/-{f.Deletions}");
        sb.AppendLine();

        foreach (var group in f.Groups)
        {
            if (f.Groups.Count > 1 || group.Title != "all")
                H2(sb, Capitalize(group.Title), prefs);
            RenderCommitBullets(sb, group.Commits, prefs);
        }

        if (prefs.Verbosity != Verbosity.Terse)
        {
            H2(sb, "Why", prefs);
            if (f.Reasons.Count == 0)
                sb.AppendLine("No rationale recorded in commit messages.");
            foreach (var reason in f.Reasons)
                sb.AppendLine($"- {reason}");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public string RenderLooseEnds(LooseEndsFacts f, Preferences prefs)
    {
        var sb = new StringBuilder();
        H1(sb, "Loose ends", prefs);

        var clean = f.StaleBranches.Count == 0 && f.UnpushedBranches.Count == 0
            && f.Working.Total == 0 && f.TodoTotal == 0;
        if (clean)
        {
            sb.AppendLine(prefs.Tone == Tone.Friendly
                ? "Nothing dangling. Go build something."
                : "None found.");
            return sb.ToString();
        }

        if (f.Working.Total > 0)
        {
            H2(sb, "Uncommitted work", prefs);
            sb.AppendLine($"- {f.Working.Staged} staged, {f.Working.Unstaged} modified, {f.Working.Untracked} untracked");
        }
        if (f.UnpushedBranches.Count > 0)
        {
            H2(sb, "Unpushed", prefs);
            foreach (var b in f.UnpushedBranches)
                sb.AppendLine(b.Upstream is null
                    ? $"- {b.Name}: no upstream set"
                    : $"- {b.Name}: {b.Ahead} commit(s) ahead of {b.Upstream}");
        }
        if (f.StaleBranches.Count > 0)
        {
            H2(sb, "Stale branches", prefs);
            foreach (var b in f.StaleBranches)
                sb.AppendLine($"- {b.Name}: last commit {b.LastCommit:yyyy-MM-dd}");
        }
        if (f.TodoTotal > 0)
        {
            H2(sb, $"Markers ({f.TodoTotal} total)", prefs);
            foreach (var t in f.Todos)
                sb.AppendLine($"- {t.File}:{t.Line} {t.Text}");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void RenderCommitBullets(
        StringBuilder sb, IReadOnlyList<Git.Commit> commits, Preferences prefs)
    {
        if (prefs.Verbosity == Verbosity.Terse)
        {
            var subjects = commits
                .Select(c => Classification.CleanSubject(c.Subject))
                .Take(3)
                .ToList();
            var line = string.Join("; ", subjects);
            if (commits.Count > 3) line += $"; +{commits.Count - 3} more";
            sb.AppendLine($"- {line} ({commits.Count} commit(s))");
            return;
        }

        foreach (var c in commits)
        {
            sb.AppendLine($"- {Classification.CleanSubject(c.Subject)}");
            if (prefs.Verbosity == Verbosity.Detailed && c.Body.Length > 0)
            {
                var first = c.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                sb.AppendLine($"  {first}");
            }
        }
        sb.AppendLine();
    }

    private static void H1(StringBuilder sb, string text, Preferences prefs)
    {
        if (prefs.Format == OutputFormat.Markdown) sb.AppendLine($"# {text}");
        else { sb.AppendLine(text); sb.AppendLine(new string('=', Math.Min(text.Length, 60))); }
        sb.AppendLine();
    }

    private static void H2(StringBuilder sb, string text, Preferences prefs)
    {
        if (prefs.Format == OutputFormat.Markdown) sb.AppendLine($"## {text}");
        else sb.AppendLine($"{text}:");
    }

    private static string Capitalize(string s)
        => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
}
