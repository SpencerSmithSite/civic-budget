using CivicBudget.Web.Components.Common;

namespace CivicBudget.Web.Tests;

/// <summary>
/// Times are shown in Ohio's zone (Eastern) with the zone named, whatever the server's own zone is;
/// a container runs in UTC, which put a 2 PM sync at "6:00 PM".
/// </summary>
public class DisplayTests
{
    [Fact]
    public void Timestamps_are_shown_in_eastern_time_across_daylight_saving()
    {
        Assert.Equal("Jul 1, 2026 2:05 PM ET", Display.Timestamp(new DateTimeOffset(2026, 7, 1, 18, 5, 0, TimeSpan.Zero)));  // EDT, UTC-4
        Assert.Equal("Jan 15, 2026 2:05 PM ET", Display.Timestamp(new DateTimeOffset(2026, 1, 15, 19, 5, 0, TimeSpan.Zero))); // EST, UTC-5
    }

    [Fact]
    public void An_evening_publish_in_ohio_is_dated_that_day_not_the_next()
    {
        // 9:30 PM Eastern on June 30 is already July 1 in UTC.
        Assert.Equal("June 30, 2026", Display.LongDate(new DateTimeOffset(2026, 7, 1, 1, 30, 0, TimeSpan.Zero)));
    }
}
