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

    /// <summary>The longest clear stretch left, or null if the day is full.</summary>
    public static FreeBlock? LongestBlock(IEnumerable<FreeBlock> blocks)
    {
        FreeBlock? best = null;
        foreach (var b in blocks)
            if (best is null || b.Length > best.Value.Length) best = b;
        return best;
    }
}
