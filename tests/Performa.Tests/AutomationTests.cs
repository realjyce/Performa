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
}
