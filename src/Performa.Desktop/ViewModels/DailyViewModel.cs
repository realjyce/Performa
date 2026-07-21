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

public sealed class EventCard(CalendarEvent e)
{
    public string Title { get; } = e.Title;
    public string Colour { get; } = e.ColourHex;
    public string Calendar { get; } = e.CalendarName;
    public string? Location { get; } = e.Location;
    public bool HasLocation { get; } = !string.IsNullOrWhiteSpace(e.Location);

    public string Day { get; } = e.Start is { } s
        ? (s.Date == DateTimeOffset.Now.Date ? "Today"
            : s.Date == DateTimeOffset.Now.Date.AddDays(1) ? "Tomorrow"
            : s.ToString("ddd d MMM"))
        : "";

    public string Time { get; } = e.AllDay
        ? "All day"
        : e.Start is { } st
            ? (e.End is { } en ? $"{st:HH:mm} – {en:HH:mm}" : st.ToString("HH:mm"))
            : "";
}

/// <summary>
/// The day in one place: what you committed, what's on your calendar, what you
/// still owe yourself. Calendar lives here rather than in its own page so
/// there's a single answer to "what does today look like".
/// </summary>
public sealed class DailyViewModel : ObservableObject, IActivatablePage
{
    private readonly PerformaEngine _engine;
    private readonly DailyStore _store = new();
    private readonly GoogleAuthService _auth = new();
    private readonly GoogleCalendarService _calendar = new();
    private bool _loaded;
    private bool _calendarLoaded;

    public DailyViewModel(PerformaEngine engine)
    {
        _engine = engine;
        AddTaskCommand = new RelayCommand(AddTask);
        RefreshCalendarCommand = new RelayCommand(() => _ = LoadCalendarAsync(force: true));

        var data = _store.Load();
        foreach (var t in data.Tasks) Tasks.Add(Wrap(t.Text, t.Done));
        _notes = data.Notes;
        Today = DateTimeOffset.Now.ToString("dddd, d MMMM");
        _loaded = true;

        engine.GoogleSignedIn += () => _ = LoadCalendarAsync(force: true);

        _ = LoadTimelineAsync();
        _ = LoadCalendarAsync(force: false);
    }

    public string Today { get; }

    public ObservableCollection<TaskRow> Tasks { get; } = [];
    public ObservableCollection<TimelineRow> Timeline { get; } = [];
    public ObservableCollection<EventCard> Events { get; } = [];

    private string _newTaskText = "";
    public string NewTaskText { get => _newTaskText; set => SetProperty(ref _newTaskText, value); }

    private string _notes;
    public string Notes
    {
        get => _notes;
        set { if (SetProperty(ref _notes, value) && _loaded) Save(); }
    }

    private string _timelineEmpty = "";
    public string TimelineEmpty { get => _timelineEmpty; set => SetProperty(ref _timelineEmpty, value); }

    private string _calendarStatus = "";
    public string CalendarStatus { get => _calendarStatus; set => SetProperty(ref _calendarStatus, value); }

    private bool _googleConnected;
    public bool GoogleConnected { get => _googleConnected; set => SetProperty(ref _googleConnected, value); }

    public RelayCommand AddTaskCommand { get; }
    public RelayCommand RefreshCalendarCommand { get; }

    /// <summary>Navigating here re-checks sign-in, so the calendar fills itself in.</summary>
    public void OnActivated()
    {
        _ = LoadTimelineAsync();
        if (!_calendarLoaded && _auth.IsSignedIn) _ = LoadCalendarAsync(force: true);
    }

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
        => _store.Save(new DailyData
        {
            Tasks = [.. Tasks.Select(t => new DailyTask { Text = t.Text, Done = t.Done })],
            Notes = Notes,
        });

    private async Task LoadTimelineAsync()
    {
        var commits = await Task.Run(_engine.TodayCommits);
        Timeline.Clear();
        foreach (var (repo, when, subject) in commits)
            Timeline.Add(new TimelineRow(when.ToString("HH:mm"), repo, subject));
        TimelineEmpty = commits.Count == 0 ? "No commits yet today." : "";
    }

    private async Task LoadCalendarAsync(bool force)
    {
        GoogleConnected = _auth.IsSignedIn;
        if (!GoogleConnected)
        {
            CalendarStatus = "Connect Google in Settings to see your schedule.";
            Events.Clear();
            _calendarLoaded = false;
            return;
        }
        if (_calendarLoaded && !force) return;

        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) { CalendarStatus = "No Google client configured."; return; }

        CalendarStatus = "Reading your calendar…";
        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null)
        {
            CalendarStatus = "Google sign-in expired. Reconnect in Settings.";
            GoogleConnected = false;
            return;
        }

        var events = await _calendar.GetUpcomingAsync(token);
        Events.Clear();
        foreach (var e in events) Events.Add(new EventCard(e));

        _calendarLoaded = true;
        CalendarStatus = events.Count == 0
            ? "Nothing scheduled in the next seven days."
            : "";
    }
}
