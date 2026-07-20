using Performa.Git;
using Xunit;

namespace Performa.Tests;

public class GitLogParserTests
{
    private const string RS = "\x1e";
    private const string US = "\x1f";
    private const string GS = "\x1d";

    [Fact]
    public void Parses_two_commits_with_files_and_multiline_body()
    {
        var raw =
            RS + "aaa111" + US + "Jason" + US + "j@example.com" + US + "2026-07-18T10:00:00+09:00"
               + US + "feat: add catching" + US + "Uses a netgun.\nInfinite ammo." + GS
               + "\nbattle.js\nclasses.js\n"
          + RS + "bbb222" + US + "Jason" + US + "j@example.com" + US + "2026-07-17T09:00:00+09:00"
               + US + "fix: turn order" + US + "" + GS + "\nbattle.js\n";

        var commits = GitLogParser.Parse(raw);

        Assert.Equal(2, commits.Count);
        Assert.Equal("aaa111", commits[0].Sha);
        Assert.Equal("feat: add catching", commits[0].Subject);
        Assert.Equal("Uses a netgun.\nInfinite ammo.", commits[0].Body);
        Assert.Equal(["battle.js", "classes.js"], commits[0].Files);
        Assert.Equal(18, commits[0].Date.Day);
        Assert.Empty(commits[1].Body);
        Assert.Equal(["battle.js"], commits[1].Files);
    }

    [Fact]
    public void Parses_commit_without_files()
    {
        var raw = RS + "ccc333" + US + "J" + US + "j@x" + US + "2026-07-01T00:00:00Z"
            + US + "empty commit" + US + "" + GS;
        var commits = GitLogParser.Parse(raw);
        Assert.Single(commits);
        Assert.Empty(commits[0].Files);
    }

    [Fact]
    public void Empty_input_gives_no_commits()
        => Assert.Empty(GitLogParser.Parse(""));
}
