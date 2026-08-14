namespace Performa.Desktop.Services;

/// <summary>Why a task is where it is in the order. Named rather than a bare
/// number so the reason can be shown next to the task.</summary>
public enum Urgency
{
    Overdue,
    Today,
    Soon,
    Someday,
    Done,
}

/// <summary>Which stream an item came from. Decides what can be done about it.</summary>
public enum ItemKind
{
    Meeting,
    Task,
    Repo,
    Mail,
}

/// <summary>
/// One thing wanting attention, from any stream.
/// </summary>
/// <param name="Pressure">Lower is more pressing. A number rather than an
/// enum because the ordering across streams is the whole point: an overdue
/// task and a meeting in ten minutes have to be comparable, and "which enum
/// member is more urgent" is not a question the type system answers.</param>
public sealed record ServingCandidate(
    ItemKind Kind, string Title, string Why, int Pressure, string Payload = "");

/// <summary>What Serving needs to know about a repository. A narrow shape
/// rather than RepoSnapshot so the ordering can be tested without building a
/// working tree.</summary>
public readonly record struct RepoState(string Name, string Path, string Branch, int Uncommitted, int Unpushed);

/// <summary>A stretch of the day with nothing booked in it.</summary>
public readonly record struct FreeBlock(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// The ordering behind the Serving page.
///
/// Deliberately deterministic. The model is given this order and asked to say
/// it well; it is never asked what the order should be. A language model
/// ranking a list with no dates on it produces a confident guess, and a
/// confident guess about what someone should do next is worse than no
/// suggestion at all.
/// </summary>
public static class Serving
{
    /// <summary>How many days ahead still counts as "soon" rather than "someday".</summary>
    public const int SoonDays = 7;

    /// <summary>Deferrals before the wording should stop calling it urgent.</summary>
    public const int StaleDeferrals = 3;

    public static Urgency UrgencyOf(DailyTask task, DateOnly today)
    {
        if (task.Done) return Urgency.Done;
        if (!DateOnly.TryParse(task.Due, out var due)) return Urgency.Someday;

        var days = due.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => Urgency.Overdue,
            0 => Urgency.Today,
            <= SoonDays => Urgency.Soon,
            _ => Urgency.Someday,
        };
    }

    /// <summary>
    /// Orders tasks by how soon they are due. Stable inside each band, so two
    /// tasks due the same day keep the order they were written in rather than
    /// shuffling every refresh.
    /// </summary>
    public static IReadOnlyList<DailyTask> Rank(IEnumerable<DailyTask> tasks, DateOnly today)
        => [.. tasks
            .Select((task, index) => (task, index))
            .OrderBy(x => (int)UrgencyOf(x.task, today))
            .ThenBy(x => x.task.Due ?? "9999-99-99")
            .ThenBy(x => x.index)
            .Select(x => x.task)];

    /// <summary>
    /// A task pushed enough times is not urgent, it is unwanted. Saying so is
    /// more useful than presenting it as next for a fourth morning.
    /// </summary>
    public static bool IsStale(DailyTask task) => task.Deferred >= StaleDeferrals;

    /// <summary>
    /// The gaps between today's timed events, inside a working window.
    ///
    /// All-day entries are ignored: they block the whole day on paper and none
    /// of it in practice, so counting them would report no free time on any day
    /// with a birthday in the calendar. Overlapping meetings are merged, since
    /// two overlapping bookings do not make the gap between them negative.
    /// </summary>
    public static IReadOnlyList<FreeBlock> FreeBlocks(
        IEnumerable<CalendarEvent> events, DateTimeOffset from, DateTimeOffset until)
    {
        var booked = events
            .Where(e => !e.AllDay && e.Start is not null && e.End is not null)
            .Select(e => (Start: e.Start!.Value, End: e.End!.Value))
            .Where(e => e.End > from && e.Start < until)
            .OrderBy(e => e.Start)
            .ToList();

        var free = new List<FreeBlock>();
        var cursor = from;

        foreach (var (start, end) in booked)
        {
            if (start > cursor) free.Add(new FreeBlock(cursor, start));
            if (end > cursor) cursor = end;
        }

        if (cursor < until) free.Add(new FreeBlock(cursor, until));

        // A four-minute slot between two meetings is not focus time.
        return [.. free.Where(b => b.Length >= TimeSpan.FromMinutes(15))];
    }

    /// <summary>How many finished tasks to keep. Enough to answer "what did I
    /// get done recently" without the file growing for the life of the app.</summary>
    public const int ArchiveKeep = 200;

    // Pressure bands. Spaced so a band can be split later without renumbering,
    // and ordered by how little choice you have about when to deal with it: a
    // meeting happens whether or not you are ready, unpushed work is a machine
    // failure away from being gone, and a task with no date can wait by
    // definition.
    private const int MeetingImminent = 0;
    private const int TaskOverdue = 10;
    private const int MeetingSoon = 20;
    private const int TaskToday = 30;
    private const int RepoUnpushed = 40;
    private const int RepoUncommitted = 50;
    private const int TaskSoon = 60;
    private const int MailAsks = 70;
    private const int TaskSomeday = 80;

    /// <summary>A meeting inside this window is the next thing you do.</summary>
    public static readonly TimeSpan Imminent = TimeSpan.FromMinutes(30);

    /// <summary>Beyond this a meeting is context rather than pressure.</summary>
    public static readonly TimeSpan LaterToday = TimeSpan.FromHours(2);

    /// <summary>
    /// Everything wanting attention, from every stream, in one order.
    ///
    /// The point of ranking across streams rather than within them: a list of
    /// tasks cannot tell you that the useful thing right now is pushing four
    /// commits before a meeting starts. Kept a pure function of already
    /// gathered facts so the ordering can be tested without a calendar, a
    /// mailbox or a working tree.
    /// </summary>
    public static IReadOnlyList<ServingCandidate> Compose(
        IEnumerable<DailyTask> tasks,
        IEnumerable<CalendarEvent> events,
        IEnumerable<RepoState> repos,
        int mailAsks,
        DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.Date);
        var found = new List<ServingCandidate>();

        foreach (var e in events)
        {
            if (e.AllDay || e.Start is not { } start) continue;
            var until = start - now;
            if (until < TimeSpan.Zero || until > LaterToday) continue;

            found.Add(new ServingCandidate(
                ItemKind.Meeting, e.Title,
                until <= Imminent ? $"starts {Minutes(until)}" : $"at {start:HH:mm}",
                until <= Imminent ? MeetingImminent : MeetingSoon));
        }

        foreach (var task in tasks)
        {
            if (task.Done) continue;
            var urgency = UrgencyOf(task, today);
            var pressure = urgency switch
            {
                Urgency.Overdue => TaskOverdue,
                Urgency.Today => TaskToday,
                Urgency.Soon => TaskSoon,
                _ => TaskSomeday,
            };
            var why = urgency switch
            {
                Urgency.Overdue => task.Due is null ? "overdue" : $"was due {task.Due}",
                Urgency.Today => "due today",
                Urgency.Soon => $"due {task.Due}",
                _ => IsStale(task) ? $"pushed {task.Deferred} times" : "no date",
            };
            found.Add(new ServingCandidate(ItemKind.Task, task.Text, why, pressure, task.Text));
        }

        foreach (var repo in repos)
        {
            // Unpushed first: uncommitted work is on your disk, unpushed work
            // is on your disk and nowhere else.
            if (repo.Unpushed > 0)
                found.Add(new ServingCandidate(
                    ItemKind.Repo, repo.Name,
                    $"{repo.Unpushed} commit(s) only on this machine",
                    RepoUnpushed, repo.Path));
            else if (repo.Uncommitted > 0)
                found.Add(new ServingCandidate(
                    ItemKind.Repo, repo.Name,
                    $"{repo.Uncommitted} uncommitted file(s) on {repo.Branch}",
                    RepoUncommitted, repo.Path));
        }

        if (mailAsks > 0)
            found.Add(new ServingCandidate(
                ItemKind.Mail, $"{mailAsks} message(s) asking something",
                "in the last three days", MailAsks));

        // Stable inside a band so the list does not reshuffle between refreshes.
        return [.. found
            .Select((item, index) => (item, index))
            .OrderBy(x => x.item.Pressure)
            .ThenBy(x => x.index)
            .Select(x => x.item)];
    }

    private static string Minutes(TimeSpan until)
        => until.TotalMinutes < 1 ? "now" : $"in {(int)until.TotalMinutes} min";

    /// <summary>
    /// Splits finished work off the live list once the day it was finished on
    /// has passed.
    ///
    /// Today's completions stay put, because ticking something and watching it
    /// vanish reads as having lost it, and the progress meter counts them. Only
    /// yesterday's move, so the list you open in the morning is what is left
    /// rather than a record of what already happened.
    /// </summary>
    public static (List<DailyTask> Live, List<DailyTask> Archived) Sweep(
        IEnumerable<DailyTask> tasks, DateOnly today)
    {
        List<DailyTask> live = [], archived = [];
        foreach (var task in tasks)
        {
            var stale = task.Done
                && DateOnly.TryParse(task.DoneAt, out var at)
                && at < today;
            (stale ? archived : live).Add(task);
        }
        return (live, archived);
    }

    /// <summary>
    /// Lifts a due date out of what someone typed and hands back the task
    /// without it.
    ///
    /// A date picker is three moves for something already being written, so
    /// "ship the readme friday" becomes "ship the readme" due that Friday. Only
    /// the trailing phrase is read: a date in the middle of a sentence is
    /// usually part of the task ("move the 3pm standup") rather than its
    /// deadline, and cutting it would mangle the text.
    ///
    /// Deliberately small. It knows the handful of forms people actually type
    /// and leaves everything else alone, because a parser that guesses wrong
    /// silently reschedules work.
    /// </summary>
    public static (string Text, string? Due) SplitDue(string raw, DateOnly today)
    {
        var text = raw.TrimEnd();
        foreach (var (phrase, resolve) in TrailingDates(today))
        {
            if (!text.EndsWith(phrase, StringComparison.OrdinalIgnoreCase)) continue;

            var head = text[..^phrase.Length].TrimEnd();
            // "friday" alone is a task called friday, not an undated nothing.
            if (head.Length == 0) continue;

            // Strip a trailing "by"/"on"/"due" so "ship it by friday" reads
            // "ship it" rather than "ship it by".
            foreach (var filler in (string[])[" by", " on", " due", " for"])
                if (head.EndsWith(filler, StringComparison.OrdinalIgnoreCase))
                {
                    head = head[..^filler.Length].TrimEnd();
                    break;
                }

            return head.Length == 0 ? (text, null) : (head, resolve().ToString("yyyy-MM-dd"));
        }

        return (text, null);
    }

    private static IEnumerable<(string Phrase, Func<DateOnly> Resolve)> TrailingDates(DateOnly today)
    {
        yield return ("today", () => today);
        yield return ("tomorrow", () => today.AddDays(1));
        yield return ("next week", () => today.AddDays(7));

        // Longest first so "next monday" is not eaten by "monday".
        for (var day = 0; day < 7; day++)
        {
            var name = ((DayOfWeek)day).ToString();
            yield return ($"next {name}", () => NextWeekday(today, (DayOfWeek)day, skipThisWeek: true));
            yield return (name, () => NextWeekday(today, (DayOfWeek)day, skipThisWeek: false));
        }
    }

    /// <summary>The next date falling on a weekday. Naming today's weekday means
    /// next week rather than now: "ship it friday" said on a Friday is about the
    /// one coming, since a deadline already passed is not a deadline.</summary>
    private static DateOnly NextWeekday(DateOnly from, DayOfWeek target, bool skipThisWeek)
    {
        var ahead = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (ahead == 0) ahead = 7;
        if (skipThisWeek && ahead < 7) ahead += 7;
        return from.AddDays(ahead);
    }

    /// <summary>
    /// Which icon an entry gets, read from its title.
    ///
    /// A Google colour says which calendar an event came from, never what it
    /// is. Two entries in the same colour can be a lecture and a flight, and
    /// nothing on the row distinguishes them.
    ///
    /// Read rather than asked. A model could classify these, but it would be a
    /// call per event on every refresh, it would need the network to draw a
    /// calendar Performa already has, and it would answer differently on
    /// different days for the same entry. The cost of a miss here is the wrong
    /// small glyph, so certainty and speed are worth more than coverage.
    /// Anything unmatched keeps the plain calendar mark.
    /// </summary>
    public static string IconForEvent(string title)
    {
        var t = title.ToLowerInvariant();

        if (Any(t, "flight", "airport", "depart", "arrive", "boarding", "terminal"))
            return "IconPlane";
        if (Any(t, "lecture", "class", "exam", "quiz", "course", "seminar", "tutorial",
                   "lab ", "midterm", "final", "수업", "강의", "시험"))
            return "IconCap";
        if (Any(t, "deadline", "due", "submit", "hand in", "expires", "cutoff"))
            return "IconClock";
        if (Any(t, "lunch", "dinner", "breakfast", "coffee", "brunch", "drinks"))
            return "IconCup";
        if (Any(t, "meet", "standup", "stand-up", "sync", "call", "1:1", "one to one",
                   "interview", "review", "catch up", "huddle", "retro"))
            return "IconPeople";

        return "IconDaily";
    }

    private static bool Any(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    /// <summary>
    /// Whether a task is talking about a given repository.
    ///
    /// Substring, but not blindly: a repo called "a" or "ui" appears inside
    /// half the English language, and matching one would hand the model
    /// context about the wrong project while sounding just as certain. Short
    /// names have to be a standalone word to count.
    /// </summary>
    public static bool MentionsRepo(string taskText, string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName)) return false;

        var at = taskText.IndexOf(repoName, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;
        if (repoName.Length >= 5) return true;

        var before = at == 0 || !char.IsLetterOrDigit(taskText[at - 1]);
        var afterAt = at + repoName.Length;
        var after = afterAt >= taskText.Length || !char.IsLetterOrDigit(taskText[afterAt]);
        return before && after;
    }

    /// <summary>The longest clear stretch left, or null if the day is full.</summary>
    public static FreeBlock? LongestBlock(IEnumerable<FreeBlock> blocks)
    {
        FreeBlock? best = null;
        foreach (var b in blocks)
            if (best is null || b.Length > best.Value.Length) best = b;
        return best;
    }
}
