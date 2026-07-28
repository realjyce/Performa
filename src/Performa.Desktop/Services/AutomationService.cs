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

    private void MarkFired(string rule)
    {
        _state.LastFired[rule] = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        SaveState();
    }

    /// <summary>
    /// Whether a morning brief still makes sense right now.
    ///
    /// It catches up if the machine was off at its hour, but only within the
    /// morning it belongs to: opening the app at 22:00 must not greet you with
    /// "Good morning". A late brief is worse than no brief, because it is wrong
    /// about the day it claims to describe.
    /// </summary>
    public const int BriefWindowHours = 4;

    public static bool IsWithinBriefWindow(int nowHour, int briefHour)
        => nowHour >= briefHour && nowHour < briefHour + BriefWindowHours;

    private async Task TickAsync()
    {
        if (_ticking) return;   // a slow rule must not stack behind itself
        _ticking = true;
        try
        {
            var prefs = _engine.Prefs;
            var now = DateTimeOffset.Now;

            await RefreshEventsAsync();

            if (prefs.AutoMeetingReminders) await RemindMeetingsAsync(now);

            if (prefs.AutoBrief && !FiredToday("brief")
                && IsWithinBriefWindow(now.Hour, prefs.BriefHour))
                await MorningBriefAsync();
            if (prefs.AutoNudgeUnpushed && now.Hour >= 17 && !FiredToday("nudge"))
                await NudgeUnpushedAsync();
            if (prefs.AutoCloseout && now.Hour >= prefs.CloseoutHour && !FiredToday("closeout"))
                await CloseoutAsync();
            if (prefs.AutoHarvestTasks) await HarvestAsync(now);
        }
        catch (Exception) { /* one bad tick must never kill the loop */ }
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
        MarkFired("brief");

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

        ToastService.Show(
            $"Good morning{(_engine.Prefs.UserName is { Length: > 0 } n ? ", " + n : "")}",
            $"Today: {string.Join(", ", bits)}. Your brief is ready in Performa.");
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
        MarkFired("closeout");

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
            + (tomorrow is not null
                ? $" Tomorrow opens with \"{tomorrow.Title}\" at {tomorrow.Start:HH:mm}."
                : " Tomorrow starts clear.");

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

        ToastService.Show("Day closed out", text);
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
            foreach (var ask in m.Actions.Take(2))
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
