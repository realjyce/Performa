using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;
using Performa.Prefs;

namespace Performa.Desktop.ViewModels;

public sealed class GitHubRepoRow : ObservableObject
{
    public required string Name { get; init; }
    public required string Meta { get; init; }
    public required string CloneUrl { get; init; }

    private bool _isLocal;
    public bool IsLocal { get => _isLocal; set { SetProperty(ref _isLocal, value); OnPropertyChanged(nameof(CanClone)); } }
    public bool CanClone => !_isLocal;

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

    public SettingsViewModel(PerformaEngine engine)
    {
        _engine = engine;
        _workspacePath = engine.WorkspacePath ?? "";
        _gitHubToken = engine.Prefs.GitHubToken ?? "";
        _verbosity = engine.Prefs.Verbosity.ToString();
        _grouping = engine.Prefs.Grouping.ToString();
        _tone = engine.Prefs.Tone.ToString();
        SaveCommand = new RelayCommand(Save);
        ScanGitHubCommand = new RelayCommand(() => _ = ScanGitHubAsync());
        AutoDetectCommand = new RelayCommand(AutoDetectLocal);
        CloneCommand = new RelayCommand<GitHubRepoRow>(row => { if (row is not null) Clone(row); });
    }

    private readonly GitHubService _github = new();

    public ObservableCollection<GitHubRepoRow> GitHubRepos { get; } = [];

    public RelayCommand ScanGitHubCommand { get; }
    public RelayCommand AutoDetectCommand { get; }
    public RelayCommand<GitHubRepoRow> CloneCommand { get; }

    private string _gitHubNote = "";
    public string GitHubNote { get => _gitHubNote; set => SetProperty(ref _gitHubNote, value); }

    private string _localNote = "";
    public string LocalNote { get => _localNote; set => SetProperty(ref _localNote, value); }

    private bool _scanning;
    public bool Scanning { get => _scanning; set => SetProperty(ref _scanning, value); }

    private void AutoDetectLocal()
    {
        if (_engine.AutoDetect())
        {
            WorkspacePath = _engine.WorkspacePath ?? "";
            LocalNote = $"Found {_engine.DiscoverRepos().Count} repositories in {WorkspacePath}";
        }
        else
        {
            LocalNote = "No git repositories found in the usual dev folders.";
        }
    }

    private async Task ScanGitHubAsync()
    {
        var token = GitHubToken.Trim();
        if (token.Length == 0)
        {
            GitHubNote = "Paste a GitHub token above first, then scan.";
            return;
        }

        Scanning = true;
        GitHubNote = "Asking GitHub…";
        GitHubRepos.Clear();

        var repos = await _github.GetUserReposAsync(token);
        if (repos is null)
        {
            GitHubNote = "GitHub refused that token. Check it has repo read access.";
            Scanning = false;
            return;
        }

        var local = _engine.LocalRepoNames();
        foreach (var r in repos)
        {
            var bits = new List<string> { r.IsPrivate ? "private" : "public" };
            if (r.Language is { Length: > 0 }) bits.Add(r.Language);
            if (r.PushedAt is { } p) bits.Add($"pushed {p:yyyy-MM-dd}");
            GitHubRepos.Add(new GitHubRepoRow
            {
                Name = r.Name,
                Meta = string.Join(" · ", bits),
                CloneUrl = r.CloneUrl,
                IsLocal = local.Contains(r.Name),
            });
        }

        var missing = GitHubRepos.Count(r => !r.IsLocal);
        GitHubNote = $"{GitHubRepos.Count} repositories on GitHub · {missing} not on this machine";
        Scanning = false;
    }

    private void Clone(GitHubRepoRow row)
    {
        row.Status = "Cloning…";
        var result = _engine.Clone(row.CloneUrl, row.Name);
        row.Status = result;
        if (result == "Cloned.") row.IsLocal = true;
    }

    private string _workspacePath;
    public string WorkspacePath
    {
        get => _workspacePath;
        set => SetProperty(ref _workspacePath, value);
    }

    private string _gitHubToken;
    public string GitHubToken
    {
        get => _gitHubToken;
        set => SetProperty(ref _gitHubToken, value);
    }

    public string[] Verbosities { get; } = ["Terse", "Normal", "Detailed"];
    public string[] Groupings { get; } = ["Area", "Kind", "Flat"];
    public string[] Tones { get; } = ["Plain", "Friendly"];

    private string _verbosity;
    public string Verbosity { get => _verbosity; set => SetProperty(ref _verbosity, value); }

    private string _grouping;
    public string Grouping { get => _grouping; set => SetProperty(ref _grouping, value); }

    private string _tone;
    public string Tone { get => _tone; set => SetProperty(ref _tone, value); }

    private string _savedNote = "";
    public string SavedNote { get => _savedNote; set => SetProperty(ref _savedNote, value); }

    public RelayCommand SaveCommand { get; }

    /// <summary>Applies a picked folder immediately and reloads the pages.</summary>
    public void ApplyWorkspace(string path)
    {
        WorkspacePath = path;
        if (Directory.Exists(path))
        {
            _engine.SetWorkspace(path);
            SavedNote = "Workspace updated. Your pages just reloaded.";
        }
        else
        {
            SavedNote = "That folder was not found.";
        }
    }

    private void Save()
    {
        _engine.Prefs.GitHubToken = string.IsNullOrWhiteSpace(GitHubToken) ? null : GitHubToken.Trim();
        _engine.Prefs.Verbosity = Enum.Parse<Performa.Prefs.Verbosity>(_verbosity);
        _engine.Prefs.Grouping = Enum.Parse<Performa.Prefs.Grouping>(_grouping);
        _engine.Prefs.Tone = Enum.Parse<Performa.Prefs.Tone>(_tone);
        _engine.Prefs.Initialised = true;

        if (Directory.Exists(WorkspacePath))
            _engine.SetWorkspace(WorkspacePath); // saves prefs + raises reload
        else
            _engine.SavePrefs();

        // Token or output changes should take effect without a restart.
        _engine.Rescan();

        SavedNote = Directory.Exists(WorkspacePath)
            ? "Saved."
            : "Saved. Workspace folder not found, left unchanged.";
    }
}
