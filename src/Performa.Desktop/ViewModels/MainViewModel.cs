using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public PerformaEngine Engine { get; } = new();

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<NavItem> UtilityItems { get; }

    private readonly ReportsViewModel _reports;
    private readonly NavItem _reportsNav;
    private readonly NavItem _looseNav;
    private NavItem _assistantNav = null!;

    private NavItem _selected = null!;
    public NavItem Selected
    {
        get => _selected;
        set
        {
            // Two sidebar lists bind to this one property; the non-owning list
            // clears itself and writes null back. Ignore that so the real
            // selection survives.
            if (value is null) return;
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(CurrentPage));
                (value.Page as IActivatablePage)?.OnActivated();
            }
        }
    }

    public ObservableObject CurrentPage => Selected.Page;

    public ConsoleViewModel Console { get; }

    // Launch walkthrough: name first, then offer the sign-ins, once only.
    private readonly GitHubAuthService _gitHubAuth = new();
    private readonly GoogleAuthService _googleAuth = new();

    private bool _showOnboarding;
    public bool ShowOnboarding { get => _showOnboarding; set => SetProperty(ref _showOnboarding, value); }

    private bool _needsName;
    public bool NeedsName { get => _needsName; set => SetProperty(ref _needsName, value); }

    public bool ShowConnectStep => _showOnboarding && !_needsName;

    private string _gitHubDeviceCode = "";
    public string GitHubDeviceCode
    {
        get => _gitHubDeviceCode;
        set { if (SetProperty(ref _gitHubDeviceCode, value)) OnPropertyChanged(nameof(HasDeviceCode)); }
    }
    public bool HasDeviceCode => _gitHubDeviceCode.Length > 0;

    private string _gitHubStatus = "Not connected";
    public string GitHubStatus { get => _gitHubStatus; set => SetProperty(ref _gitHubStatus, value); }

    private bool _gitHubConnected;
    public bool GitHubConnected { get => _gitHubConnected; set => SetProperty(ref _gitHubConnected, value); }

    private string _googleStatusText = "Not connected";
    public string GoogleStatusText { get => _googleStatusText; set => SetProperty(ref _googleStatusText, value); }

    private bool _googleConnected;
    public bool GoogleConnected { get => _googleConnected; set => SetProperty(ref _googleConnected, value); }

    public RelayCommand ConnectGitHubCommand { get; }
    public RelayCommand ConnectGoogleCommand { get; }
    public RelayCommand FinishOnboardingCommand { get; }

    /// <summary>Device flow at launch: show a code, poll until GitHub approves.</summary>
    private async Task ConnectGitHubAsync()
    {
        var clientId = AppCredentialStore.GitHubClientId(Engine.Prefs);
        if (clientId is null)
        {
            GitHubStatus = "No GitHub client configured. Add one in Settings.";
            return;
        }

        GitHubStatus = "Asking GitHub for a code…";
        var prompt = await _gitHubAuth.StartAsync(clientId);
        if (prompt is null)
        {
            GitHubStatus = "GitHub would not start the sign-in.";
            return;
        }

        GitHubDeviceCode = prompt.UserCode;
        GitHubStatus = $"Enter this code at {prompt.VerificationUri}";

        GitHubStatus = await _gitHubAuth.CompleteAsync(clientId);
        GitHubDeviceCode = "";
        GitHubConnected = _gitHubAuth.IsSignedIn;
        if (GitHubConnected) Engine.NotifyGitHubChanged();
    }

    private async Task ConnectGoogleAsync()
    {
        var creds = GoogleCredentialStore.Load(Engine.Prefs);
        if (creds is null)
        {
            GoogleStatusText = "No Google client configured. Add one in Settings.";
            return;
        }
        GoogleStatusText = "Opening your browser…";
        GoogleStatusText = await _googleAuth.SignInAsync(creds.ClientId, creds.ClientSecret);
        GoogleConnected = _googleAuth.IsSignedIn;
        if (GoogleConnected) Engine.NotifyGoogleSignedIn();
    }

    private void FinishOnboarding()
    {
        Engine.Prefs.OnboardingDone = true;
        Engine.SavePrefs();
        ShowOnboarding = false;
        OnPropertyChanged(nameof(ShowConnectStep));
    }

    private string _nameEntry = "";
    public string NameEntry { get => _nameEntry; set => SetProperty(ref _nameEntry, value); }

    private string _greeting = "";
    public string Greeting { get => _greeting; set => SetProperty(ref _greeting, value); }

    public RelayCommand SaveNameCommand { get; }
    public RelayCommand OpenAssistantCommand { get; }

    private void SaveName()
    {
        var name = NameEntry.Trim();
        if (name.Length == 0) return;
        Engine.Prefs.UserName = name;
        Engine.SavePrefs();
        NeedsName = false;
        Greeting = name;
        OnPropertyChanged(nameof(ShowConnectStep));
    }

    private bool _isConsoleOpen;
    public bool IsConsoleOpen
    {
        get => _isConsoleOpen;
        set => SetProperty(ref _isConsoleOpen, value);
    }

    public RelayCommand ToggleConsoleCommand { get; }

    public MainViewModel()
    {
        Console = new ConsoleViewModel(Engine);
        ToggleConsoleCommand = new RelayCommand(() => IsConsoleOpen = !IsConsoleOpen);
        SaveNameCommand = new RelayCommand(SaveName);
        ConnectGitHubCommand = new RelayCommand(() => _ = ConnectGitHubAsync());
        ConnectGoogleCommand = new RelayCommand(() => _ = ConnectGoogleAsync());
        FinishOnboardingCommand = new RelayCommand(FinishOnboarding);
        _gitHubConnected = _gitHubAuth.IsSignedIn;
        _googleConnected = _googleAuth.IsSignedIn;
        if (_gitHubConnected) _gitHubStatus = "Connected";
        if (_googleConnected) _googleStatusText = "Connected";
        _showOnboarding = !Engine.Prefs.OnboardingDone;
        OpenAssistantCommand = new RelayCommand(() => Selected = _assistantNav);
        _needsName = string.IsNullOrWhiteSpace(Engine.Prefs.UserName);
        _greeting = Engine.Prefs.UserName ?? "";

        var dashboard = new DashboardViewModel(Engine, HandleQuickAction);
        var daily = new DailyViewModel(Engine);
        _reports = new ReportsViewModel(Engine);
        var loose = new LooseEndsViewModel(Engine);
        var inbox = new InboxViewModel(Engine);
        var streams = new StreamsViewModel();
        var assistant = new AssistantViewModel(Engine);
        var settings = new SettingsViewModel(Engine);

        _reportsNav = new NavItem("Reports", "IconReports", _reports);
        _looseNav = new NavItem("Loose Ends", "IconLoose", loose);

        NavItems =
        [
            new NavItem("Dashboard", "IconDashboard", dashboard),
            new NavItem("Daily", "IconDaily", daily),
            new NavItem("Inbox", "IconStreams", inbox),
            _reportsNav,
            _looseNav,
            new NavItem("Streams", "IconStreams", streams, dormant: true),
        ];
        _assistantNav = new NavItem("Assistant", "IconAssistant", assistant);
        UtilityItems = [new NavItem("Settings", "IconSettings", settings)];

        _selected = NavItems[0];
    }

    private void HandleQuickAction(string command)
    {
        if (command == "loose")
        {
            Selected = _looseNav;
            return;
        }

        _reports.SelectedKind = command switch
        {
            "changelog" => "Changelog",
            "summary" => "Summary",
            _ => "Standup",
        };
        Selected = _reportsNav;
        _ = _reports.GenerateAsync();
    }
}
