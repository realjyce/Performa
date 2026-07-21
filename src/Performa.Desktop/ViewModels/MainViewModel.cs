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
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public ObservableObject CurrentPage => Selected.Page;

    public ConsoleViewModel Console { get; }

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

        var dashboard = new DashboardViewModel(Engine, HandleQuickAction);
        var daily = new DailyViewModel(Engine);
        _reports = new ReportsViewModel(Engine);
        var loose = new LooseEndsViewModel(Engine);
        var streams = new StreamsViewModel();
        var assistant = new AssistantViewModel(Engine);
        var settings = new SettingsViewModel(Engine);

        _reportsNav = new NavItem("Reports", "IconReports", _reports);
        _looseNav = new NavItem("Loose Ends", "IconLoose", loose);

        NavItems =
        [
            new NavItem("Dashboard", "IconDashboard", dashboard),
            new NavItem("Daily", "IconDaily", daily),
            _reportsNav,
            _looseNav,
            new NavItem("Assistant", "IconAssistant", assistant),
            new NavItem("Streams", "IconStreams", streams, dormant: true),
        ];
        UtilityItems =
        [
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
