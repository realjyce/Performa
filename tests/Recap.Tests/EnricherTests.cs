using Recap.Enrich;
using Recap.Git;
using Recap.Prefs;
using Recap.Reports;
using Xunit;

namespace Recap.Tests;

public class EnricherTests
{
    private static Commit MakeCommit(string subject, string body = "", params string[] files)
        => new("abcdef1234567", "Jason", "j@x", DateTimeOffset.Now, subject, body, files);

    private readonly DeterministicEnricher _enricher = new();

    [Fact]
    public void Standup_groups_and_loose_ends_render()
    {
        var facts = new StandupFacts(
            "PocketJS",
            new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            [new ReportGroup("battle", [MakeCommit("feat: add netgun")])],
            UnpushedCommits: 2,
            UncommittedFiles: 1);

        var output = _enricher.RenderStandup(facts, new Preferences());

        Assert.Contains("# Standup", output);
        Assert.Contains("## Battle", output);
        Assert.Contains("- Add netgun", output);
        Assert.Contains("2 unpushed commit(s)", output);
        Assert.Contains("1 uncommitted file(s)", output);
    }

    [Fact]
    public void Standup_terse_collapses_to_single_bullet_per_group()
    {
        var commits = Enumerable.Range(0, 5)
            .Select(i => MakeCommit($"add thing {i}"))
            .ToList();
        var facts = new StandupFacts(
            "x", DateTimeOffset.Now, DateTimeOffset.Now,
            [new ReportGroup("all", commits)], 0, 0);

        var output = _enricher.RenderStandup(
            facts, new Preferences { Verbosity = Verbosity.Terse });

        Assert.Contains("+2 more", output);
        Assert.Contains("(5 commit(s))", output);
        Assert.Single(output.Split('\n'), l => l.StartsWith("- "));
    }

    [Fact]
    public void Changelog_sections_in_order_with_clean_subjects()
    {
        var facts = new ChangelogFacts("v0.2.0", "v0.1.0", "HEAD",
        [
            ("Added", new[] { MakeCommit("feat: netgun catch system") }),
            ("Fixed", new[] { MakeCommit("fix: enemy acting at zero hp") }),
        ]);

        var output = _enricher.RenderChangelog(facts, new Preferences());

        Assert.Contains("# v0.2.0", output);
        var added = output.IndexOf("## Added", StringComparison.Ordinal);
        var @fixed = output.IndexOf("## Fixed", StringComparison.Ordinal);
        Assert.True(added >= 0 && @fixed > added);
        Assert.Contains("- Netgun catch system", output);
        Assert.DoesNotContain("feat:", output);
    }

    [Fact]
    public void Changelog_detailed_includes_short_shas()
    {
        var facts = new ChangelogFacts("v1", "a", "b",
            [("Added", new[] { MakeCommit("feat: x") })]);
        var output = _enricher.RenderChangelog(
            facts, new Preferences { Verbosity = Verbosity.Detailed });
        Assert.Contains("(abcdef1)", output);
    }

    [Fact]
    public void Summary_includes_stats_and_reasons()
    {
        var facts = new SummaryFacts("feature/nets", "main",
            [new ReportGroup("all", [MakeCommit("add nets")])],
            ["Players need a catch mechanic."], 3, 120, 40);

        var output = _enricher.RenderSummary(facts, new Preferences());

        Assert.Contains("What changed on feature/nets (vs main)", output);
        Assert.Contains("3 file(s) changed, +120/-40", output);
        Assert.Contains("## Why", output);
        Assert.Contains("- Players need a catch mechanic.", output);
    }

    [Fact]
    public void Text_format_has_no_markdown_hashes()
    {
        var facts = new StandupFacts("x", DateTimeOffset.Now, DateTimeOffset.Now,
            [new ReportGroup("all", [MakeCommit("add thing")])], 0, 0);
        var output = _enricher.RenderStandup(
            facts, new Preferences { Format = OutputFormat.Text });
        Assert.DoesNotContain("# ", output);
        Assert.Contains("=", output);
    }
}
