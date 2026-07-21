using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;
using Performa.Reports;

namespace Performa.Desktop.ViewModels;

public sealed class RepoCard(RepoSnapshot snap)
{
    public string Name { get; } = snap.Name;
    public string Branch { get; } = snap.Branch;

    public IReadOnlyList<string> Recent { get; } =
        snap.Recent.Count == 0
            ? ["No commits today"]
            : [.. snap.Recent.Select(c => Classification.CleanSubject(c.Subject))];

    public bool IsQuiet { get; } = snap.Recent.Count == 0;

    public string Status { get; } = BuildStatus(snap);
    public bool HasStatus { get; } = snap.UncommittedFiles > 0 || snap.UnpushedCommits > 0;
    public bool IsClean { get; } = snap.UncommittedFiles == 0 && snap.UnpushedCommits == 0;

    private static string BuildStatus(RepoSnapshot s)
    {
        var parts = new List<string>();
        if (s.UncommittedFiles > 0) parts.Add($"{s.UncommittedFiles} uncommitted");
        if (s.UnpushedCommits > 0) parts.Add($"{s.UnpushedCommits} unpushed");
        return parts.Count > 0 ? string.Join(" · ", parts) : "clean & pushed";
    }
}

public sealed class DashboardViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

    public DashboardViewModel(PerformaEngine engine)
    {
        _engine = engine;
        _ = LoadAsync();
    }

    public ObservableCollection<RepoCard> Repos { get; } = [];

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _hasWorkspace = true;
    public bool HasWorkspace { get => _hasWorkspace; set => SetProperty(ref _hasWorkspace, value); }

    private string _workspaceName = "";
    public string WorkspaceName { get => _workspaceName; set => SetProperty(ref _workspaceName, value); }

    private string _thisWeek = "0";
    public string ThisWeek { get => _thisWeek; set => SetProperty(ref _thisWeek, value); }

    private string _weekDelta = "";
    public string WeekDelta { get => _weekDelta; set => SetProperty(ref _weekDelta, value); }

    private string _streak = "0";
    public string Streak { get => _streak; set => SetProperty(ref _streak, value); }

    private string _busiest = "-";
    public string Busiest { get => _busiest; set => SetProperty(ref _busiest, value); }

    private string _repoCount = "0";
    public string RepoCount { get => _repoCount; set => SetProperty(ref _repoCount, value); }

    public async Task LoadAsync()
    {
        if (_engine.WorkspacePath is not { Length: > 0 } ws || !Directory.Exists(ws))
        {
            HasWorkspace = false;
            IsLoading = false;
            return;
        }

        IsLoading = true;
        var facts = await Task.Run(_engine.BuildWorkspace);

        Repos.Clear();
        foreach (var snap in facts.Repos)
            Repos.Add(new RepoCard(snap));

        WorkspaceName = facts.Root;
        RepoCount = facts.Repos.Count.ToString();
        ThisWeek = facts.Velocity.ThisWeek.ToString();
        Streak = facts.Velocity.StreakDays.ToString();
        Busiest = facts.Velocity.BusiestRepo ?? "-";

        var delta = facts.Velocity.ThisWeek - facts.Velocity.LastWeek;
        WeekDelta = delta switch
        {
            > 0 => $"+{delta} vs last week",
            < 0 => $"{delta} vs last week",
            _ => "same as last week",
        };

        IsLoading = false;
    }
}
