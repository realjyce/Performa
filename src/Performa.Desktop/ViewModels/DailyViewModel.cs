using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class TaskRow : ObservableObject
{
    private bool _done;
    public string Text { get; init; } = "";
    public bool Done
    {
        get => _done;
        set { if (SetProperty(ref _done, value)) Changed?.Invoke(); }
    }

    public Action? Changed;
}

public sealed class TimelineRow(string time, string repo, string subject)
{
    public string Time { get; } = time;
    public string Repo { get; } = repo;
    public string Subject { get; } = subject;
}

public sealed class DailyViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;
    private readonly DailyStore _store = new();
    private bool _loaded;

    public DailyViewModel(PerformaEngine engine)
    {
        _engine = engine;
        AddTaskCommand = new RelayCommand(AddTask);

        var data = _store.Load();
        foreach (var t in data.Tasks) Tasks.Add(Wrap(t.Text, t.Done));
        _notes = data.Notes;
        Today = DateTimeOffset.Now.ToString("dddd, d MMMM");
        _loaded = true;

        _ = LoadTimelineAsync();
    }

    public string Today { get; }

    public ObservableCollection<TaskRow> Tasks { get; } = [];
    public ObservableCollection<TimelineRow> Timeline { get; } = [];

    private string _newTaskText = "";
    public string NewTaskText { get => _newTaskText; set => SetProperty(ref _newTaskText, value); }

    private string _notes;
    public string Notes
    {
        get => _notes;
        set { if (SetProperty(ref _notes, value) && _loaded) Save(); }
    }

    private bool _timelineLoading = true;
    public bool TimelineLoading { get => _timelineLoading; set => SetProperty(ref _timelineLoading, value); }

    private string _timelineEmpty = "";
    public string TimelineEmpty { get => _timelineEmpty; set => SetProperty(ref _timelineEmpty, value); }

    public RelayCommand AddTaskCommand { get; }

    private TaskRow Wrap(string text, bool done)
    {
        var row = new TaskRow { Text = text, Done = done };
        row.Changed = Save;
        return row;
    }

    private void AddTask()
    {
        var text = NewTaskText.Trim();
        if (text.Length == 0) return;
        Tasks.Add(Wrap(text, false));
        NewTaskText = "";
        Save();
    }

    private void Save()
    {
        _store.Save(new DailyData
        {
            Tasks = [.. Tasks.Select(t => new DailyTask { Text = t.Text, Done = t.Done })],
            Notes = Notes,
        });
    }

    private async Task LoadTimelineAsync()
    {
        TimelineLoading = true;
        var commits = await Task.Run(_engine.TodayCommits);

        Timeline.Clear();
        foreach (var (repo, when, subject) in commits)
            Timeline.Add(new TimelineRow(when.ToString("HH:mm"), repo, subject));

        TimelineEmpty = commits.Count == 0 ? "No commits yet today." : "";
        TimelineLoading = false;
    }
}
