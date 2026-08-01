using Performa.Desktop.Services;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// The automation loop acts without being asked, so its timing rules are the
/// part most worth pinning down: a rule that fires at the wrong hour is not a
/// cosmetic bug, it is the app saying something untrue.
/// </summary>
public class AutomationTests
{
    [Fact]
    public void A_short_run_log_is_left_whole()
    {
        var lines = Enumerable.Range(0, 5).Select(i => $"line {i}").ToArray();
        Assert.Equal(lines, AutomationService.TrimLog(lines, 400));
    }

    [Fact]
    public void A_log_past_the_ceiling_keeps_the_newest_entries()
    {
        var lines = Enumerable.Range(0, 25).Select(i => $"line {i}").ToArray();
        var kept = AutomationService.TrimLog(lines, 10);

        Assert.Equal(10, kept.Length);
        // The newest survive: this file exists to answer "what just happened",
        // so trimming the tail instead of the head would defeat the point.
        Assert.Equal("line 24", kept[^1]);
        Assert.Equal("line 15", kept[0]);
    }

    [Fact]
    public void Trimming_waits_until_the_log_is_well_past_the_ceiling()
    {
        // The loop ticks every minute all day, so rewriting the file the moment
        // it crosses the line means a rewrite on nearly every append.
        var lines = Enumerable.Range(0, 15).Select(i => $"line {i}").ToArray();
        Assert.Equal(15, AutomationService.TrimLog(lines, 10).Length);
    }

    [Theory]
    [InlineData(9, 9)]    // exactly on the hour
    [InlineData(10, 9)]   // machine booted an hour late
    [InlineData(12, 9)]   // last hour of the catch-up window
    public void The_brief_fires_at_its_hour_and_catches_up_through_the_morning(
        int nowHour, int briefHour)
        => Assert.True(AutomationService.IsWithinBriefWindow(nowHour, briefHour));

    [Theory]
    [InlineData(8, 9)]    // before its hour
    [InlineData(13, 9)]   // window closed
    [InlineData(22, 9)]   // the bug this pins: no "Good morning" at 22:00
    public void A_brief_never_arrives_outside_the_morning_it_describes(
        int nowHour, int briefHour)
        => Assert.False(AutomationService.IsWithinBriefWindow(nowHour, briefHour));

    [Fact]
    public void A_late_night_brief_hour_still_has_a_window()
    {
        // 23:00 + 4 would run past midnight; the window simply ends at the day
        // boundary rather than wrapping into the next morning's brief.
        Assert.True(AutomationService.IsWithinBriefWindow(23, 23));
        Assert.False(AutomationService.IsWithinBriefWindow(1, 23));
    }

    // Every one of these was actually proposed as a task by the first build.
    [Theory]
    [InlineData("Please do not respond.")]
    [InlineData("by Joshua Luke Smith")]
    [InlineData("You were found by people from these companies.")]
    [InlineData("If you need additional help, please visit Steam Support.")]
    [InlineData("Sign up for the Power On newsletter <https://www.bloomberg.com/account>")]
    [InlineData("One designer with the right AI tools can now build what used to take entire teams.")]
    public void Newsletter_noise_never_becomes_a_task(string sentence)
        => Assert.False(AutomationService.LooksLikeARealAsk(sentence));

    [Theory]
    [InlineData("Could you review the draft before Thursday?")]
    [InlineData("Please confirm your attendance for the workshop.")]
    [InlineData("The deadline for the scholarship form is 14 August.")]
    [InlineData("Action required: your enrolment is incomplete.")]
    public void A_genuine_request_still_gets_through(string sentence)
        => Assert.True(AutomationService.LooksLikeARealAsk(sentence));

    [Fact]
    public void A_fragment_or_an_essay_is_neither()
    {
        Assert.False(AutomationService.LooksLikeARealAsk("please"));
        Assert.False(AutomationService.LooksLikeARealAsk(
            "Could you " + new string('x', 200)));
    }
}
