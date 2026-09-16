using CivicBudget.Domain.FiscalYears;

namespace CivicBudget.Domain.Tests;

public class FiscalPeriodTests
{
    [Fact]
    public void Calendar_year_starts_january_first_of_the_label_year()
    {
        FiscalPeriod period = FiscalPeriod.For(2027, startMonth: 1);

        Assert.Equal(new DateOnly(2027, 1, 1), period.Start);
        Assert.Equal(new DateOnly(2027, 12, 31), period.End);
    }

    [Fact]
    public void July_start_ends_in_the_label_year()
    {
        FiscalPeriod period = FiscalPeriod.For(2027, startMonth: 7);

        Assert.Equal(new DateOnly(2026, 7, 1), period.Start);
        Assert.Equal(new DateOnly(2027, 6, 30), period.End);
    }

    [Fact]
    public void Period_handles_leap_years()
    {
        FiscalPeriod period = FiscalPeriod.For(2028, startMonth: 3);

        Assert.Equal(new DateOnly(2027, 3, 1), period.Start);
        Assert.Equal(new DateOnly(2028, 2, 29), period.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Start_month_must_be_a_calendar_month(int month) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FiscalPeriod.For(2027, month));

    [Fact]
    public void Contains_is_inclusive_at_both_ends()
    {
        FiscalPeriod period = FiscalPeriod.For(2027, 7);

        Assert.True(period.Contains(new DateOnly(2026, 7, 1)));
        Assert.True(period.Contains(new DateOnly(2027, 6, 30)));
        Assert.False(period.Contains(new DateOnly(2027, 7, 1)));
    }

    [Fact]
    public void FiscalYear_label_uses_the_ending_year()
    {
        var fiscalYear = new FiscalYear(TestData.GovernmentId, 2027, fiscalYearStartMonth: 7);

        Assert.Equal("FY2027", fiscalYear.Label);
        Assert.Equal(new DateOnly(2026, 7, 1), fiscalYear.StartDate);
    }
}
