using System.Text;
using Performa.Reports;

namespace Performa.Enrich;

public static class WorkspaceRenderer
{
    public static string Render(WorkspaceFacts f, bool pretty)
    {
        var sb = new StringBuilder();

        if (pretty)
        {
            sb.AppendLine($"{Ansi.Bold}{Ansi.Cyan}performa{Ansi.Reset}{Ansi.Dim} · workspace: {f.Root}{Ansi.Reset}");
            sb.AppendLine($"{Ansi.Dim}{new string('─', 48)}{Ansi.Reset}");
        }
        else
        {
            sb.AppendLine($"# performa · workspace: {f.Root}");
        }
        sb.AppendLine();

        var v = f.Velocity;
        var velocity =
            $"{v.ThisWeek} commit(s) this week (last week {v.LastWeek}) · {v.StreakDays}-day streak";
        if (v.BusiestRepo is not null) velocity += $" · busiest: {v.BusiestRepo}";
        sb.AppendLine(pretty ? $"{Ansi.Bold}{velocity}{Ansi.Reset}" : velocity);
        sb.AppendLine();

        foreach (var repo in f.Repos)
        {
            if (pretty)
                sb.AppendLine($"{Ansi.Bold}{repo.Name}{Ansi.Reset}{Ansi.Dim} · {repo.Branch}{Ansi.Reset}");
            else
                sb.AppendLine($"## {repo.Name} · {repo.Branch}");

            foreach (var c in repo.Recent)
                sb.AppendLine(pretty
                    ? $"  {Ansi.Cyan}•{Ansi.Reset} {Classification.CleanSubject(c.Subject)}"
                    : $"- {Classification.CleanSubject(c.Subject)}");

            var warns = new List<string>();
            if (repo.UncommittedFiles > 0) warns.Add($"{repo.UncommittedFiles} uncommitted");
            if (repo.UnpushedCommits > 0) warns.Add($"{repo.UnpushedCommits} unpushed");
            if (warns.Count > 0)
                sb.AppendLine(pretty
                    ? $"  {Ansi.Yellow}!{Ansi.Reset} {string.Join(" · ", warns)}"
                    : $"- ! {string.Join(" · ", warns)}");
            else if (repo.Recent.Count == 0)
                sb.AppendLine(pretty
                    ? $"  {Ansi.Dim}quiet · clean{Ansi.Reset}"
                    : "- quiet, clean");
            sb.AppendLine();
        }

        sb.AppendLine(pretty
            ? $"{Ansi.Dim}performa standup · changelog · summary <branch> · loose-ends (--repo <path>){Ansi.Reset}"
            : "Commands: performa standup | changelog | summary <branch> | loose-ends (--repo <path>)");
        return sb.ToString();
    }
}
