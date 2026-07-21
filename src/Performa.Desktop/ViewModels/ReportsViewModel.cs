using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed record RepoRef(string Name, string Path)
{
    public override string ToString() => Name;
}

public sealed class ReportsViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

    public ReportsViewModel(PerformaEngine engine)
    {
        _engine = engine;
        RefreshRepos();
        GenerateCommand = new RelayCommand(() => _ = GenerateAsync());
        engine.WorkspaceChanged += RefreshRepos;
    }

    public void RefreshRepos()
    {
        Repos.Clear();
        foreach (var path in _engine.DiscoverRepos())
            Repos.Add(new RepoRef(System.IO.Path.GetFileName(path), path));
        SelectedRepo = Repos.FirstOrDefault();
    }

    public ObservableCollection<RepoRef> Repos { get; } = [];
    public string[] Kinds { get; } = ["Standup", "Changelog", "Summary"];

    private RepoRef? _selectedRepo;
    public RepoRef? SelectedRepo
    {
        get => _selectedRepo;
        set => SetProperty(ref _selectedRepo, value);
    }

    private string _selectedKind = "Standup";
    public string SelectedKind
    {
        get => _selectedKind;
        set => SetProperty(ref _selectedKind, value);
    }

    private string _output = "Pick a repo and a report, then hit Generate.";
    public string Output { get => _output; set => SetProperty(ref _output, value); }

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        set { if (SetProperty(ref _isGenerating, value)) OnPropertyChanged(nameof(HasOutput)); }
    }

    public bool HasOutput => !_isGenerating;

    public RelayCommand GenerateCommand { get; }

    public async Task GenerateAsync()
    {
        if (SelectedRepo is null) return;
        IsGenerating = true;
        Output = "";
        var repo = SelectedRepo;
        var kind = SelectedKind;

        try
        {
            var text = await Task.Run(() => kind switch
            {
                "Changelog" => _engine.RenderChangelog(repo.Path),
                "Summary" => _engine.RenderSummary(repo.Path, _engine.CurrentBranch(repo.Path)),
                _ => _engine.RenderStandup(repo.Path),
            });
            Output = text.TrimEnd();
        }
        catch (Exception e)
        {
            Output = $"Could not generate: {e.Message}";
        }
        IsGenerating = false;
    }
}
