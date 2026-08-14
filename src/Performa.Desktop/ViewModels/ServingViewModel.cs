using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

/// <summary>One task as the Serving page presents it: the text, why it sits
/// where it does, and whether it has been pushed often enough to say so.</summary>
public sealed class ServingRow(DailyTask task, Urgency urgency) : ObservableObject
{
    public DailyTask Task { get; } = task;
    public string Text => Task.Text;
    public Urgency Urgency { get; } = urgency;

    public string Why => Urgency switch
    {
        Urgency.Overdue => Task.Due is null ? "overdue" : $"was due {Task.Due}",
        Urgency.Today => "due today",
        Urgency.Soon => $"due {Task.Due}",
        _ => "no date",
    };

    /// <summary>Only the overdue and today bands are worth colouring. Painting
    /// every row by urgency turns the list into a value-ramp where nothing
    /// stands out, which is the opposite of what a priority list is for.</summary>
    public bool IsPressing => Urgency is Urgency.Overdue or Urgency.Today;

    public bool IsStale => Serving.IsStale(Task);
    public string StaleNote => $"pushed {Task.Deferred} times";
}

/// <summary>
/// The Serving page: what to start, and how long you have before something
/// interrupts you.
///
/// The order is computed by <see cref="Serving"/> from due dates. The model is
/// handed that order and asked to say it in a sentence; it is never asked what
/// the order should be. If the model is off or fails, the deterministic
/// sentence stands on its own, so the page is never blank and never waiting.
/// </summary>
public sealed class ServingViewModel : ObservableObject, IActivatablePage
{
    private readonly PerformaEngine _engine;
    private readonly DailyStore _store = new();
    private readonly GoogleAuthService _auth = new();
    private readonly GoogleCalendarService _calendar = new();
    private readonly AiService _ai = new();

    public ObservableCollection<ServingRow> Rows { get; } = [];

    public ServingViewModel(PerformaEngine engine)
    {
        _engine = engine;
        DeferCommand = new RelayCommand<ServingRow>(Defer);
        DoneCommand = new RelayCommand<ServingRow>(MarkDone);
        engine.DailyChanged += Load;

        // This is the landing page, and MainViewModel seeds its selection by
        // assigning the field rather than the property, so OnActivated does not
        // run for whichever page is first. Load here or the app opens on a
        // blank version of the one screen meant to answer a question.
        Load();
    }

    public RelayCommand<ServingRow> DeferCommand { get; }
    public RelayCommand<ServingRow> DoneCommand { get; }

    private string _headline = "";
    /// <summary>The one sentence at the top: what to start with, and why.</summary>
    public string Headline { get => _headline; private set => SetProperty(ref _headline, value); }

    private string _window = "";
    /// <summary>How much clear time is left before the next interruption.</summary>
    public string Window { get => _window; private set => SetProperty(ref _window, value); }

    private string _source = "";
    /// <summary>Which model phrased the headline, or that nothing did. Generated
    /// prose always says where it came from.</summary>
    public string Source { get => _source; private set => SetProperty(ref _source, value); }

    public bool HasRows => Rows.Count > 0;

    public void OnActivated()
    {
        Load();
        _ = PhraseAsync();
    }

    private void Load()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var data = _store.Load();

        Rows.Clear();
        foreach (var task in Serving.Rank(data.Tasks, today))
        {
            if (task.Done) continue;
            Rows.Add(new ServingRow(task, Serving.UrgencyOf(task, today)));
        }

        OnPropertyChanged(nameof(HasRows));
        Headline = PlainHeadline();
        Source = "";
    }

    /// <summary>
    /// The headline with no model involved. This is the real answer; the model
    /// only ever rewords it. Written first so the page is complete before any
    /// network call, and so a failed call costs nothing.
    /// </summary>
    private string PlainHeadline()
    {
        if (Rows.Count == 0) return "Nothing waiting. The list is clear.";

        var first = Rows[0];
        if (first.IsStale)
            return $"\"{first.Text}\" is still top of the list after {first.Task.Deferred} pushes. "
                   + "Worth doing, dropping, or breaking into something smaller.";

        var overdue = Rows.Count(r => r.Urgency == Urgency.Overdue);
        var lead = overdue > 1
            ? $"{overdue} things are overdue. Start with \"{first.Text}\"."
            : $"Start with \"{first.Text}\" ({first.Why}).";

        return lead;
    }

    /// <summary>
    /// Asks the model to say the same thing better. It is given the order and
    /// the facts and told to keep them; if it declines or errors, the plain
    /// sentence written by <see cref="PlainHeadline"/> stays on screen.
    /// </summary>
    private async Task PhraseAsync()
    {
        await LoadWindowAsync();
        if (Rows.Count == 0) return;

        var key = AppCredentialStore.AiKey(_engine.Prefs, _engine.Prefs.AiProvider);
        if (!_engine.Prefs.AiEnabled || string.IsNullOrWhiteSpace(key)) return;

        var list = string.Join("; ", Rows.Take(5).Select(r => $"{r.Text} ({r.Why})"));
        var answer = await _ai.AskAsync(_engine.Prefs,
            $"The user is {_engine.Prefs.UserName ?? "the developer"}. "
            + $"Their tasks in priority order: {list}. "
            + (Window.Length > 0 ? $"Clear time: {Window}. " : "No calendar data. "),
            "In two sentences, tell them what to start with and why. Keep the order given, "
            + "do not reorder or invent tasks, do not add encouragement. Calm and concrete.");

        if (answer is null)
        {
            // Say so rather than let the plain sentence pass as the model's.
            Source = "not phrased, model unavailable";
            return;
        }

        Headline = answer.Text;
        Source = answer.Model;
    }

    private async Task LoadWindowAsync()
    {
        if (!_auth.IsSignedIn) { Window = ""; return; }
        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) { Window = ""; return; }

        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null) { Window = ""; return; }

        var events = await _calendar.GetUpcomingAsync(token, days: 1);
        var now = DateTimeOffset.Now;
        var endOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset);
        if (endOfDay <= now) { Window = "The working day is done."; return; }

        var longest = Serving.LongestBlock(Serving.FreeBlocks(events, now, endOfDay));
        Window = longest is null
            ? "No clear stretch left today."
            : $"{Describe(longest.Value.Length)} clear from {longest.Value.Start:HH:mm}.";
    }

    private static string Describe(TimeSpan span)
        => span.TotalMinutes < 60
            ? $"{(int)span.TotalMinutes} min"
            : span.Minutes == 0
                ? $"{(int)span.TotalHours}h"
                : $"{(int)span.TotalHours}h {span.Minutes}m";

    /// <summary>Pushes a task to tomorrow and counts the push. Deferring is the
    /// point of the page as much as starting is: a list you can only add to
    /// stops being a plan and becomes a pile.</summary>
    private void Defer(ServingRow? row)
    {
        if (row is null) return;
        var data = _store.Load();
        // ponytail: matched on text because DailyTask has no id; two tasks
        // worded identically would defer the first. Add an id if that bites.
        var match = data.Tasks.FirstOrDefault(t => t.Text == row.Text && !t.Done);
        if (match is null) return;

        var from = DateOnly.TryParse(match.Due, out var due) && due > DateOnly.FromDateTime(DateTime.Now)
            ? due
            : DateOnly.FromDateTime(DateTime.Now);
        match.Due = from.AddDays(1).ToString("yyyy-MM-dd");
        match.Deferred++;
        _store.Save(data);
        Load();
    }

    private void MarkDone(ServingRow? row)
    {
        if (row is null) return;
        var data = _store.Load();
        var match = data.Tasks.FirstOrDefault(t => t.Text == row.Text && !t.Done);
        if (match is null) return;

        match.Done = true;
        _store.Save(data);
        _engine.NotifyDailyChanged();
        Load();
    }
}
