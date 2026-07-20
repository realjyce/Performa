using Performa.Enrich;
using Performa.Git;
using Performa.Reports;
using Xunit;

namespace Performa.Tests;

[Collection("git-environment")]
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root;

    public WorkspaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"performa-ws-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        MakeRepo("alpha", commitToday: true);
        MakeRepo("beta", commitToday: false);
        Directory.CreateDirectory(Path.Combine(_root, "not-a-repo"));
    }

    private void MakeRepo(string name, bool commitToday)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var git = new GitRunner(dir);
        git.Run("init", "-q", "-b", "main");
        git.Run("config", "user.email", "test@performa.local");
        git.Run("config", "user.name", "Performa Test");
        File.WriteAllText(Path.Combine(dir, "a.txt"), name);
        git.Run("add", "-A");
        if (commitToday)
        {
            git.Run("commit", "-q", "-m", $"feat: work in {name}");
        }
        else
        {
            var old = DateTimeOffset.Now.AddDays(-10).ToString("yyyy-MM-ddTHH:mm:ss");
            Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", old);
            Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", old);
            git.Run("commit", "-q", "-m", $"chore: old work in {name}");
            Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", null);
            Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", null);
        }
    }

    [Fact]
    public void Discovers_only_git_repos()
    {
        var repos = WorkspaceBuilder.DiscoverRepos(_root);
        Assert.Equal(2, repos.Count);
        Assert.DoesNotContain(repos, r => r.EndsWith("not-a-repo"));
    }

    [Fact]
    public void Builds_snapshots_and_velocity()
    {
        var facts = WorkspaceBuilder.Build(_root, DateTimeOffset.Now);

        Assert.Equal(2, facts.Repos.Count);
        var alpha = facts.Repos.Single(r => r.Name == "alpha");
        Assert.Single(alpha.Recent);
        Assert.Equal("main", alpha.Branch);

        Assert.True(facts.Velocity.ThisWeek >= 1);
        Assert.True(facts.Velocity.LastWeek >= 1);
        Assert.True(facts.Velocity.StreakDays >= 1);
        Assert.Equal("alpha", facts.Velocity.BusiestRepo);
    }

    [Fact]
    public void Renderer_produces_both_faces()
    {
        var facts = WorkspaceBuilder.Build(_root, DateTimeOffset.Now);

        var md = WorkspaceRenderer.Render(facts, pretty: false);
        Assert.Contains("# performa · workspace:", md);
        Assert.Contains("## alpha · main", md);
        Assert.Contains("commit(s) this week", md);
        Assert.DoesNotContain("\x1b[", md);

        var ansi = WorkspaceRenderer.Render(facts, pretty: true);
        Assert.Contains("\x1b[", ansi);
        Assert.Contains("alpha", ansi);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
