using System.Text.RegularExpressions;
using Performa.Git;

namespace Performa.Reports;

public enum ChangeKind { Feature, Fix, Refactor, Docs, Chore, Other }

public static partial class Classification
{
    [GeneratedRegex(@"^(?<type>feat|fix|docs|refactor|perf|test|build|ci|chore|style)(\([^)]*\))?!?:\s*(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ConventionalPrefix();

    public static ChangeKind Classify(string subject)
    {
        var match = ConventionalPrefix().Match(subject);
        if (match.Success)
        {
            return match.Groups["type"].Value.ToLowerInvariant() switch
            {
                "feat" => ChangeKind.Feature,
                "fix" => ChangeKind.Fix,
                "docs" => ChangeKind.Docs,
                "refactor" or "perf" or "style" => ChangeKind.Refactor,
                _ => ChangeKind.Chore,
            };
        }

        var s = subject.ToLowerInvariant();
        if (StartsWithAny(s, "fix", "bugfix", "repair", "patch", "correct", "resolve"))
            return ChangeKind.Fix;
        if (StartsWithAny(s, "add", "implement", "create", "build", "introduce", "new "))
            return ChangeKind.Feature;
        if (StartsWithAny(s, "refactor", "clean", "rename", "move", "restructure", "simplify", "rewrite"))
            return ChangeKind.Refactor;
        if (StartsWithAny(s, "doc", "comment") || s.Contains("readme"))
            return ChangeKind.Docs;
        if (StartsWithAny(s, "bump", "upgrade", "update dep", "chore", "merge", "release", "version"))
            return ChangeKind.Chore;
        return ChangeKind.Other;
    }

    public static string CleanSubject(string subject)
    {
        var match = ConventionalPrefix().Match(subject);
        var text = match.Success ? match.Groups["rest"].Value : subject;
        text = text.Trim().TrimEnd('.');
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    public static string AreaOf(Commit commit)
    {
        if (commit.Files.Count == 0) return "general";
        var areas = commit.Files
            .Select(f => f.Replace('\\', '/'))
            .Select(f => f.Contains('/') ? f[..f.IndexOf('/')] : "(root)")
            .GroupBy(a => a)
            .OrderByDescending(g => g.Count())
            .First().Key;
        return areas;
    }

    public static string SectionTitle(ChangeKind kind) => kind switch
    {
        ChangeKind.Feature => "Added",
        ChangeKind.Fix => "Fixed",
        ChangeKind.Refactor => "Changed",
        ChangeKind.Docs => "Docs",
        ChangeKind.Chore => "Internal",
        _ => "Other",
    };

    private static bool StartsWithAny(string s, params string[] prefixes)
        => prefixes.Any(s.StartsWith);
}
