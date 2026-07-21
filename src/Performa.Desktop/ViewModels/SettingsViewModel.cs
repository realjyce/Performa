using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;
using Performa.Prefs;

namespace Performa.Desktop.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

    public SettingsViewModel(PerformaEngine engine)
    {
        _engine = engine;
        _workspacePath = engine.WorkspacePath ?? "";
        _verbosity = engine.Prefs.Verbosity.ToString();
        _grouping = engine.Prefs.Grouping.ToString();
        _tone = engine.Prefs.Tone.ToString();
        SaveCommand = new RelayCommand(Save);
    }

    private string _workspacePath;
    public string WorkspacePath
    {
        get => _workspacePath;
        set => SetProperty(ref _workspacePath, value);
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

    private void Save()
    {
        if (Directory.Exists(WorkspacePath))
            _engine.Prefs.WorkspacePath = WorkspacePath;
        _engine.Prefs.Verbosity = Enum.Parse<Performa.Prefs.Verbosity>(_verbosity);
        _engine.Prefs.Grouping = Enum.Parse<Performa.Prefs.Grouping>(_grouping);
        _engine.Prefs.Tone = Enum.Parse<Performa.Prefs.Tone>(_tone);
        _engine.Prefs.Initialised = true;
        _engine.SavePrefs();
        SavedNote = Directory.Exists(WorkspacePath)
            ? "Saved. Reopen a page to see the change."
            : "Saved preferences. Workspace path not found, left unchanged.";
    }
}
