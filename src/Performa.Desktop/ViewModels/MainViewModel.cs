using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public PerformaEngine Engine { get; } = new();

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<NavItem> UtilityItems { get; }

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

    public MainViewModel()
    {
        var dashboard = new DashboardViewModel(Engine);
        var daily = new DailyViewModel(Engine);
        var reports = new ReportsViewModel(Engine);
        var loose = new LooseEndsViewModel(Engine);
        var streams = new StreamsViewModel();
        var assistant = new AssistantViewModel(Engine);
        var settings = new SettingsViewModel(Engine);

        NavItems =
        [
            new NavItem("Dashboard", "IconDashboard", dashboard),
            new NavItem("Daily", "IconDaily", daily),
            new NavItem("Reports", "IconReports", reports),
            new NavItem("Loose Ends", "IconLoose", loose),
            new NavItem("Assistant", "IconAssistant", assistant),
            new NavItem("Streams", "IconStreams", streams, dormant: true),
        ];
        UtilityItems =
        [
            new NavItem("Settings", "IconSettings", settings),
        ];

        _selected = NavItems[0];
    }
}
