using CivicBudget.Domain.Personnel;

namespace CivicBudget.Domain.Tests.Personnel;

public class LongevityTests
{
    private static LongevityRule Schedule(LongevityMethod method, int? maxYears, params LongevityStepRule[] steps) =>
        new(Guid.CreateVersion7(), "Contract", method, ServiceCountedOn.FirstDayOfYear, maxYears, null, steps);

    [Fact]
    public void Step_table_reads_as_ranges_of_years()
    {
        LongevityRule schedule = Schedule(LongevityMethod.FlatAmount, null, new(5, 500m), new(10, 750m), new(15, 1_000m));

        Assert.Equal(
        [
            "Fewer than 5 years of service: no longevity.",
            "5 to 9 years of service: $500.00 a year.",
            "10 to 14 years of service: $750.00 a year.",
            "15 or more years of service: $1,000.00 a year.",
            "Years of service are counted on the first day of the budget year.",
        ], Longevity.Describe(schedule));
    }

    [Fact]
    public void Percent_and_per_year_schedules_say_what_they_pay()
    {
        Assert.Contains("5 or more years of service: 2.5% of base pay.", Longevity.Describe(Schedule(LongevityMethod.PercentOfPay, null, new LongevityStepRule(5, 2.5m))));

        IReadOnlyList<string> perYear = Longevity.Describe(Schedule(LongevityMethod.AmountPerYear, 25, new LongevityStepRule(5, 100m)));
        Assert.Contains("5 or more years of service: $100.00 for each year of service.", perYear);
        Assert.Contains("Years after 25 do not add more.", perYear);
    }

    [Fact]
    public void A_schedule_from_year_zero_has_no_nobody_line()
    {
        IReadOnlyList<string> lines = Longevity.Describe(Schedule(LongevityMethod.FlatAmount, null, new(0, 100m), new(1, 200m)));

        Assert.Equal("0 years of service: $100.00 a year.", lines[0]);
        Assert.Equal("1 or more years of service: $200.00 a year.", lines[1]);
    }

    [Fact]
    public void Amount_is_zero_before_the_first_step()
    {
        LongevityRule schedule = Schedule(LongevityMethod.AmountPerYear, null, new LongevityStepRule(5, 100m));

        Assert.Equal(0m, Longevity.Amount(schedule, 4, 60_000m));
        Assert.Equal(500m, Longevity.Amount(schedule, 5, 60_000m));
    }
}
