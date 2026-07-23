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
        _userName = engine.Prefs.UserName ?? "";
        _geminiKey = engine.Prefs.GeminiApiKey ?? "";
        _anthropicKey = engine.Prefs.AnthropicApiKey ?? "";
        _openAiKey = engine.Prefs.OpenAiApiKey ?? "";
        _aiProvider = engine.Prefs.AiProvider.ToString();
        _aiEnabled = engine.Prefs.AiEnabled;
        _workspacePath = engine.WorkspacePath ?? "";
        _gitHubToken = engine.Prefs.GitHubToken ?? "";
        _verbosity = engine.Prefs.Verbosity.ToString();
        _grouping = engine.Prefs.Grouping.ToString();
        _tone = engine.Prefs.Tone.ToString();
        SaveCommand = new RelayCommand(Save);
        _launchAtStartup = StartupService.IsEnabled();
        ScanGitHubCommand = new RelayCommand(() => _ = ScanGitHubAsync());
        AutoDetectCommand = new RelayCommand(AutoDetectLocal);
        CloneCommand = new RelayCommand<GitHubRepoRow>(row => { if (row is not null) Clone(row); });

        _googleClientId = engine.Prefs.GoogleClientId ?? "";
        _googleClientSecret = engine.Prefs.GoogleClientSecret ?? "";
        GoogleSignInCommand = new RelayCommand(() => _ = GoogleSignInAsync());
        GoogleSignOutCommand = new RelayCommand(GoogleSignOut);
        RefreshGoogleStatus();

        _gitHubClientId = engine.Prefs.GitHubClientId ?? "";
        GitHubSignInCommand = new RelayCommand(() => _ = GitHubSignInAsync());
        GitHubSignOutCommand = new RelayCommand(GitHubSignOut);
        RefreshGitHubStatus();
    }

    // Launch at desktop boot. Reads the live registry state rather than a
    // stored flag, so it stays honest if the user removes it from Task Manager.
    private bool _launchAtStartup;
    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (!SetProperty(ref _launchAtStartup, value)) return;
            StartupNote = StartupService.Set(value);
            _launchAtStartup = StartupService.IsEnabled();
            OnPropertyChanged(nameof(LaunchAtStartup));
        }
    }

    private string _startupNote = "";
    public string StartupNote { get => _startupNote; set => SetProperty(ref _startupNote, value); }

    private readonly GitHubService _github = new();
    private readonly GoogleAuthService _google = new();
    private readonly GitHubAuthService _gitHubAuth = new();

    private string _gitHubClientId;
    public string GitHubClientId
    {
        get => _gitHubClientId;
        set => SetProperty(ref _gitHubClientId, value);
    }

    private bool _gitHubConnected;
    public bool GitHubConnected
    {
        get => _gitHubConnected;
        set => SetProperty(ref _gitHubConnected, value);
    }

    private string _deviceCode = "";
    public string DeviceCode
    {
        get => _deviceCode;
        set { if (SetProperty(ref _deviceCode, value)) OnPropertyChanged(nameof(HasDeviceCode)); }
    }

    public bool HasDeviceCode => _deviceCode.Length > 0;

    private bool _showGitHubAdvanced;
    public bool ShowGitHubAdvanced
    {
        get => _showGitHubAdvanced;
        set => SetProperty(ref _showGitHubAdvanced, value);
    }

    public RelayCommand ToggleGitHubAdvancedCommand => new(() => ShowGitHubAdvanced = !ShowGitHubAdvanced);

    public RelayCommand GitHubSignInCommand { get; }
    public RelayCommand GitHubSignOutCommand { get; }

    private void RefreshGitHubStatus()
    {
        GitHubConnected = _gitHubAuth.IsSignedIn;
        if (GitHubConnected)
        {
            GitHubNote = "Signed in to GitHub.";
            return;
        }
        GitHubNote = _engine.GitHubAccessToken is null
            ? "Not connected. Public repositories only."
            : "Using the personal token below.";
    }

    /// <summary>
    /// Device flow: GitHub gives a short code, the user types it on github.com,
    /// and we poll until they approve. No client secret is involved.
    /// </summary>
    private async Task GitHubSignInAsync()
    {
        // The shipped build carries its own client id, so this is one click for
        // an end user. Typing one in Settings is only for pointing the app at
        // your own OAuth App.
        var typed = GitHubClientId.Trim();
        if (typed.Length > 0)
        {
            _engine.Prefs.GitHubClientId = typed;
            _engine.SavePrefs();
        }

        var clientId = AppCredentialStore.GitHubClientId(_engine.Prefs);
        if (clientId is null)
        {
            GitHubNote = "No GitHub client configured. Add one under Advanced.";
            ShowGitHubAdvanced = true;
            return;
        }

        GitHubNote = "Asking GitHub for a code…";
        var prompt = await _gitHubAuth.StartAsync(clientId);
        if (prompt is null)
        {
            GitHubNote = "GitHub would not start the sign-in. Check the client id.";
            return;
        }

        DeviceCode = prompt.UserCode;
        GitHubNote = $"Enter this code at {prompt.VerificationUri} — your browser should already be open.";

        var result = await _gitHubAuth.CompleteAsync(clientId);
        DeviceCode = "";
        GitHubNote = result;
        GitHubConnected = _gitHubAuth.IsSignedIn;
        if (GitHubConnected)
        {
            _engine.NotifyGitHubChanged();
            await ScanGitHubAsync();
        }
    }

    private void GitHubSignOut()
    {
        _gitHubAuth.SignOut();
        GitHubRepos.Clear();
        RefreshGitHubStatus();
    }

    private string _googleClientId;
    public string GoogleClientId
    {
        get => _googleClientId;
        set => SetProperty(ref _googleClientId, value);
    }

    private string _googleClientSecret;
    public string GoogleClientSecret
    {
        get => _googleClientSecret;
        set => SetProperty(ref _googleClientSecret, value);
    }

    private string _googleStatus = "";
    public string GoogleStatus { get => _googleStatus; set => SetProperty(ref _googleStatus, value); }

    private bool _googleConnected;
    public bool GoogleConnected
    {
        get => _googleConnected;
        set => SetProperty(ref _googleConnected, value);
    }

    public RelayCommand GoogleSignInCommand { get; }
    public RelayCommand GoogleSignOutCommand { get; }

    private bool _showGoogleAdvanced;
    public bool ShowGoogleAdvanced
    {
        get => _showGoogleAdvanced;
        set => SetProperty(ref _showGoogleAdvanced, value);
    }

    public RelayCommand ToggleGoogleAdvancedCommand => new(() => ShowGoogleAdvanced = !ShowGoogleAdvanced);

    private void RefreshGoogleStatus()
    {
        GoogleConnected = _google.IsSignedIn;
        if (GoogleConnected)
        {
            GoogleStatus = "Connected. Calendar and Gmail are readable.";
            return;
        }
        GoogleStatus = GoogleCredentialStore.Load(_engine.Prefs) is null
            ? "No Google client configured. Add one under Advanced."
            : "Ready to connect.";
    }

    private void SaveGoogleCredentials()
    {
        _engine.Prefs.GoogleClientId =
            string.IsNullOrWhiteSpace(GoogleClientId) ? null : GoogleClientId.Trim();
        _engine.Prefs.GoogleClientSecret =
            string.IsNullOrWhiteSpace(GoogleClientSecret) ? null : GoogleClientSecret.Trim();
        _engine.SavePrefs();
    }

    private async Task GoogleSignInAsync()
    {
        SaveGoogleCredentials();

        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null)
        {
            GoogleStatus = "No Google client configured. Add one under Advanced.";
            return;
        }

        GoogleStatus = "Opening your browser…";
        GoogleStatus = await _google.SignInAsync(creds.ClientId, creds.ClientSecret);
        GoogleConnected = _google.IsSignedIn;
        if (GoogleConnected) _engine.NotifyGoogleSignedIn();
    }

    private void GoogleSignOut()
    {
        _google.SignOut();
        RefreshGoogleStatus();
    }

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
        // A device-flow sign-in is preferred; the pasted token still works.
        var typed = GitHubToken.Trim();
        var token = _gitHubAuth.LoadToken() ?? (typed.Length > 0 ? typed : null);
        if (token is null)
        {
            GitHubNote = "Sign in with GitHub, or paste a token below, then scan.";
            return;
        }

        Scanning = true;
        GitHubNote = "Asking GitHub…";
        GitHubRepos.Clear();

        var repos = await _github.GetUserReposAsync(token);
        if (repos is null)
        {
            GitHubNote = "GitHub refused that credential. Check it has repo read access.";
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

    private string _userName;
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

    private string _geminiKey;
    public string GeminiKey { get => _geminiKey; set => SetProperty(ref _geminiKey, value); }

    private string _anthropicKey;
    public string AnthropicKey { get => _anthropicKey; set => SetProperty(ref _anthropicKey, value); }

    private string _openAiKey;
    public string OpenAiKey { get => _openAiKey; set => SetProperty(ref _openAiKey, value); }

    public string[] AiProviders { get; } = Enum.GetNames<Performa.Prefs.AiProvider>();

    /// <summary>
    /// Which vendor answers. Each provider keeps its own key, so switching back
    /// and forth never costs a re-paste; only the selected one's field is shown.
    /// </summary>
    private string _aiProvider;
    public string AiProvider
    {
        get => _aiProvider;
        set
        {
            if (!SetProperty(ref _aiProvider, value)) return;
            OnPropertyChanged(nameof(IsGemini));
            OnPropertyChanged(nameof(IsClaude));
            OnPropertyChanged(nameof(IsOpenAi));
            OnPropertyChanged(nameof(AiKeyHint));
        }
    }

    public bool IsGemini => _aiProvider == nameof(Performa.Prefs.AiProvider.Gemini);
    public bool IsClaude => _aiProvider == nameof(Performa.Prefs.AiProvider.Claude);
    public bool IsOpenAi => _aiProvider == nameof(Performa.Prefs.AiProvider.OpenAi);

    /// <summary>Where to get a key for whichever provider is selected.</summary>
    public string AiKeyHint =>
        "Key from " + AiService.KeyUrlOf(Enum.Parse<Performa.Prefs.AiProvider>(_aiProvider));

    private bool _aiEnabled;
    public bool AiEnabled { get => _aiEnabled; set => SetProperty(ref _aiEnabled, value); }

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

    /// <summary>
    /// Applies immediately rather than on Save. A theme you cannot see until you
    /// press a button is a theme you cannot judge.
    /// </summary>
    public bool IsLightTheme
    {
        get => _engine.Prefs.Theme == AppTheme.Light;
        set
        {
            var theme = value ? AppTheme.Light : AppTheme.Dark;
            if (_engine.Prefs.Theme == theme) return;
            _engine.Prefs.Theme = theme;
            _engine.SavePrefs();
            App.ApplyTheme(theme);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeLabel));
        }
    }

    public string ThemeLabel => IsLightTheme ? "Paper" : "Carbon";

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
        _engine.Prefs.UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim();
        _engine.Prefs.GeminiApiKey = string.IsNullOrWhiteSpace(GeminiKey) ? null : GeminiKey.Trim();
        _engine.Prefs.AnthropicApiKey =
            string.IsNullOrWhiteSpace(AnthropicKey) ? null : AnthropicKey.Trim();
        _engine.Prefs.OpenAiApiKey =
            string.IsNullOrWhiteSpace(OpenAiKey) ? null : OpenAiKey.Trim();
        _engine.Prefs.AiProvider = Enum.Parse<Performa.Prefs.AiProvider>(AiProvider);
        _engine.Prefs.AiEnabled = AiEnabled;
        _engine.Prefs.GitHubToken = string.IsNullOrWhiteSpace(GitHubToken) ? null : GitHubToken.Trim();
        _engine.Prefs.GitHubClientId =
            string.IsNullOrWhiteSpace(GitHubClientId) ? null : GitHubClientId.Trim();
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
