using Performa.Desktop.Services;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// The Serving page tells someone what to do next, so the order has to come
/// from data rather than from a model's opinion. These pin the order itself;
/// the model only ever gets handed the result and asked to phrase it.
/// </summary>
public class ServingTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private static DailyTask Task(string text, string? due = null, bool done = false, int deferred = 0)
        => new() { Text = text, Due = due, Done = done, Deferred = deferred };

    [Theory]
    [InlineData("2026-08-14", Urgency.Overdue)]
    [InlineData("2026-08-15", Urgency.Today)]
    [InlineData("2026-08-18", Urgency.Soon)]
    [InlineData("2026-08-22", Urgency.Soon)]     // exactly 7 days out
    [InlineData("2026-08-23", Urgency.Someday)]  // one past the horizon
    [InlineData(null, Urgency.Someday)]
    public void Urgency_comes_from_the_due_date(string? due, Urgency expected)
        => Assert.Equal(expected, Serving.UrgencyOf(Task("x", due), Today));

    [Fact]
    public void A_done_task_is_never_urgent_however_overdue()
        => Assert.Equal(Urgency.Done, Serving.UrgencyOf(Task("x", "2020-01-01", done: true), Today));

    [Fact]
    public void Garbage_in_the_due_field_reads_as_undated_not_as_a_crash()
        => Assert.Equal(Urgency.Someday, Serving.UrgencyOf(Task("x", "next tuesday"), Today));

    [Fact]
    public void Overdue_leads_and_finished_work_sinks()
    {
        var ranked = Serving.Rank(
        [
            Task("someday"),
            Task("done", "2026-08-14", done: true),
            Task("today", "2026-08-15"),
            Task("overdue", "2026-08-10"),
            Task("soon", "2026-08-17"),
        ], Today);

        Assert.Equal(
            ["overdue", "today", "soon", "someday", "done"],
            ranked.Select(t => t.Text));
    }

    [Fact]
    public void Two_tasks_due_the_same_day_keep_the_order_they_were_written()
    {
        // Without this the list reshuffles on every refresh, and a list that
        // moves while you are reading it cannot be trusted to be a priority.
        var ranked = Serving.Rank(
            [Task("first", "2026-08-15"), Task("second", "2026-08-15")], Today);

        Assert.Equal(["first", "second"], ranked.Select(t => t.Text));
    }

    [Fact]
    public void A_task_pushed_enough_times_stops_claiming_to_be_next()
    {
        Assert.False(Serving.IsStale(Task("x", deferred: 2)));
        Assert.True(Serving.IsStale(Task("x", deferred: 3)));
    }

    // --- free blocks ---

    private static DateTimeOffset At(int hour, int minute = 0)
        => new(2026, 8, 15, hour, minute, 0, TimeSpan.Zero);

    private static CalendarEvent Meeting(int fromHour, int toHour)
        => new("m", At(fromHour), At(toHour), false, null, "#fff", "cal");

    [Fact]
    public void Gaps_are_the_time_between_the_meetings()
    {
        var free = Serving.FreeBlocks([Meeting(10, 11), Meeting(14, 15)], At(9), At(17));

        Assert.Equal(3, free.Count);
        Assert.Equal(At(9), free[0].Start);
        Assert.Equal(At(10), free[0].End);
        Assert.Equal(TimeSpan.FromHours(3), free[1].Length);   // 11:00 to 14:00
        Assert.Equal(TimeSpan.FromHours(2), free[2].Length);   // 15:00 to 17:00
    }

    [Fact]
    public void An_all_day_entry_does_not_swallow_the_day()
    {
        // A birthday blocks the whole day on paper and none of it in practice.
        var birthday = new CalendarEvent("bday", At(0), At(23), true, null, "#fff", "cal");
        var free = Serving.FreeBlocks([birthday], At(9), At(17));

        Assert.Single(free);
        Assert.Equal(TimeSpan.FromHours(8), free[0].Length);
    }

    [Fact]
    public void Overlapping_meetings_do_not_invent_a_gap_between_them()
    {
        var free = Serving.FreeBlocks([Meeting(10, 12), Meeting(11, 13)], At(9), At(17));

        Assert.Equal(2, free.Count);
        Assert.Equal(TimeSpan.FromHours(1), free[0].Length);   // 09:00 to 10:00
        Assert.Equal(TimeSpan.FromHours(4), free[1].Length);   // 13:00 to 17:00
    }

    [Fact]
    public void A_scrap_between_back_to_back_meetings_is_not_focus_time()
    {
        var free = Serving.FreeBlocks(
            [Meeting(9, 10), new CalendarEvent("m", At(10, 5), At(17), false, null, "#fff", "cal")],
            At(9), At(17));

        Assert.Empty(free);
    }

    // --- grounding the "how do I start this" answer ---

    [Theory]
    [InlineData("Test the Performa exe on a clean box", "Performa")]
    [InlineData("Loosen the everyAir harvest filter", "everyAir")]
    [InlineData("fix EVERYAIR gauge", "everyAir")]            // case does not matter
    public void A_task_naming_a_repo_is_matched_to_it(string task, string repo)
        => Assert.True(Serving.MentionsRepo(task, repo));

    [Theory]
    [InlineData("Write the Glance widget", "Performa")]
    [InlineData("", "Performa")]
    public void A_task_naming_no_repo_matches_none(string task, string repo)
        => Assert.False(Serving.MentionsRepo(task, repo));

    [Theory]
    [InlineData("Update the changelog", "log")]     // buried inside "changelog"
    [InlineData("Rewrite the parser", "rs")]        // buried inside "parser"
    [InlineData("Draft the README", "ad")]          // buried inside "Draft"
    public void A_short_repo_name_does_not_match_inside_another_word(string task, string repo)
        => Assert.False(Serving.MentionsRepo(task, repo));

    [Fact]
    public void A_short_repo_name_still_matches_when_it_stands_alone()
    {
        Assert.True(Serving.MentionsRepo("push log to origin", "log"));
        Assert.True(Serving.MentionsRepo("fix rs-parser build", "rs"));
    }

    [Fact]
    public void A_full_day_reports_no_longest_block_rather_than_a_zero_one()
    {
        Assert.Null(Serving.LongestBlock(Serving.FreeBlocks([Meeting(9, 17)], At(9), At(17))));
    }

    [Fact]
    public void The_longest_block_is_the_one_worth_naming()
    {
        var free = Serving.FreeBlocks([Meeting(10, 11), Meeting(14, 15)], At(9), At(17));
        Assert.Equal(TimeSpan.FromHours(3), Serving.LongestBlock(free)!.Value.Length);
    }
}
