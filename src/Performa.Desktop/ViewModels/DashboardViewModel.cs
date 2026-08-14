using System.Collections.ObjectModel;
using Avalonia.Threading;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;
using Performa.Reports;

namespace Performa.Desktop.ViewModels;

public sealed class RepoCard : ObservableObject
{
    public RepoCard(RepoSnapshot snap)
    {
        Name = snap.Name;
        Path = snap.Path;
        Branch = snap.Branch;
        Recent = snap.Recent.Count == 0
            ? ["No commits today"]
            : [.. snap.Recent.Select(c => Classification.CleanSubject(c.Subject))];
        Status = BuildStatus(snap);
        IsClean = snap.UncommittedFiles == 0 && snap.UnpushedCommits == 0;
    }

    public string Name { get; }
    public string Path { get; }
    public string Branch { get; }
    public IReadOnlyList<string> Recent { get; }
    public string Status { get; }
    public bool IsClean { get; }

    private string _remoteText = "";
    public string RemoteText { get => _remoteText; set => SetProperty(ref _remoteText, value); }

    private string _webUrl = "";
    /// <summary>The repo's page on GitHub, empty when the remote is not GitHub
    /// or there is no remote at all.</summary>
    public string WebUrl
    {
        get => _webUrl;
        set { if (SetProperty(ref _webUrl, value)) OnPropertyChanged(nameof(HasWebUrl)); }
    }

    public bool HasWebUrl => _webUrl.Length > 0;

    private bool _hasRemote;
    public bool HasRemote { get => _hasRemote; set => SetProperty(ref _hasRemote, value); }

    private static string BuildStatus(RepoSnapshot s)
    {
        var parts = new List<string>();
        if (s.UncommittedFiles > 0) parts.Add($"{s.UncommittedFiles} uncommitted");
        if (s.UnpushedCommits > 0) parts.Add($"{s.UnpushedCommits} unpushed");
        return parts.Count > 0 ? string.Join(" · ", parts) : "clean & pushed";
    }
}

/// <summary>
/// One column of the activity chart. Height is a share of the tallest day, and
/// the view multiplies it by the plot height: the bar's own geometry, not a
/// pixel count baked in the view model.
/// </summary>
public sealed class DayBar(DateTime day, int count, int peak, bool isToday)
{
    public int Count { get; } = count;
    public bool IsToday { get; } = isToday;

    /// <summary>A day with no commits still shows a sliver, so the baseline
    /// reads as a row of days rather than a gap in the chart.</summary>
    public double Share { get; } = peak == 0 ? 0.02 : Math.Max(0.02, count / (double)peak);

    /// <summary>Single letter under the bar; the tooltip carries the full date.</summary>
    public string Initial { get; } = day.ToString("ddd")[..1];

    public string Tip { get; } =
        $"{day:ddd d MMM} · {count} commit(s)";
}

public sealed class QuickAction(string title, string blurb, string command)
{
    public string Title { get; } = title;
    public string Blurb { get; } = blurb;
    public string Command { get; } = command;
}

public sealed class DashboardViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;
    private readonly GitHubService _github = new();
    private readonly Action<string> _onQuickAction;
    private readonly DispatcherTimer _timer;

    public DashboardViewModel(PerformaEngine engine, Action<string> onQuickAction)
    {
        _engine = engine;
        _onQuickAction = onQuickAction;
        QuickCommand = new RelayCommand<string>(c => { if (c is not null) _onQuickAction(c); });
        OpenRepoCommand = new RelayCommand<RepoCard>(
            card => { if (card is not null) Launcher.OpenRepo(card.Path, _engine.Prefs.EditorCommand); });
        OpenWebCommand = new RelayCommand<RepoCard>(
            card => { if (card is { HasWebUrl: true }) Launcher.OpenUrl(card.WebUrl); });
        RescanCommand = new RelayCommand(() => _ = LoadAsync());
        DetectCommand = new RelayCommand(Detect);
        engine.WorkspaceChanged += () => _ = LoadAsync();

        // Local git is cheap, but each scan spawns processes; three minutes
        // keeps the dashboard live without churn.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        _timer.Tick += (_, _) => _ = LoadAsync();
        _timer.Start();

        _ = LoadAsync();
    }

    public ObservableCollection<RepoCard> Repos { get; } = [];

    public QuickAction[] QuickActions { get; } =
    [
        new("Standup", "What you did, grouped", "standup"),
        new("Changelog", "Release notes since last tag", "changelog"),
        new("Recap", "Branch summary & why", "summary"),
        new("Loose ends", "What's still open", "loose"),
    ];

    public RelayCommand<string> QuickCommand { get; }
    public RelayCommand<RepoCard> OpenRepoCommand { get; }
    public RelayCommand<RepoCard> OpenWebCommand { get; }
    public RelayCommand RescanCommand { get; }
    public RelayCommand DetectCommand { get; }

    private string _scanNote = "";
    public string ScanNote { get => _scanNote; set => SetProperty(ref _scanNote, value); }

    private void Detect()
    {
        ScanNote = _engine.AutoDetect()
            ? $"Found repositories in {_engine.WorkspacePath}"
            : "No git repositories found in the usual places. Pick a folder in Settings.";
    }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    /// <summary>When the workspace last actually scanned, so a quiet auto-rescan
    /// is visible and stale numbers are never mistaken for fresh.</summary>
    private string _lastRefreshed = "";
    public string LastRefreshed { get => _lastRefreshed; set => SetProperty(ref _lastRefreshed, value); }

    /// <summary>Fourteen days of commit counts, oldest first.</summary>
    public ObservableCollection<DayBar> Days { get; } = [];

    private string _daysSummary = "";
    public string DaysSummary { get => _daysSummary; set => SetProperty(ref _daysSummary, value); }

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
        {
            var card = new RepoCard(snap);
            // Resolved once here rather than per click: it shells out to git,
            // and the dashboard already has the repo open.
            if (_engine.RemoteSlug(snap.Path) is { } slug)
                card.WebUrl = $"https://github.com/{slug.Owner}/{slug.Name}";
            Repos.Add(card);
        }

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

        var byDay = await Task.Run(() => _engine.CommitsByDay(14));
        var peak = byDay.Count == 0 ? 0 : byDay.Max(d => d.Count);
        var today = DateTimeOffset.Now.Date;
        Days.Clear();
        foreach (var (day, count) in byDay)
            Days.Add(new DayBar(day, count, peak, day == today));

        var total = byDay.Sum(d => d.Count);
        var active = byDay.Count(d => d.Count > 0);
        DaysSummary = total == 0
            ? "No commits in the last two weeks"
            : $"{total} commits over {active} active day(s) · busiest {peak}";

        IsLoading = false;
        LastRefreshed = $"Updated {DateTimeOffset.Now:HH:mm}";
        _ = LoadRemotesAsync();
    }

    private async Task LoadRemotesAsync()
    {
        var token = _engine.GitHubAccessToken;
        foreach (var card in Repos)
        {
            var slug = _engine.RemoteSlug(card.Path);
            if (slug is not { } s) continue;
            var info = await _github.GetRepoAsync(s.Owner, s.Name, token);
            if (info is null) continue;

            var bits = new List<string> { $"★ {info.Stars}" };
            if (info.OpenIssues > 0) bits.Add($"{info.OpenIssues} open");
            if (info.PushedAt is { } pushed) bits.Add($"pushed {Ago(pushed)}");
            card.RemoteText = string.Join("  ·  ", bits);
            card.HasRemote = true;
        }
    }

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }
}
