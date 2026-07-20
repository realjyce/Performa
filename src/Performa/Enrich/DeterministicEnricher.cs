using System.Text;
using Performa.Git;
using Performa.Prefs;
using Performa.Reports;

namespace Performa.Enrich;

public sealed class DeterministicEnricher(bool pretty = false) : IEnricher
{
    private static readonly Dictionary<string, string> SectionColors = new()
    {
        ["Added"] = Ansi.Green,
        ["Fixed"] = Ansi.Yellow,
        ["Changed"] = Ansi.Cyan,
        ["Docs"] = Ansi.Blue,
        ["Internal"] = Ansi.Magenta,
    };

    public string RenderStandup(StandupFacts f, Preferences prefs)
    {
        var sb = new StringBuilder();
        Stamp(sb, $"performa · {f.RepoName}");
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
            Warn(sb, $"Loose ends: {string.Join(", ", loose)}");
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
                var suffix = prefs.Verbosity == Verbosity.Detailed
                    ? DimText($" ({c.Sha[..7]})")
                    : "";
                Bullet(sb, Classification.CleanSubject(c.Subject) + suffix);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public string RenderSummary(SummaryFacts f, Preferences prefs)
    {
        var sb = new StringBuilder();
        H1(sb, $"What changed on {f.Target} (vs {f.BaseRef})", prefs);
        sb.AppendLine(DimText($"{f.Files} file(s) changed, +{f.Insertions}/-{f.Deletions}"));
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
                Bullet(sb, reason);
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
            if (pretty)
                Warn(sb, $"{f.Working.Staged} staged, {f.Working.Unstaged} modified, {f.Working.Untracked} untracked");
            else
                Bullet(sb, $"{f.Working.Staged} staged, {f.Working.Unstaged} modified, {f.Working.Untracked} untracked");
        }
        if (f.UnpushedBranches.Count > 0)
        {
            H2(sb, "Unpushed", prefs);
            foreach (var b in f.UnpushedBranches)
                Bullet(sb, b.Upstream is null
                    ? $"{b.Name}: no upstream set"
                    : $"{b.Name}: {b.Ahead} commit(s) ahead of {b.Upstream}");
        }
        if (f.StaleBranches.Count > 0)
        {
            H2(sb, "Stale branches", prefs);
            foreach (var b in f.StaleBranches)
                Bullet(sb, $"{b.Name}: last commit {b.LastCommit:yyyy-MM-dd}");
        }
        if (f.TodoTotal > 0)
        {
            H2(sb, $"Markers ({f.TodoTotal} total)", prefs);
            foreach (var t in f.Todos)
                Bullet(sb, $"{DimText($"{t.File}:{t.Line}")} {t.Text}");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private void RenderCommitBullets(
        StringBuilder sb, IReadOnlyList<Commit> commits, Preferences prefs)
    {
        if (prefs.Verbosity == Verbosity.Terse)
        {
            var subjects = commits
                .Select(c => Classification.CleanSubject(c.Subject))
                .Take(3)
                .ToList();
            var line = string.Join("; ", subjects);
            if (commits.Count > 3) line += $"; +{commits.Count - 3} more";
            Bullet(sb, $"{line} {DimText($"({commits.Count} commit(s))")}");
            return;
        }

        foreach (var c in commits)
        {
            Bullet(sb, Classification.CleanSubject(c.Subject));
            if (prefs.Verbosity == Verbosity.Detailed && c.Body.Length > 0)
            {
                var first = c.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                sb.AppendLine($"  {DimText(first)}");
            }
        }
        sb.AppendLine();
    }

    private void Stamp(StringBuilder sb, string text)
    {
        if (!pretty) return;
        sb.AppendLine($"{Ansi.Dim}{text}{Ansi.Reset}");
    }

    private void H1(StringBuilder sb, string text, Preferences prefs)
    {
        if (pretty)
        {
            sb.AppendLine($"{Ansi.Bold}{Ansi.Cyan}{text}{Ansi.Reset}");
            sb.AppendLine($"{Ansi.Dim}{new string('─', Math.Min(text.Length, 60))}{Ansi.Reset}");
        }
        else if (prefs.Format == OutputFormat.Markdown)
        {
            sb.AppendLine($"# {text}");
        }
        else
        {
            sb.AppendLine(text);
            sb.AppendLine(new string('=', Math.Min(text.Length, 60)));
        }
        sb.AppendLine();
    }

    private void H2(StringBuilder sb, string text, Preferences prefs)
    {
        if (pretty)
        {
            var color = SectionColors.GetValueOrDefault(text, "");
            sb.AppendLine($"{Ansi.Bold}{color}{text}{Ansi.Reset}");
        }
        else if (prefs.Format == OutputFormat.Markdown)
        {
            sb.AppendLine($"## {text}");
        }
        else
        {
            sb.AppendLine($"{text}:");
        }
    }

    private void Bullet(StringBuilder sb, string text)
        => sb.AppendLine(pretty ? $"  {Ansi.Cyan}•{Ansi.Reset} {text}" : $"- {text}");

    private void Warn(StringBuilder sb, string text)
        => sb.AppendLine(pretty ? $"  {Ansi.Yellow}! {text}{Ansi.Reset}" : text);

    private string DimText(string text)
        => pretty ? $"{Ansi.Dim}{text}{Ansi.Reset}" : text;

    private static string Capitalize(string s)
        => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
}
