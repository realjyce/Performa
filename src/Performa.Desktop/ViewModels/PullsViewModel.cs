using System.Collections.ObjectModel;
using Avalonia.Threading;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class WorkItemRow(WorkItem item)
{
    public string Repo { get; } = item.Repo;
    public string Number { get; } = $"#{item.Number}";
    public string Title { get; } = item.Title;
    public string Url { get; } = item.Url;
    public string Kind { get; } = item.IsPull ? "PR" : "issue";
    public bool IsPull { get; } = item.IsPull;

    public string Updated { get; } = item.Updated is { } u
        ? (DateTimeOffset.Now - u) switch
        {
            { TotalMinutes: < 60 } s => $"{(int)s.TotalMinutes}m ago",
            { TotalHours: < 24 } s => $"{(int)s.TotalHours}h ago",
            var s => $"{(int)s.TotalDays}d ago",
        }
        : "";
}

/// <summary>
/// Open PRs and issues that involve you, from GitHub's own "involves" search.
/// Inbox is what email asks of you; this is what code asks of you.
/// </summary>
public sealed class PullsViewModel : ObservableObject, IActivatablePage
{
    private readonly PerformaEngine _engine;
    private readonly GitHubService _github = new();
    private readonly DispatcherTimer _timer;

    public PullsViewModel(PerformaEngine engine)
    {
        _engine = engine;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
        OpenCommand = new RelayCommand<WorkItemRow>(Open);
        engine.GitHubSignedInChanged += () => _ = LoadAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += (_, _) => _ = LoadAsync();
        _timer.Start();

        _ = LoadAsync();
    }

    public ObservableCollection<WorkItemRow> Pulls { get; } = [];
    public ObservableCollection<WorkItemRow> Issues { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public RelayCommand<WorkItemRow> OpenCommand { get; }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private string _lastRefreshed = "";
    public string LastRefreshed { get => _lastRefreshed; set => SetProperty(ref _lastRefreshed, value); }

    private bool _connected;
    public bool Connected { get => _connected; set => SetProperty(ref _connected, value); }

    public bool HasPulls => Pulls.Count > 0;
    public bool HasIssues => Issues.Count > 0;

    public void OnActivated()
    {
        if (Pulls.Count == 0 && Issues.Count == 0) _ = LoadAsync();
    }

    private static void Open(WorkItemRow? row)
    {
        if (row?.Url is not { Length: > 0 } url) return;
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private async Task LoadAsync()
    {
        var token = _engine.GitHubAccessToken;
        Connected = token is not null;
        if (token is null)
        {
            Status = "Sign in with GitHub in Settings to see what's waiting on you.";
            return;
        }

        IsLoading = true;
        var work = await _github.GetOpenWorkAsync(token);
        if (work is null)
        {
            Status = "GitHub did not answer. Check the connection or the token.";
            IsLoading = false;
            return;
        }

        Pulls.Clear();
        Issues.Clear();
        foreach (var item in work)
        {
            var row = new WorkItemRow(item);
            if (row.IsPull) Pulls.Add(row); else Issues.Add(row);
        }
        OnPropertyChanged(nameof(HasPulls));
        OnPropertyChanged(nameof(HasIssues));

        Status = work.Count == 0
            ? "Nothing open involves you right now. Enjoy it."
            : $"{Pulls.Count} pull request(s) and {Issues.Count} issue(s) involve you.";
        LastRefreshed = $"Updated {DateTimeOffset.Now:HH:mm}";
        IsLoading = false;
    }
}
