using System.Collections.ObjectModel;
using Avalonia.Threading;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class ActivityCommit(string time, string repo, string subject)
{
    public string Time { get; } = time;
    public string Repo { get; } = repo;
    public string Subject { get; } = subject;
}

/// <summary>One day of the feed: a heading plus that day's commits.</summary>
public sealed class ActivityDay(string title, string count, IReadOnlyList<ActivityCommit> commits)
{
    public string Title { get; } = title;
    public string Count { get; } = count;
    public IReadOnlyList<ActivityCommit> Commits { get; } = commits;
}

/// <summary>
/// The last two weeks of your own commits, grouped by day. Dashboard says
/// where things stand now and Daily covers today; this is the trail behind
/// them, so "what was I doing on Tuesday" has an answer.
/// </summary>
public sealed class ActivityViewModel : ObservableObject, IActivatablePage
{
    private readonly PerformaEngine _engine;
    private readonly DispatcherTimer _timer;

    public ActivityViewModel(PerformaEngine engine)
    {
        _engine = engine;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
        engine.WorkspaceChanged += () => _ = LoadAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += (_, _) => _ = LoadAsync();
        _timer.Start();

        _ = LoadAsync();
    }

    public ObservableCollection<ActivityDay> Days { get; } = [];
    public RelayCommand RefreshCommand { get; }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string _summary = "";
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

    private string _lastRefreshed = "";
    public string LastRefreshed { get => _lastRefreshed; set => SetProperty(ref _lastRefreshed, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    public void OnActivated() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        var since = new DateTimeOffset(DateTimeOffset.Now.Date.AddDays(-13), DateTimeOffset.Now.Offset);
        var commits = await Task.Run(() => _engine.CommitsBack(since));

        Days.Clear();
        foreach (var group in commits.GroupBy(c => c.When.Date).OrderByDescending(g => g.Key))
        {
            var title = group.Key == DateTimeOffset.Now.Date ? "Today"
                : group.Key == DateTimeOffset.Now.Date.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("dddd, d MMMM");
            var rows = group
                .OrderByDescending(c => c.When)
                .Select(c => new ActivityCommit(c.When.ToString("HH:mm"), c.Repo, c.Subject))
                .ToList();
            Days.Add(new ActivityDay(title, $"{rows.Count} commit(s)", rows));
        }

        IsEmpty = Days.Count == 0;
        Summary = commits.Count == 0
            ? "No commits in the last two weeks."
            : $"{commits.Count} commit(s) across the last two weeks.";
        LastRefreshed = $"Updated {DateTimeOffset.Now:HH:mm}";
        IsLoading = false;
    }
}
