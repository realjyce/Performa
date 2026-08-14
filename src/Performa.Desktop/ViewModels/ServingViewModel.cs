using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

/// <summary>One task as the Serving page presents it: the text, why it sits
/// where it does, and whether it has been pushed often enough to say so.</summary>
public sealed class ServingRow(ServingCandidate item) : ObservableObject
{
    public ServingCandidate Item { get; } = item;
    public string Text => Item.Title;
    public string Why => Item.Why;
    public ItemKind Kind => Item.Kind;

    /// <summary>Only the top band is worth colouring. Painting every row turns
    /// the list into a ramp, and a ramp has no top.</summary>
    public bool IsPressing => Item.Pressure <= 10;

    public string KindLabel => Item.Kind switch
    {
        ItemKind.Meeting => "MEETING",
        ItemKind.Repo => "CODE",
        ItemKind.Mail => "INBOX",
        _ => "TASK",
    };

    // Only tasks can be ticked, deferred or explained. A meeting happens
    // whether or not you press anything, and Performa never pushes for you.
    public bool IsTask => Item.Kind == ItemKind.Task;
    public bool CanOpen => Item.Kind is ItemKind.Repo or ItemKind.Mail;
    public string OpenLabel => Item.Kind == ItemKind.Repo ? "Open" : "Read";

    private string _steps = "";
    /// <summary>Concrete first moves for this task. Empty until asked for:
    /// generating one of these per row on load would spend a call on every
    /// task to answer a question about one.</summary>
    public string Steps
    {
        get => _steps;
        set { if (SetProperty(ref _steps, value)) OnPropertyChanged(nameof(HasSteps)); }
    }

    public bool HasSteps => _steps.Length > 0;

    private bool _thinking;
    public bool Thinking { get => _thinking; set => SetProperty(ref _thinking, value); }

    private string _stepsSource = "";
    /// <summary>Which model wrote the steps. Generated prose always says so.</summary>
    public string StepsSource
    {
        get => _stepsSource;
        set { if (SetProperty(ref _stepsSource, value)) OnPropertyChanged(nameof(HasStepsSource)); }
    }

    public bool HasStepsSource => _stepsSource.Length > 0;
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
    private readonly GmailService _gmail = new();
    private readonly AiService _ai = new();

    public ObservableCollection<ServingRow> Rows { get; } = [];

    public ServingViewModel(PerformaEngine engine)
    {
        _engine = engine;
        DeferCommand = new RelayCommand<ServingRow>(Defer);
        DoneCommand = new RelayCommand<ServingRow>(MarkDone);
        HowCommand = new RelayCommand<ServingRow>(row => _ = HowAsync(row));
        OpenCommand = new RelayCommand<ServingRow>(Open);
        AskCommand = new RelayCommand(() => _ = AskAsync());
        RefreshCommand = new RelayCommand(() => { Load(); _ = GatherAsync(); });
        engine.DailyChanged += Load;

        // This is the landing page, and MainViewModel seeds its selection by
        // assigning the field rather than the property, so OnActivated does not
        // run for whichever page is first. Draw the tasks here, then let the
        // slower streams fill in, or the app opens on a blank version of the
        // one screen meant to answer a question.
        Load();
        _ = GatherAsync();
    }

    public RelayCommand<ServingRow> DeferCommand { get; }
    public RelayCommand<ServingRow> DoneCommand { get; }
    public RelayCommand<ServingRow> HowCommand { get; }
    public RelayCommand<ServingRow> OpenCommand { get; }
    public RelayCommand AskCommand { get; }
    public RelayCommand RefreshCommand { get; }

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

    private bool _settling;
    /// <summary>True while the model is out and the headline is about to be
    /// replaced. The text dims rather than sitting there looking current, and a
    /// sentence that fades out and back reads as an answer arriving instead of
    /// a glitch.</summary>
    public bool Settling { get => _settling; private set => SetProperty(ref _settling, value); }

    public bool HasRows => Rows.Count > 0;

    public void OnActivated()
    {
        Load();
        _ = GatherAsync();
    }

    /// <summary>
    /// Draws instantly from the one cheap source: the task file. Repositories,
    /// calendar and mail all arrive later and re-rank on their own.
    ///
    /// Deliberately does not scan the working trees. That shells out to git
    /// once per repository, and doing it here would run it on the UI thread
    /// during construction, so the window would not paint until every repo had
    /// answered and a single slow one would hang the app before it opened.
    /// </summary>
    private void Load() => Compose(_store.Load().Tasks, _repos);

    private List<CalendarEvent> _events = [];
    private int _mailAsks;
    private List<RepoState> _repos = [];

    private void Compose(IEnumerable<DailyTask> tasks, List<RepoState> repos)
    {
        _repos = repos;
        var items = Serving.Compose(tasks, _events, repos, _mailAsks, DateTimeOffset.Now);

        Rows.Clear();
        foreach (var item in items) Rows.Add(new ServingRow(item));

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
        if (Rows.Count == 0) return "Nothing waiting. The day is clear.";

        var first = Rows[0];
        return first.Kind switch
        {
            ItemKind.Meeting => $"\"{first.Text}\" {first.Why}. Anything you start now gets interrupted.",
            ItemKind.Repo => $"{first.Text}: {first.Why}. Worth clearing before it grows.",
            ItemKind.Mail => $"{first.Text} {first.Why}.",
            _ => $"Start with \"{first.Text}\" ({first.Why}).",
        };
    }

    /// <summary>
    /// Asks the model to say the same thing better. It is given the order and
    /// the facts and told to keep them; if it declines or errors, the plain
    /// sentence written by <see cref="PlainHeadline"/> stays on screen.
    /// </summary>
    private async Task GatherAsync()
    {
        // Off the UI thread: this is git, once per repository.
        var repos = await Task.Run(() =>
        {
            try
            {
                return _engine.BuildWorkspace().Repos
                    .Select(r => new RepoState(r.Name, r.Path, r.Branch,
                                               r.UncommittedFiles, r.UnpushedCommits))
                    .ToList();
            }
            catch (Exception) { return []; }
        });

        await LoadStreamsAsync();
        // Every stream can outrank what is already drawn, so the list is
        // rebuilt rather than appended to.
        Compose(_store.Load().Tasks, repos);
        await PhraseAsync();
    }

    private async Task PhraseAsync()
    {
        if (Rows.Count == 0) return;

        var key = AppCredentialStore.AiKey(_engine.Prefs, _engine.Prefs.AiProvider);
        if (!_engine.Prefs.AiEnabled || string.IsNullOrWhiteSpace(key)) return;

        Settling = true;
        try
        {
            var list = string.Join("; ", Rows.Take(6).Select(r => $"[{r.KindLabel}] {r.Text} ({r.Why})"));
            var answer = await _ai.AskAsync(_engine.Prefs,
                $"The user is {_engine.Prefs.UserName ?? "the developer"}. "
                + $"Everything wanting their attention, most pressing first: {list}. "
                + (Window.Length > 0 ? $"Clear time: {Window}. " : "No calendar data. "),
                "In two sentences, tell them what to do first and why. Keep the order given, "
                + "do not reorder or invent items, do not add encouragement. If a meeting is "
                + "imminent, say what fits before it. Calm and concrete.");

            if (answer is null)
            {
                // Say so rather than let the plain sentence pass as the model's.
                Source = "not phrased, model unavailable";
                return;
            }

            Headline = answer.Text;
            Source = answer.Model;
        }
        finally { Settling = false; }
    }

    /// <summary>
    /// Asks how to start one task, once, and keeps the answer on the row.
    ///
    /// Grounded in the workspace rather than asked cold: a task naming a repo
    /// the user has uncommitted work in should get steps that mention it. The
    /// model is told to give first moves rather than a plan, because the thing
    /// stopping someone starting is usually not knowing the first move.
    /// </summary>
    private async Task HowAsync(ServingRow? row)
    {
        if (row is null || row.Thinking) return;
        if (row.HasSteps) { row.Steps = ""; row.StepsSource = ""; return; }   // second press closes it

        var key = AppCredentialStore.AiKey(_engine.Prefs, _engine.Prefs.AiProvider);
        if (!_engine.Prefs.AiEnabled || string.IsNullOrWhiteSpace(key))
        {
            row.Steps = "Turn on AI prose in Settings and add a key to get steps here.";
            return;
        }

        row.Thinking = true;
        try
        {
            var context = await Task.Run(() => WorkContext(row.Text));
            var answer = await _ai.AskAsync(_engine.Prefs, context,
                $"The task is: \"{row.Text}\". Give the first two or three concrete moves to "
                + "start it, as short lines beginning with a verb. Name real files, repos or "
                + "commands from the context where they fit. No preamble, no encouragement, "
                + "no restating the task. If the task is unclear, say what to decide first.");

            if (answer is null)
            {
                row.Steps = "The model did not answer. Try again, or start with whatever is smallest.";
                row.StepsSource = "";
                return;
            }

            row.Steps = answer.Text;
            row.StepsSource = answer.Model;
        }
        finally { row.Thinking = false; }
    }

    /// <summary>
    /// What the workspace can say about a task. Only repos with something
    /// outstanding, and only the ones whose name the task actually mentions
    /// when it mentions one, so the steps are about this job rather than a
    /// tour of everything open.
    /// </summary>
    private string WorkContext(string taskText)
    {
        try
        {
            var facts = _engine.BuildWorkspace();
            var named = facts.Repos
                .Where(r => Serving.MentionsRepo(taskText, r.Name))
                .ToList();

            var relevant = named.Count > 0
                ? named
                : [.. facts.Repos.Where(r => r.UncommittedFiles > 0 || r.UnpushedCommits > 0).Take(3)];

            if (relevant.Count == 0) return "No repository has outstanding work.";

            return "Repositories in play: " + string.Join("; ", relevant.Select(r =>
                $"{r.Name} on {r.Branch}, {r.UncommittedFiles} uncommitted, {r.UnpushedCommits} unpushed"));
        }
        catch (Exception ex)
        {
            return $"(Workspace facts unavailable: {ex.Message})";
        }
    }

    /// <summary>
    /// Pulls the two streams that need the network. Both are optional: a page
    /// that will not draw without Google is a page that is blank whenever the
    /// token has expired.
    /// </summary>
    private async Task LoadStreamsAsync()
    {
        if (!_auth.IsSignedIn) { Window = ""; return; }
        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) { Window = ""; return; }

        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null) { Window = ""; return; }

        var events = await _calendar.GetUpcomingAsync(token, days: 1);
        _events = events;

        try
        {
            // Only mail that asks something. A newsletter is not a thing to do,
            // and bulk mail never is however many verbs it contains.
            var mail = await _gmail.GetRecentAsync(token);
            _mailAsks = mail.Count(m => !m.IsBulk && m.Actions.Count > 0);
        }
        catch (Exception) { _mailAsks = 0; }

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
        if (row is null || !row.IsTask) return;
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
        if (row is null || !row.IsTask) return;
        var data = _store.Load();
        var match = data.Tasks.FirstOrDefault(t => t.Text == row.Text && !t.Done);
        if (match is null) return;

        match.Done = true;
        match.DoneAt = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        _store.Save(data);
        _engine.NotifyDailyChanged();
        Load();
    }

    /// <summary>Acts on the row rather than describing it. A repo opens in the
    /// editor; mail hands over to the Inbox page, which already groups by what
    /// each message wants.</summary>
    private void Open(ServingRow? row)
    {
        if (row is null) return;
        if (row.Kind == ItemKind.Repo && row.Item.Payload.Length > 0)
            Launcher.OpenRepo(row.Item.Payload, _engine.Prefs.EditorCommand);
        else if (row.Kind == ItemKind.Mail)
            GoToInbox?.Invoke();
    }

    /// <summary>Raised when the page wants the shell to navigate. Set by
    /// MainViewModel, which owns the nav list; Serving does not reach into it.</summary>
    public Action? GoToInbox;

    // --- asking about the day, without leaving the page ---

    private string _ask = "";
    public string Ask { get => _ask; set => SetProperty(ref _ask, value); }

    private string _answer = "";
    public string Answer
    {
        get => _answer;
        private set { if (SetProperty(ref _answer, value)) OnPropertyChanged(nameof(HasAnswer)); }
    }

    public bool HasAnswer => _answer.Length > 0;

    private bool _asking;
    public bool Asking { get => _asking; private set => SetProperty(ref _asking, value); }

    /// <summary>
    /// A question about what is on this page, answered on this page.
    ///
    /// Separate from the Assistant, which answers about the repositories. This
    /// one is handed the ranked list and the free time, so "what can I finish
    /// before the standup" is answerable without the model guessing at either.
    /// </summary>
    private async Task AskAsync()
    {
        var question = Ask.Trim();
        if (question.Length == 0 || Asking) return;

        var key = AppCredentialStore.AiKey(_engine.Prefs, _engine.Prefs.AiProvider);
        if (!_engine.Prefs.AiEnabled || string.IsNullOrWhiteSpace(key))
        {
            Answer = "Turn on AI prose in Settings and add a key to ask here.";
            return;
        }

        Asking = true;
        Ask = "";
        try
        {
            var list = string.Join("; ", Rows.Select(r => $"[{r.KindLabel}] {r.Text} ({r.Why})"));
            var reply = await _ai.AskAsync(_engine.Prefs,
                $"The user is {_engine.Prefs.UserName ?? "the developer"}. "
                + $"On their plate, most pressing first: {list}. "
                + (Window.Length > 0 ? $"Clear time today: {Window} " : "")
                + "Performa can open repositories and mail but never pushes, sends or replies.",
                $"{question}\n\nAnswer in two or three sentences using only what is listed. "
                + "If the answer is not in there, say so rather than guessing.");

            Answer = reply?.Text ?? "The model did not answer.";
            Source = reply?.Model ?? "";
        }
        finally { Asking = false; }
    }
}
