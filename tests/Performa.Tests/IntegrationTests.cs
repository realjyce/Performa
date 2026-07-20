using Performa.Git;
using Performa.Prefs;
using Performa.Reports;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// Builds a real git repository in a temp directory and runs the full
/// pipeline against it. Requires git on PATH, same as the tool itself.
/// </summary>
public sealed class IntegrationTests : IDisposable
{
    private readonly string _dir;
    private readonly GitRunner _git;
    private readonly RepoQueries _repo;

    public IntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"performa-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _git = new GitRunner(_dir);
        _git.Run("init", "-q", "-b", "main");
        _git.Run("config", "user.email", "test@performa.local");
        _git.Run("config", "user.name", "Performa Test");

        CommitFile("src/engine.js", "core", "feat: add game engine core", "2026-07-10T10:00:00");
        CommitFile("src/battle.js", "battle", "feat: add battle system", "2026-07-15T10:00:00");
        _git.Run("tag", "v0.1.0");
        CommitFile("src/battle.js", "battle v2", "fix: enemy acting at zero hp", "2026-07-18T10:00:00");
        CommitFile("docs/readme.md", "docs", "docs: write readme", "2026-07-19T10:00:00");

        _repo = new RepoQueries(_git);
    }

    private void CommitFile(string path, string content, string message, string date)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        _git.Run("add", "-A");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", date);
        Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", date);
        _git.Run("commit", "-q", "-m", message);
        Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", null);
        Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", null);
    }

    [Fact]
    public void Standup_finds_commits_since_date()
    {
        var facts = FactBuilders.BuildStandup(
            _repo, new Preferences(),
            since: DateTimeOffset.Parse("2026-07-16T00:00:00+00:00"),
            now: DateTimeOffset.Parse("2026-07-20T00:00:00+00:00"));

        var all = facts.Groups.SelectMany(g => g.Commits).ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Subject.Contains("zero hp"));
        Assert.Contains(all, c => c.Subject.Contains("readme"));
    }

    [Fact]
    public void Changelog_defaults_to_since_last_tag()
    {
        var facts = FactBuilders.BuildChangelog(_repo, from: null, to: "HEAD");
        Assert.Equal("v0.1.0", facts.FromRef);
        var subjects = facts.Sections.SelectMany(s => s.Commits).Select(c => c.Subject).ToList();
        Assert.Equal(2, subjects.Count);
        Assert.DoesNotContain(subjects, s => s.Contains("engine core"));
        Assert.Contains(facts.Sections, s => s.Section == "Fixed");
        Assert.Contains(facts.Sections, s => s.Section == "Docs");
    }

    [Fact]
    public void Summary_of_feature_branch_vs_main()
    {
        _git.Run("checkout", "-q", "-b", "feature/nets");
        CommitFile("src/nets.js", "nets", "feat: netgun catching\n\nPlayers need a catch mechanic.", "2026-07-20T10:00:00");
        var facts = FactBuilders.BuildSummary(_repo, new Preferences(), "feature/nets");

        Assert.Equal("main", facts.BaseRef);
        Assert.Single(facts.Groups.SelectMany(g => g.Commits));
        Assert.Contains("Players need a catch mechanic.", facts.Reasons);
        Assert.True(facts.Insertions > 0);
        _git.Run("checkout", "-q", "main");
    }

    [Fact]
    public void Loose_ends_sees_uncommitted_and_todos()
    {
        File.WriteAllText(Path.Combine(_dir, "src", "wip.js"), "// TODO: finish this\n");
        _git.Run("add", "src/wip.js");
        _git.Run("commit", "-q", "-m", "add wip marker");
        _git.Run("branch", "wip-branch");
        File.WriteAllText(Path.Combine(_dir, "uncommitted.txt"), "dirty");

        var facts = FactBuilders.BuildLooseEnds(_repo);

        Assert.True(facts.Working.Untracked >= 1);
        Assert.True(facts.TodoTotal >= 1);
        Assert.Contains(facts.Todos, t => t.File.EndsWith("wip.js"));
        Assert.Contains(facts.UnpushedBranches,
            b => b.Name == "wip-branch" && b.Upstream is null);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
