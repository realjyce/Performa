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

    // Asked once on first run so nothing is silently inherited from an SSO profile.
    private bool _needsName;
    public bool NeedsName { get => _needsName; set => SetProperty(ref _needsName, value); }

    private string _nameEntry = "";
    public string NameEntry { get => _nameEntry; set => SetProperty(ref _nameEntry, value); }

    private string _greeting = "";
    public string Greeting { get => _greeting; set => SetProperty(ref _greeting, value); }

    public RelayCommand SaveNameCommand { get; }

    private void SaveName()
    {
        var name = NameEntry.Trim();
        if (name.Length == 0) return;
        Engine.Prefs.UserName = name;
        Engine.SavePrefs();
        NeedsName = false;
        Greeting = name;
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
        UtilityItems =
        [
            new NavItem("Assistant", "IconAssistant", assistant, featured: true),
            new NavItem("Settings", "IconSettings", settings),
        ];

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
