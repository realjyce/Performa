using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class LooseEndItem(string kind, string text, string tone)
{
    public string Kind { get; } = kind;
    public string Text { get; } = text;
    public string Tone { get; } = tone; // "warn" | "info" | "danger"
}

/// <summary>One repo's line in the health roster: name, branch, verdict.</summary>
public sealed class RepoHealthRow(string name, string branch, string verdict, bool clean)
{
    public string Name { get; } = name;
    public string Branch { get; } = branch;
    public string Verdict { get; } = verdict;
    public bool Clean { get; } = clean;
}

public sealed class LooseEndsViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

    public LooseEndsViewModel(PerformaEngine engine)
    {
        _engine = engine;
        engine.WorkspaceChanged += () => _ = LoadAsync();
        _ = LoadAsync();
    }

    public ObservableCollection<LooseEndItem> Items { get; } = [];

    /// <summary>Per-repo verdicts, shown always. A clean workspace deserves a
    /// roster saying so per repo, not a bare tick in an empty page.</summary>
    public ObservableCollection<RepoHealthRow> Health { get; } = [];

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isClean;
    public bool IsClean { get => _isClean; set => SetProperty(ref _isClean, value); }

    private string _summary = "";
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        var repos = _engine.DiscoverRepos();

        var (collected, health) = await Task.Run(() =>
        {
            var list = new List<LooseEndItem>();
            var roster = new List<RepoHealthRow>();
            foreach (var path in repos)
            {
                var name = System.IO.Path.GetFileName(path);
                var f = _engine.BuildLooseEnds(path);
                var before = list.Count;

                if (f.Working.Total > 0)
                    list.Add(new LooseEndItem(name,
                        $"{f.Working.Total} uncommitted file(s): {f.Working.Staged} staged, {f.Working.Unstaged} modified, {f.Working.Untracked} untracked",
                        "warn"));
                foreach (var b in f.UnpushedBranches)
                    list.Add(new LooseEndItem(name,
                        b.Upstream is null
                            ? $"branch '{b.Name}' has no upstream set"
                            : $"branch '{b.Name}' is {b.Ahead} commit(s) ahead of {b.Upstream}",
                        "info"));
                foreach (var b in f.StaleBranches)
                    list.Add(new LooseEndItem(name,
                        $"stale branch '{b.Name}', last commit {b.LastCommit:yyyy-MM-dd}", "info"));
                if (f.TodoTotal > 0)
                    list.Add(new LooseEndItem(name,
                        $"{f.TodoTotal} TODO/FIXME marker(s)", "danger"));

                var found = list.Count - before;
                var branch = _engine.CurrentBranch(path);
                roster.Add(new RepoHealthRow(name, branch,
                    found == 0 ? "clean" : $"{found} loose end(s)", found == 0));
            }
            return (list, roster);
        });

        Items.Clear();
        foreach (var item in collected) Items.Add(item);
        Health.Clear();
        foreach (var row in health) Health.Add(row);

        IsClean = collected.Count == 0;
        Summary = collected.Count == 0
            ? "Nothing dangling across your workspace. Go build something."
            : $"{collected.Count} loose end(s) across {repos.Count} repositories.";
        IsLoading = false;
    }
}
