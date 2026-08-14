using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
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

    /// <summary>Carried through untouched so that saving from Daily does not
    /// erase what Serving sorts on. Daily does not show either of these; it
    /// only has to avoid destroying them.</summary>
    public string? Due { get; init; }
    public int Deferred { get; init; }

    /// <summary>When there is a date, say so on the row. A task that sorts to
    /// the top of Serving for a reason invisible on Daily reads as arbitrary.</summary>
    public string DueLabel => Due is null ? "" : $"due {Due}";
    public bool HasDue => Due is not null;

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
    private readonly AiService _ai = new();
    private bool _loaded;
    private bool _calendarLoaded;
    private readonly DispatcherTimer _timer;

    public DailyViewModel(PerformaEngine engine)
    {
        _engine = engine;
        AddTaskCommand = new RelayCommand(AddTask);
        RefreshCalendarCommand = new RelayCommand(() => _ = LoadCalendarAsync(force: true));

        var data = _store.Load();
        foreach (var t in data.Tasks) Tasks.Add(Wrap(t.Text, t.Done, t.Due, t.Deferred));
        _notes = data.Notes;
        Today = DateTimeOffset.Now.ToString("dddd, d MMMM");
        _loaded = true;

        engine.GoogleSignedIn += () => _ = LoadCalendarAsync(force: true);
        engine.DailyChanged += LoadAutomationSurfaces;
        AcceptSuggestionCommand = new RelayCommand<string>(AcceptSuggestion);
        DismissSuggestionCommand = new RelayCommand<string>(DismissSuggestion);
        LoadAutomationSurfaces();
        RefreshTaskMeter();

        // Calendars change slowly; five minutes keeps it fresh without
        // hammering the API or the battery.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += (_, _) => { _ = LoadTimelineAsync(); _ = RefreshAsync(force: true); };
        _timer.Start();

        _ = LoadTimelineAsync();
        _ = RefreshAsync(force: false);
    }

    /// <summary>Calendar first, then the brief, so the brief can count events.</summary>
    private async Task RefreshAsync(bool force)
    {
        await LoadCalendarAsync(force);
        await BuildBriefAsync();
    }

    public string Today { get; }

    /// <summary>Greets by clock, not a canned string. Static so the brief can
    /// greet the same way rather than keeping its own copy of the hours.</summary>
    public static string Greeting(int hour, string? name)
    {
        var part = hour switch
        {
            < 5 => "Up late",
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };
        return string.IsNullOrWhiteSpace(name) ? part : $"{part}, {name}";
    }

    public string Salutation => Greeting(DateTimeOffset.Now.Hour, _engine.Prefs.UserName);

    public ObservableCollection<string> Suggestions { get; } = [];

    public RelayCommand<string> AcceptSuggestionCommand { get; }
    public RelayCommand<string> DismissSuggestionCommand { get; }

    private string _closeout = "";
    public string Closeout
    {
        get => _closeout;
        set { if (SetProperty(ref _closeout, value)) OnPropertyChanged(nameof(HasCloseout)); }
    }

    public bool HasCloseout => _closeout.Length > 0;

    private string _closeoutStamp = "";
    public string CloseoutStamp { get => _closeoutStamp; set => SetProperty(ref _closeoutStamp, value); }

    private bool _hasSuggestions;
    public bool HasSuggestions { get => _hasSuggestions; set => SetProperty(ref _hasSuggestions, value); }

    // A ratio against a limit is a meter, not a two-slice donut: the number is
    // the point and the track just shows how far along it sits.
    public string TaskProgress
    {
        get
        {
            var done = Tasks.Count(t => t.Done);
            return Tasks.Count == 0 ? "" : $"{done} of {Tasks.Count} done";
        }
    }

    /// <summary>Meter fill as a horizontal scale against a full-width track.
    /// A transform rather than a width: animating Width relayouts the panel on
    /// every frame, while a scale is composited.</summary>
    public ITransform TaskMeterFill
        => TransformOperations.Parse(
            Tasks.Count == 0
                ? "scaleX(0)"
                : $"scaleX({Tasks.Count(t => t.Done) / (double)Tasks.Count:0.####})");

    public bool HasTasks => Tasks.Count > 0;

    private void RefreshTaskMeter()
    {
        OnPropertyChanged(nameof(TaskProgress));
        OnPropertyChanged(nameof(TaskMeterFill));
        OnPropertyChanged(nameof(HasTasks));
    }

    /// <summary>Pulls what the automation loop wrote: close-out and harvested
    /// suggestions. Runs at start and whenever the loop signals a change.</summary>
    private void LoadAutomationSurfaces()
    {
        var data = _store.Load();
        // Yesterday's close-out is history, not today's page.
        var isToday = data.CloseoutDate == DateTimeOffset.Now.ToString("yyyy-MM-dd");
        Closeout = isToday ? data.Closeout : "";
        CloseoutStamp = isToday ? data.CloseoutStamp : "";

        Suggestions.Clear();
        foreach (var s in data.Suggested) Suggestions.Add(s);
        HasSuggestions = Suggestions.Count > 0;
    }

    private void AcceptSuggestion(string? text)
    {
        if (text is null) return;
        Tasks.Add(Wrap(text, false));
        RemoveSuggestion(text, dismiss: false);
        Save();
    }

    private void DismissSuggestion(string? text)
    {
        if (text is null) return;
        RemoveSuggestion(text, dismiss: true);
    }

    private void RemoveSuggestion(string text, bool dismiss)
    {
        Suggestions.Remove(text);
        HasSuggestions = Suggestions.Count > 0;
        var data = _store.Load();
        data.Suggested.Remove(text);
        if (dismiss) data.Dismissed.Add(text);
        _store.Save(data);
    }

    private string _brief = "";
    public string Brief
    {
        get => _brief;
        set { if (SetProperty(ref _brief, value)) OnPropertyChanged(nameof(HasBrief)); }
    }

    public bool HasBrief => _brief.Length > 0;

    private string _briefSource = "";
    public string BriefSource { get => _briefSource; set => SetProperty(ref _briefSource, value); }

    /// <summary>
    /// One paragraph that reads the whole day: schedule, tasks, code, streak.
    /// The facts are computed first and are the fallback; a model only ever
    /// rewrites them into something warmer.
    /// </summary>
    private async Task BuildBriefAsync()
    {
        var commits = await Task.Run(_engine.TodayCommits);
        var openTasks = Tasks.Count(t => !t.Done);

        // All-day entries are birthdays and reminders, not meetings; counting
        // them together produced "2 meetings (first at All day)".
        var timed = Events.Where(e => e.Day == "Today" && e.Time != "All day").ToList();
        var allDay = Events.Count(e => e.Day == "Today" && e.Time == "All day");
        var todayEvents = timed.Count;
        var nextEvent = timed.FirstOrDefault();

        var streak = 0;
        try
        {
            streak = await Task.Run(() => _engine.BuildWorkspace().Velocity.StreakDays);
        }
        catch (Exception) { /* no workspace yet; the brief just skips it */ }

        // The deterministic sentence: always available, never wrong.
        var bits = new List<string>();
        bits.Add(todayEvents switch
        {
            0 when allDay > 0 => $"no meetings but {allDay} all-day item(s)",
            0 => "a clear calendar",
            1 => $"one meeting at {nextEvent?.Time}",
            _ => $"{todayEvents} meetings, first at {nextEvent?.Time}",
        });
        if (openTasks > 0) bits.Add($"{openTasks} task(s) still open");
        bits.Add(commits.Count switch
        {
            0 => "no commits yet",
            1 => "one commit down",
            _ => $"{commits.Count} commits down",
        });
        if (streak > 1) bits.Add($"a {streak}-day streak going");
        var facts = $"Today: {string.Join(", ", bits)}.";

        Brief = facts;
        BriefSource = "";

        var context =
            $"You are Performa, a developer's chief of staff. The user is {_engine.Prefs.UserName ?? "the developer"}. "
            + $"It is {DateTimeOffset.Now:dddd HH:mm}. Facts about today:\n"
            + $"- Meetings today: {todayEvents}"
            + (nextEvent is null ? "" : $", next: \"{nextEvent.Title}\" at {nextEvent.Time}") + "\n"
            + $"- Open tasks: {openTasks}\n"
            + $"- Commits so far today: {commits.Count}\n"
            + (commits.Count > 0 ? $"- Latest commit: {commits[0].Subject}\n" : "")
            + $"- Coding streak: {streak} day(s)";

        var answer = await _ai.AskAsync(_engine.Prefs, context,
            "Write a two-sentence brief of my day. Warm, concrete, a little wry; no bullet "
            + "points, no exclamation marks, and use only the facts above.");
        if (answer is not null)
        {
            Brief = answer.Text;
            BriefSource = answer.Model;
        }
    }

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

    private bool _isRefreshing;
    public bool IsRefreshing { get => _isRefreshing; set => SetProperty(ref _isRefreshing, value); }

    /// <summary>When the schedule last actually loaded, so a quiet auto-refresh
    /// is visible and stale data is never mistaken for fresh.</summary>
    private string _lastRefreshed = "";
    public string LastRefreshed { get => _lastRefreshed; set => SetProperty(ref _lastRefreshed, value); }

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
        OnPropertyChanged(nameof(Salutation));
        if (!_calendarLoaded && _auth.IsSignedIn) _ = RefreshAsync(force: true);
        else if (!HasBrief) _ = BuildBriefAsync();
    }

    private TaskRow Wrap(string text, bool done, string? due = null, int deferred = 0)
    {
        var row = new TaskRow { Text = text, Done = done, Due = due, Deferred = deferred };
        row.Changed = Save;
        return row;
    }

    private void AddTask()
    {
        var raw = NewTaskText.Trim();
        if (raw.Length == 0) return;

        // The date is read out of what was typed rather than picked from a
        // control: you are already writing the sentence, and "friday" is
        // fewer moves than opening a calendar to find Friday.
        var (text, due) = Serving.SplitDue(raw, DateOnly.FromDateTime(DateTime.Now));
        if (text.Length == 0) return;

        Tasks.Add(Wrap(text, false, due));
        NewTaskText = "";
        Save();
    }

    /// <summary>
    /// Read-modify-write, never build-fresh: the automation loop owns the
    /// close-out and the suggestion lists in this same file, and constructing a
    /// new DailyData here would silently erase its work every time a task was
    /// ticked.
    /// </summary>
    private void Save()
    {
        var data = _store.Load();
        data.Tasks = [.. Tasks.Select(t => new DailyTask
        {
            Text = t.Text,
            Done = t.Done,
            // Daily does not edit these, so it has no business dropping them.
            // Rebuilding the row from Text and Done alone erased every due date
            // the moment a checkbox moved.
            Due = t.Due,
            Deferred = t.Deferred,
        })];
        data.Notes = Notes;
        _store.Save(data);
        RefreshTaskMeter();
    }

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

        IsRefreshing = true;
        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null)
        {
            CalendarStatus = "Google sign-in expired. Reconnect in Settings.";
            GoogleConnected = false;
            IsRefreshing = false;
            return;
        }

        var events = await _calendar.GetUpcomingAsync(token);
        Events.Clear();
        foreach (var e in events) Events.Add(new EventCard(e));

        _calendarLoaded = true;
        IsRefreshing = false;
        LastRefreshed = $"Updated {DateTimeOffset.Now:HH:mm}";
        CalendarStatus = events.Count == 0
            ? "Nothing scheduled in the next seven days."
            : "";
    }
}
