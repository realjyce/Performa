using System.Text.Json;
using Avalonia.Threading;

namespace Performa.Desktop.Services;

/// <summary>
/// The automation spine: one clock, every rule. Performa prepares and the user
/// glances - the brief lands at its hour, meetings announce themselves, the
/// close-out writes itself, email asks turn into suggested tasks.
///
/// Guardrails, deliberately: every rule has a visible switch in Settings, every
/// artifact it writes is stamped as automated, and nothing outbound ever fires.
/// It reads, prepares and notifies; it never sends, pushes or replies.
///
/// One-a-day rules track their last firing in automation.json so a restart at
/// 09:05 does not re-toast a brief the user already saw at 09:00.
/// </summary>
public sealed class AutomationService
{
    private readonly PerformaEngine _engine;
    private readonly GoogleAuthService _auth = new();
    private readonly GoogleCalendarService _calendar = new();
    private readonly GmailService _gmail = new();
    private readonly AiService _ai = new();
    private readonly DailyStore _store = new();
    private readonly DispatcherTimer _timer;
    private readonly string _statePath;
    private readonly string _logPath;

    // In-memory, per-run: which events were announced, when mail/calendar were
    // last pulled, and the cached upcoming events between pulls.
    private readonly HashSet<string> _announced = [];
    private List<CalendarEvent> _events = [];
    private DateTimeOffset _eventsFetched = DateTimeOffset.MinValue;
    private DateTimeOffset _mailFetched = DateTimeOffset.MinValue;
    private bool _ticking;

    private sealed class AutomationState
    {
        public Dictionary<string, string> LastFired { get; set; } = [];
    }

    private AutomationState _state = new();

    public AutomationService(PerformaEngine engine)
    {
        _engine = engine;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "performa");
        Directory.CreateDirectory(dir);
        _statePath = Path.Combine(dir, "automation.json");
        _logPath = LogPath;
        LoadState();

        // One minute is fine-grained enough for "at 09:00" and "in 10 minutes"
        // while staying invisible in the profiler.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = TickAsync();
        _timer.Start();
        _ = TickAsync();
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
                _state = JsonSerializer.Deserialize<AutomationState>(
                    File.ReadAllText(_statePath)) ?? new AutomationState();
        }
        catch (JsonException) { _state = new AutomationState(); }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_statePath, JsonSerializer.Serialize(_state));
        }
        catch (IOException) { }
    }

    private bool FiredToday(string rule)
        => _state.LastFired.TryGetValue(rule, out var day)
           && day == DateTimeOffset.Now.ToString("yyyy-MM-dd");

    /// <summary>Called by a rule once its work is done, never before: marking
    /// first would burn the day's slot on a run that then failed.</summary>
    private void MarkFired(string rule, string detail = "")
    {
        _state.LastFired[rule] = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        SaveState();
        Log(rule, "fired", detail);
    }

    /// <summary>One rule attempt. Append-only, never rewritten.</summary>
    public readonly record struct RunEntry(string At, string Rule, string Outcome, string Detail);

    /// <summary>Lines kept in the run log. The loop ticks every minute all day,
    /// so this needs a ceiling or it grows without end; a few hundred entries
    /// still covers several days of real firings.</summary>
    private const int LogRetention = 400;

    /// <summary>
    /// Which log lines survive. Newest win, because the question being asked of
    /// this file is always "what just happened". Trims only once the file is
    /// well past the ceiling rather than on every append, since this sits on a
    /// path that runs every minute all day.
    /// </summary>
    public static string[] TrimLog(string[] lines, int retention)
        => lines.Length > retention * 2 ? lines[^retention..] : lines;

    /// <summary>
    /// Records what a rule did. The loop acts unattended, so when a brief does
    /// not arrive the only question that matters is whether it was skipped,
    /// tried and failed, or never reached - and until this existed there was
    /// nothing anywhere that could answer it.
    /// </summary>
    private void Log(string rule, string outcome, string detail = "")
    {
        try
        {
            var line = JsonSerializer.Serialize(new RunEntry(
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"), rule, outcome, detail));
            File.AppendAllText(_logPath, line + Environment.NewLine);

            var lines = File.ReadAllLines(_logPath);
            var kept = TrimLog(lines, LogRetention);
            if (kept.Length != lines.Length) File.WriteAllLines(_logPath, kept);
        }
        catch (IOException) { }
    }

    /// <summary>Where the run log lives. Static so anything that wants to read
    /// it can, without a handle on the running service.</summary>
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "performa", "automation-log.jsonl");

    /// <summary>The run log, newest first, for Settings to show.</summary>
    public static IReadOnlyList<RunEntry> RecentRuns(int take = 20)
    {
        try
        {
            if (!File.Exists(LogPath)) return [];
            return [.. File.ReadAllLines(LogPath)
                .Reverse()
                .Select(l => { try { return JsonSerializer.Deserialize<RunEntry>(l); } catch (JsonException) { return default; } })
                .Where(e => e.Rule is { Length: > 0 })
                .Take(take)];
        }
        catch (IOException) { return []; }
    }

    /// <summary>
    /// Runs one rule in its own failure boundary. Previously every rule shared a
    /// single try, so a throw in the meeting reminder took the brief, the nudge
    /// and the close-out down with it for that tick, and the swallowed exception
    /// meant nothing recorded why.
    /// </summary>
    private async Task RunRuleAsync(string rule, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            // Deliberately not MarkFired: a rule that threw has not done its
            // work, and marking it would burn the day's slot on a failure.
            Log(rule, "failed", ex.Message);
        }
    }

    /// <summary>
    /// Whether a morning brief still makes sense right now.
    ///
    /// It catches up if the machine was off at its hour, but only within the
    /// morning it belongs to: opening the app at 22:00 must not greet you with
    /// "Good morning". A late brief is worse than no brief, because it is wrong
    /// about the day it claims to describe.
    /// </summary>
    /// <summary>
    /// The brief is offered from its hour until the close-out takes over. It
    /// used to close four hours after its hour, which meant a machine first
    /// opened after 13:00 never got one at all: over two weeks of real logs the
    /// brief fired on 8 days of 14, and only once at the hour it was set to.
    /// The nudge and the close-out have no upper bound, so they always catch
    /// up; the brief was the only rule that could miss the day entirely.
    /// </summary>
    public static bool IsWithinBriefWindow(int nowHour, int briefHour, int closeoutHour)
        => nowHour >= briefHour && nowHour < closeoutHour;

    private async Task TickAsync()
    {
        if (_ticking) return;   // a slow rule must not stack behind itself
        _ticking = true;
        try
        {
            var prefs = _engine.Prefs;
            var now = DateTimeOffset.Now;

            await RunRuleAsync("events", RefreshEventsAsync);

            if (prefs.AutoMeetingReminders)
                await RunRuleAsync("meetings", () => RemindMeetingsAsync(now));

            if (prefs.AutoBrief && !FiredToday("brief")
                && IsWithinBriefWindow(now.Hour, prefs.BriefHour, prefs.CloseoutHour))
                await RunRuleAsync("brief", MorningBriefAsync);
            if (prefs.AutoNudgeUnpushed && now.Hour >= 17 && !FiredToday("nudge"))
                await RunRuleAsync("nudge", NudgeUnpushedAsync);
            if (prefs.AutoCloseout && now.Hour >= prefs.CloseoutHour && !FiredToday("closeout"))
                await RunRuleAsync("closeout", CloseoutAsync);
            if (prefs.AutoHarvestTasks)
                await RunRuleAsync("harvest", () => HarvestAsync(now));
        }
        catch (Exception ex)
        {
            // Rules carry their own boundary now, so anything reaching here is
            // the loop's own scaffolding rather than one rule misbehaving.
            Log("tick", "failed", ex.Message);
        }
        finally { _ticking = false; }
    }

    /// <summary>Calendar pulls every ten minutes; reminders read the cache.</summary>
    private async Task RefreshEventsAsync()
    {
        if (!_auth.IsSignedIn) return;
        if ((DateTimeOffset.Now - _eventsFetched).TotalMinutes < 10) return;

        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) return;
        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null) return;

        _events = await _calendar.GetUpcomingAsync(token);
        _eventsFetched = DateTimeOffset.Now;
    }

    private async Task RemindMeetingsAsync(DateTimeOffset now)
    {
        foreach (var e in _events)
        {
            if (e.AllDay || e.Start is not { } start) continue;
            var minutes = (start - now).TotalMinutes;
            if (minutes is <= 0 or > 10) continue;

            var key = $"{e.Title}|{start:O}";
            if (!_announced.Add(key)) continue;

            // The cross-stream nudge: a meeting is coming AND work is sitting
            // uncommitted. This pairing is the whole point of one spine.
            var dirty = await Task.Run(FindDirtyRepo);
            var body = $"{e.Title} at {start:HH:mm}"
                       + (e.Location is { Length: > 0 } loc ? $" · {loc}" : "");
            if (dirty is not null)
                body += $"\nYou have uncommitted work in {dirty} - commit before the call?";

            ToastService.Show("Meeting in 10 minutes", body);
        }
    }

    private string? FindDirtyRepo()
    {
        foreach (var path in _engine.DiscoverRepos())
        {
            try
            {
                if (_engine.BuildLooseEnds(path).Working.Total > 0)
                    return System.IO.Path.GetFileName(path);
            }
            catch (Exception) { }
        }
        return null;
    }

    private async Task MorningBriefAsync()
    {
        var commits = await Task.Run(_engine.TodayCommits);
        var tasks = _store.Load().Tasks.Count(t => !t.Done);
        var today = _events.Where(e =>
            e.Start is { } s && s.Date == DateTimeOffset.Now.Date && !e.AllDay).ToList();

        var bits = new List<string>
        {
            today.Count switch
            {
                0 => "a clear calendar",
                1 => $"one meeting at {today[0].Start:HH:mm}",
                _ => $"{today.Count} meetings, first at {today.Min(e => e.Start):HH:mm}",
            },
        };
        if (tasks > 0) bits.Add($"{tasks} open task(s)");
        if (commits.Count > 0) bits.Add($"{commits.Count} commit(s) already");

        // Greeted by the clock rather than by the rule's name: the brief is
        // often first opened at lunchtime, and "Good morning" at 12:54 is the
        // app saying something untrue.
        ToastService.Show(
            ViewModels.DailyViewModel.Greeting(DateTimeOffset.Now.Hour, _engine.Prefs.UserName),
            $"Today: {string.Join(", ", bits)}. Your brief is ready in Performa.");
        MarkFired("brief", $"{today.Count} meeting(s), {tasks} task(s)");
    }

    private async Task NudgeUnpushedAsync()
    {
        var stale = await Task.Run(() =>
        {
            var found = new List<string>();
            foreach (var path in _engine.DiscoverRepos())
            {
                try
                {
                    var f = _engine.BuildLooseEnds(path);
                    var name = System.IO.Path.GetFileName(path);
                    foreach (var b in f.UnpushedBranches.Where(b => b.Ahead > 0))
                        found.Add($"{name}/{b.Name} ({b.Ahead} ahead)");
                }
                catch (Exception) { }
            }
            return found;
        });
        if (stale.Count == 0) return;

        MarkFired("nudge");
        ToastService.Show("Unpushed work",
            string.Join(", ", stale.Take(3))
            + (stale.Count > 3 ? $" and {stale.Count - 3} more" : "")
            + ". One push and it is safe.");
    }

    private async Task CloseoutAsync()
    {
        var commits = await Task.Run(_engine.TodayCommits);
        var data = _store.Load();
        var done = data.Tasks.Count(t => t.Done);
        var open = data.Tasks.Count(t => !t.Done);
        var tomorrow = _events.FirstOrDefault(e =>
            e.Start is { } s && s.Date == DateTimeOffset.Now.Date.AddDays(1));

        var facts =
            $"Shipped {commits.Count} commit(s)"
            + (commits.Count > 0 ? $", last: \"{commits[0].Subject}\"" : "")
            + $". Tasks: {done} done, {open} rolling to tomorrow."
            + tomorrow switch
            {
                // An all-day entry has no start time; printing one gives "at 00:00".
                { AllDay: true } t => $" Tomorrow carries \"{t.Title}\" all day.",
                { Start: { } s } t => $" Tomorrow opens with \"{t.Title}\" at {s:HH:mm}.",
                _ => " Tomorrow starts clear.",
            };

        var text = facts;
        var answer = await _ai.AskAsync(_engine.Prefs,
            $"The user is {_engine.Prefs.UserName ?? "the developer"}. "
            + $"End-of-day facts: {facts}",
            "Write a two-sentence end-of-day close-out. Calm, concrete, a little dry; "
            + "no exclamation marks; use only the facts given.");
        if (answer is not null) text = answer.Text;

        data.Closeout = text;
        data.CloseoutDate = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        data.CloseoutStamp =
            $"Written automatically at {DateTimeOffset.Now:HH:mm}"
            + (answer is null ? "" : $" · {answer.Model}");
        _store.Save(data);
        _engine.NotifyDailyChanged();

        // Marked only now it has actually landed. Marking first burns the day:
        // if the write throws, the rule is recorded as done, never retries, and
        // yesterday's close-out sits on the page looking like today's.
        MarkFired("closeout");
        ToastService.Show("Day closed out", text);
    }

    /// <summary>
    /// Whether an extracted sentence is worth putting in front of someone as a
    /// task.
    ///
    /// The Inbox card can afford loose extraction - a noisy line sits next to
    /// the email that produced it and costs nothing. A task list cannot: a
    /// suggestion is a claim that something is owed, so junk here is worse than
    /// silence. The first build proposed "Please do not respond." as a task,
    /// which is where this filter comes from.
    /// </summary>
    public static bool LooksLikeARealAsk(string sentence)
    {
        var s = sentence.Trim();
        if (s.Length is < 20 or > 120) return false;

        // Anything carrying a URL is almost always marketing or a footer, and
        // an unreadable one once truncated to task length.
        if (s.Contains("http", StringComparison.OrdinalIgnoreCase)
            || s.Contains("<", StringComparison.Ordinal)) return false;

        // A byline, not a request.
        if (s.StartsWith("by ", StringComparison.OrdinalIgnoreCase)) return false;

        string[] boilerplate =
        [
            "do not respond", "do not reply", "unsubscribe", "privacy policy",
            "terms of service", "if you need additional help", "this email was sent",
            "you received this", "view in browser", "all rights reserved",
            "was found by", "click here", "learn more", "sign up for",
        ];
        if (boilerplate.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return false;

        // A real ask names an action directed at the reader. These cues are
        // deliberately narrower than the ones the Inbox card displays.
        string[] cues =
        [
            "could you", "can you", "please send", "please confirm", "please review",
            "please complete", "please submit", "please sign", "let me know",
            "deadline", "due by", "due on", "rsvp", "needs your", "waiting on you",
            "action required", "respond by", "reply by", "submit by", "confirm your",
        ];
        return cues.Any(c => s.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Email asks become suggested tasks. Quiet on purpose: suggestions
    /// wait on Daily rather than shouting from a toast.</summary>
    private async Task HarvestAsync(DateTimeOffset now)
    {
        if (!_auth.IsSignedIn) return;
        if ((now - _mailFetched).TotalMinutes < 15) return;
        _mailFetched = now;

        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) return;
        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null) return;

        var mail = await _gmail.GetRecentAsync(token);
        var data = _store.Load();
        var known = data.Tasks.Select(t => t.Text)
            .Concat(data.Suggested)
            .Concat(data.Dismissed)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var m in mail)
        {
            // Newsletters and no-reply senders are never asking you personally.
            if (m.IsBulk) continue;

            foreach (var ask in m.Actions.Where(LooksLikeARealAsk).Take(2))
            {
                var text = ask.Length > 90 ? ask[..90].TrimEnd() + "…" : ask;
                if (!known.Add(text)) continue;
                data.Suggested.Add(text);
                added = true;
            }
        }

        if (added)
        {
            // Suggestions accumulate but never unboundedly.
            while (data.Suggested.Count > 8) data.Suggested.RemoveAt(0);
            _store.Save(data);
            _engine.NotifyDailyChanged();
        }
    }
}
