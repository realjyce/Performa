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

public sealed class LooseEndsViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

    public LooseEndsViewModel(PerformaEngine engine)
    {
        _engine = engine;
        _ = LoadAsync();
    }

    public ObservableCollection<LooseEndItem> Items { get; } = [];

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

        var collected = await Task.Run(() =>
        {
            var list = new List<LooseEndItem>();
            foreach (var path in repos)
            {
                var name = System.IO.Path.GetFileName(path);
                var f = _engine.BuildLooseEnds(path);

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
            }
            return list;
        });

        Items.Clear();
        foreach (var item in collected) Items.Add(item);

        IsClean = collected.Count == 0;
        Summary = collected.Count == 0
            ? "Nothing dangling across your workspace. Go build something."
            : $"{collected.Count} loose end(s) across {repos.Count} repositories.";
        IsLoading = false;
    }
}
