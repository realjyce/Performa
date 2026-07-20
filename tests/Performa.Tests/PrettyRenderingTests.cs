using Performa.Enrich;
using Performa.Git;
using Performa.Prefs;
using Performa.Reports;
using Xunit;

namespace Performa.Tests;

public class PrettyRenderingTests
{
    private static Commit MakeCommit(string subject)
        => new("abcdef1234567", "J", "j@x", DateTimeOffset.Now, subject, "", []);

    private static ChangelogFacts Facts() => new("v1.0", "a", "b",
    [
        ("Added", new[] { MakeCommit("feat: netgun") }),
        ("Fixed", new[] { MakeCommit("fix: zero hp") }),
    ]);

    [Fact]
    public void Pretty_output_contains_ansi_and_section_colors()
    {
        var output = new DeterministicEnricher(pretty: true)
            .RenderChangelog(Facts(), new Preferences());

        Assert.Contains("\x1b[", output);
        Assert.Contains(Ansi.Green + "Added", output);
        Assert.Contains(Ansi.Yellow + "Fixed", output);
        Assert.Contains("•", output);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void Markdown_output_contains_no_ansi()
    {
        var output = new DeterministicEnricher(pretty: false)
            .RenderChangelog(Facts(), new Preferences());

        Assert.DoesNotContain("\x1b[", output);
        Assert.Contains("## Added", output);
        Assert.Contains("- Netgun", output);
    }

    [Fact]
    public void Pretty_standup_has_stamp_and_rule()
    {
        var facts = new StandupFacts("PocketJS", DateTimeOffset.Now, DateTimeOffset.Now,
            [new ReportGroup("all", [MakeCommit("add thing")])], 0, 0);
        var output = new DeterministicEnricher(pretty: true)
            .RenderStandup(facts, new Preferences());

        Assert.Contains("performa · PocketJS", output);
        Assert.Contains("─", output);
    }
}
