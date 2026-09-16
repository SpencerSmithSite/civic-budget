using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(0.125, 0.13)]     // banker's rounding would give 0.12
    [InlineData(-0.125, -0.13)]   // symmetric: away from zero, not toward positive
    [InlineData(2.675, 2.68)]
    [InlineData(1.005, 1.01)]
    [InlineData(10, 10)]
    [InlineData(1234.5678, 1234.57)]
    public void Round_uses_away_from_zero_to_two_places(decimal input, decimal expected) =>
        Assert.Equal(expected, Money.Round(input));

    [Theory]
    [InlineData(100, 110, 10.0)]
    [InlineData(100, 87.5, -12.5)]
    [InlineData(3, 4, 33.3)]         // 33.333… → one decimal place
    [InlineData(3, 3.5, 16.7)]       // 16.666… rounds up
    [InlineData(8, 9, 12.5)]         // exactly representable, no rounding
    [InlineData(200, 200, 0.0)]
    [InlineData(-100, -50, 50.0)]    // negative baseline: change measured against its magnitude
    public void PercentChange_rounds_to_one_decimal_place(decimal baseline, decimal proposed, decimal expected) =>
        Assert.Equal(expected, Money.PercentChange(baseline, proposed));

    [Fact]
    public void PercentChange_is_undefined_when_baseline_is_zero() =>
        Assert.Null(Money.PercentChange(0m, 5000m));

    [Fact]
    public void PercentChange_midpoint_rounds_away_from_zero()
    {
        // 100 → 100.05 is exactly +0.05%, the midpoint between 0.0 and 0.1.
        Assert.Equal(0.1m, Money.PercentChange(100m, 100.05m));
        Assert.Equal(-0.1m, Money.PercentChange(100m, 99.95m));
    }
}
