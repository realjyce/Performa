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
