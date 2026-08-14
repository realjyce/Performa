using Performa.Desktop.Services;
using Performa.Desktop.ViewModels;
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

    // --- ranking across every stream at once ---

    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static CalendarEvent MeetingAt(int hour, int minute = 0)
        => new("standup", new DateTimeOffset(2026, 8, 15, hour, minute, 0, TimeSpan.Zero),
               new DateTimeOffset(2026, 8, 15, hour + 1, minute, 0, TimeSpan.Zero),
               false, null, "#fff", "cal");

    [Fact]
    public void A_meeting_in_ten_minutes_outranks_everything_else()
    {
        // The point of ranking across streams: a task list alone cannot tell
        // you that starting anything right now is a waste.
        var items = Serving.Compose(
            [Task("overdue thing", "2026-08-01")],
            [MeetingAt(12, 10)],
            [new RepoState("api", "C:/api", "main", 0, 4)],
            mailAsks: 3, Noon);

        Assert.Equal(ItemKind.Meeting, items[0].Kind);
    }

    [Fact]
    public void A_meeting_hours_away_does_not_outrank_overdue_work()
    {
        var items = Serving.Compose(
            [Task("overdue thing", "2026-08-01")], [MeetingAt(13, 30)], [], 0, Noon);

        Assert.Equal(ItemKind.Task, items[0].Kind);
        Assert.Equal(ItemKind.Meeting, items[1].Kind);
    }

    [Fact]
    public void Unpushed_work_outranks_uncommitted_work()
    {
        // Uncommitted work is on your disk. Unpushed work is on your disk and
        // nowhere else, which is a different kind of exposure.
        var items = Serving.Compose([], [],
            [new RepoState("dirty", "C:/d", "main", 9, 0),
             new RepoState("ahead", "C:/a", "main", 0, 1)], 0, Noon);

        Assert.Equal("ahead", items[0].Title);
        Assert.Equal("dirty", items[1].Title);
    }

    [Fact]
    public void A_repo_reports_its_worse_problem_once_not_both()
    {
        var items = Serving.Compose([], [],
            [new RepoState("both", "C:/b", "main", 5, 2)], 0, Noon);

        Assert.Single(items);
        Assert.Contains("only on this machine", items[0].Why);
    }

    [Fact]
    public void A_clean_repo_and_an_empty_mailbox_say_nothing()
    {
        var items = Serving.Compose([], [],
            [new RepoState("clean", "C:/c", "main", 0, 0)], mailAsks: 0, Noon);

        Assert.Empty(items);
    }

    [Fact]
    public void An_all_day_entry_is_not_a_thing_starting_soon()
    {
        var birthday = new CalendarEvent("bday", Noon, Noon.AddHours(8), true, null, "#fff", "cal");
        Assert.Empty(Serving.Compose([], [birthday], [], 0, Noon));
    }

    [Fact]
    public void A_meeting_already_started_is_not_pressure_any_more()
    {
        // It is happening. Listing it as the next thing to do is noise.
        Assert.Empty(Serving.Compose([], [MeetingAt(11)], [], 0, Noon));
    }

    [Fact]
    public void Everything_lands_in_one_order_across_the_streams()
    {
        var items = Serving.Compose(
            [Task("someday"), Task("overdue", "2026-08-01"), Task("today", "2026-08-15")],
            [MeetingAt(13, 30)],
            [new RepoState("api", "C:/api", "main", 0, 2)],
            mailAsks: 4, Noon);

        Assert.Equal(
            [ItemKind.Task, ItemKind.Meeting, ItemKind.Task, ItemKind.Repo, ItemKind.Mail, ItemKind.Task],
            items.Select(i => i.Kind));
        Assert.Equal("overdue", items[0].Title);
    }

    // --- what a message wants from you ---

    [Theory]
    // hasActions, hasAmounts, hasDates
    [InlineData(true, false, false, "Asks")]
    [InlineData(false, true, false, "Money")]
    [InlineData(false, false, true, "Dated")]
    [InlineData(false, false, false, "Rest")]
    public void A_message_lands_in_the_bucket_for_what_it_wants(
        bool actions, bool amounts, bool dates, string expected)
        => Assert.Equal(expected, MailCard.BucketOf(actions, amounts, dates, isBulk: false));

    [Fact]
    public void A_message_that_is_several_things_at_once_is_only_filed_once()
    {
        // An invoice asking for something by Friday is an ask first. Filing it
        // under three headings means reading it three times.
        Assert.Equal("Asks", MailCard.BucketOf(true, true, true, false));
        Assert.Equal("Money", MailCard.BucketOf(false, true, true, false));
    }

    [Fact]
    public void A_newsletter_is_never_asking_you_personally()
    {
        // The whole reason the Asks count read 5 against a mailbox with none.
        Assert.NotEqual("Asks", MailCard.BucketOf(true, false, false, isBulk: true));
    }

    // --- what counts as a sentence asking something ---

    [Theory]
    [InlineData("by Kristin Nave")]
    [InlineData("Residents displaced by the earthquake at a shelter in Cali yesterday.")]
    [InlineData("Courses offered by other departments can be recognized as major electives.")]
    [InlineData("The device can be controlled fully by voice instructions.")]
    public void A_passive_sentence_or_a_byline_is_not_a_request(string sentence)
        => Assert.Empty(GmailService.ExtractActions(sentence));

    [Theory]
    [InlineData("The new design is ready for you.")]        // "sign" inside "design"
    [InlineData("Customer satisfaction rose again.")]        // "action" inside "satisfaction"
    [InlineData("There were signs its popularity was growing.")]
    public void A_cue_buried_inside_another_word_does_not_count(string sentence)
        => Assert.Empty(GmailService.ExtractActions(sentence));

    [Theory]
    [InlineData("Please confirm your attendance before Friday.")]
    [InlineData("Could you review the attached document?")]
    [InlineData("The deadline for submission is next week.")]
    [InlineData("This needs your approval to move forward.")]
    [InlineData("Please submit the form no later than Monday.")]
    public void A_real_request_still_gets_through(string sentence)
        => Assert.Single(GmailService.ExtractActions(sentence));

    // --- sweeping finished work off the live list ---

    private static DailyTask Finished(string text, string doneAt)
        => new() { Text = text, Done = true, DoneAt = doneAt };

    [Fact]
    public void Todays_finished_work_stays_where_it_was_ticked()
    {
        // Ticking something and watching it vanish reads as having lost it,
        // and the progress meter counts today's completions.
        var (live, archived) = Serving.Sweep([Finished("shipped", "2026-08-15")], Today);

        Assert.Single(live);
        Assert.Empty(archived);
    }

    [Fact]
    public void Yesterdays_finished_work_moves_off_the_list()
    {
        var (live, archived) = Serving.Sweep(
            [Finished("shipped", "2026-08-14"), Task("still open")], Today);

        Assert.Equal(["still open"], live.Select(t => t.Text));
        Assert.Equal(["shipped"], archived.Select(t => t.Text));
    }

    [Fact]
    public void An_open_task_is_never_swept_however_old_its_due_date()
    {
        var (live, archived) = Serving.Sweep([Task("ancient", "2020-01-01")], Today);

        Assert.Single(live);
        Assert.Empty(archived);
    }

    [Fact]
    public void A_done_task_with_no_stamp_is_left_alone_rather_than_guessed_at()
    {
        // Written before DoneAt existed. Sweeping it would file work under a
        // day it may not have happened on, so it stays until it is ticked again.
        var (live, archived) = Serving.Sweep(
            [new DailyTask { Text = "legacy", Done = true }], Today);

        Assert.Single(live);
        Assert.Empty(archived);
    }

    // --- reading a due date out of what was typed ---
    // Today is Saturday 15 August 2026 in these.

    [Theory]
    [InlineData("ship the readme today", "ship the readme", "2026-08-15")]
    [InlineData("ship the readme tomorrow", "ship the readme", "2026-08-16")]
    [InlineData("ship the readme next week", "ship the readme", "2026-08-22")]
    [InlineData("ship the readme monday", "ship the readme", "2026-08-17")]
    [InlineData("ship the readme by friday", "ship the readme", "2026-08-21")]
    [InlineData("ship the readme on Tuesday", "ship the readme", "2026-08-18")]
    public void A_trailing_date_is_lifted_out_and_the_task_reads_clean(
        string raw, string text, string due)
    {
        var (gotText, gotDue) = Serving.SplitDue(raw, Today);
        Assert.Equal(text, gotText);
        Assert.Equal(due, gotDue);
    }

    [Fact]
    public void Naming_todays_weekday_means_the_one_coming_not_this_morning()
    {
        // Said on a Saturday. A deadline that has already passed is not one.
        var (_, due) = Serving.SplitDue("ship the readme saturday", Today);
        Assert.Equal("2026-08-22", due);
    }

    [Fact]
    public void Next_monday_is_a_week_past_monday()
    {
        var (_, plain) = Serving.SplitDue("ship it monday", Today);
        var (_, next) = Serving.SplitDue("ship it next monday", Today);
        Assert.Equal("2026-08-17", plain);
        Assert.Equal("2026-08-24", next);
    }

    [Theory]
    [InlineData("move the friday standup earlier")]  // date is mid-sentence, part of the task
    [InlineData("write the parser")]
    [InlineData("")]
    public void Text_with_no_trailing_date_is_left_exactly_as_written(string raw)
    {
        var (text, due) = Serving.SplitDue(raw, Today);
        Assert.Equal(raw, text);
        Assert.Null(due);
    }

    [Fact]
    public void A_task_that_is_only_a_date_stays_a_task()
    {
        // Someone writing just "friday" means a task called friday, not an
        // empty task filed under Friday.
        var (text, due) = Serving.SplitDue("friday", Today);
        Assert.Equal("friday", text);
        Assert.Null(due);
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
