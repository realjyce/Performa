using Performa.Desktop.Services;
using Performa.Desktop.ViewModels;
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
    [InlineData(9, 9, 18)]    // exactly on the hour
    [InlineData(10, 9, 18)]   // machine booted an hour late
    [InlineData(13, 9, 18)]   // the case two weeks of logs turned up: a first
    [InlineData(17, 9, 18)]   // launch past lunch used to get no brief at all
    public void The_brief_catches_up_whenever_the_day_actually_starts(
        int nowHour, int briefHour, int closeoutHour)
        => Assert.True(AutomationService.IsWithinBriefWindow(nowHour, briefHour, closeoutHour));

    [Theory]
    [InlineData(8, 9, 18)]    // before its hour
    [InlineData(18, 9, 18)]   // the close-out owns the evening from here
    [InlineData(22, 9, 18)]   // the bug this still pins: no brief at 22:00
    public void The_brief_stops_where_the_closeout_takes_over(
        int nowHour, int briefHour, int closeoutHour)
        => Assert.False(AutomationService.IsWithinBriefWindow(nowHour, briefHour, closeoutHour));

    [Fact]
    public void A_brief_hour_past_the_closeout_never_opens()
    {
        // Nonsense config rather than a real schedule, but it must not wrap
        // into the small hours looking for a window.
        Assert.False(AutomationService.IsWithinBriefWindow(23, 23, 18));
        Assert.False(AutomationService.IsWithinBriefWindow(1, 23, 18));
    }

    [Theory]
    [InlineData(9, "Good morning")]
    [InlineData(12, "Good afternoon")]   // 8 of 14 real briefs landed after noon
    [InlineData(19, "Good evening")]
    [InlineData(3, "Up late")]
    public void The_brief_greets_by_the_clock_not_by_its_name(int hour, string expected)
        => Assert.StartsWith(expected, DailyViewModel.Greeting(hour, null));

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
