namespace CivicBudget.Application.Common;

/// <summary>
/// Dates as the government's own calendar shows them. Every Ohio government keeps Eastern time, so a
/// budget adopted at a 7:30 PM council meeting is dated that evening, not the next day in UTC.
/// </summary>
public static class OhioTime
{
    /// <summary>The moment in Eastern time, or UTC if the host has no time zone data.</summary>
    public static DateTimeOffset In(DateTimeOffset when)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(when, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return when.ToUniversalTime();
        }
    }

    public static DateOnly DateOf(DateTimeOffset when) => DateOnly.FromDateTime(In(when).DateTime);
}
