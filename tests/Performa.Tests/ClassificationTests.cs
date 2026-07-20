using Performa.Reports;
using Xunit;

namespace Performa.Tests;

public class ClassificationTests
{
    [Theory]
    [InlineData("feat: add netgun catching", ChangeKind.Feature)]
    [InlineData("feat(battle)!: rework turn order", ChangeKind.Feature)]
    [InlineData("fix: enemy acting at zero hp", ChangeKind.Fix)]
    [InlineData("Fixed the deck skipping cards", ChangeKind.Fix)]
    [InlineData("Add party menu", ChangeKind.Feature)]
    [InlineData("Implement scroll brake", ChangeKind.Feature)]
    [InlineData("refactor: split battle scene", ChangeKind.Refactor)]
    [InlineData("Rename monsters to custom species", ChangeKind.Refactor)]
    [InlineData("docs: update readme", ChangeKind.Docs)]
    [InlineData("Bump lenis to 1.3", ChangeKind.Chore)]
    [InlineData("chore: gitignore node_modules", ChangeKind.Chore)]
    [InlineData("Weekly checkpoint", ChangeKind.Other)]
    public void Classifies_subjects(string subject, ChangeKind expected)
        => Assert.Equal(expected, Classification.Classify(subject));

    [Theory]
    [InlineData("feat: add netgun catching", "Add netgun catching")]
    [InlineData("fix(deck): card skip.", "Card skip")]
    [InlineData("plain subject", "Plain subject")]
    public void Cleans_subjects(string subject, string expected)
        => Assert.Equal(expected, Classification.CleanSubject(subject));

    [Fact]
    public void Area_is_majority_top_level_directory()
    {
        var commit = new Git.Commit("abc", "j", "j@x", DateTimeOffset.Now, "s", "",
            ["src/Game/a.cs", "src/Game/b.cs", "docs/readme.md"]);
        Assert.Equal("src", Classification.AreaOf(commit));
    }

    [Fact]
    public void Area_of_root_files_is_root()
    {
        var commit = new Git.Commit("abc", "j", "j@x", DateTimeOffset.Now, "s", "",
            ["index.js"]);
        Assert.Equal("(root)", Classification.AreaOf(commit));
    }
}
