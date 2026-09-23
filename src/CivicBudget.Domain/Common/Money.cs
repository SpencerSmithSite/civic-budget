namespace CivicBudget.Domain.Common;

/// <summary>
/// Money rules in one place. Amounts are <see cref="decimal"/> (never double/float) and are stored
/// to two decimal places. Rounding is "away from zero" (the behavior finance staff see in Excel)
/// rather than .NET's default banker's rounding, which would turn 0.125 into 0.12.
/// </summary>
public static class Money
{
    public const int Scale = 2;

    /// <summary>
    /// The largest amount a <c>decimal(18,2)</c> column holds. Anything bigger is a typo, and letting
    /// it through fails the save with a database overflow instead of a message.
    /// </summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public const string TooLargeMessage = "That amount is too large.";

    /// <summary>Whether an amount (of either sign) fits in the database.</summary>
    public static bool IsStorable(decimal amount) => Math.Abs(amount) <= MaxAmount;

    /// <summary>Rounds an amount to cents.</summary>
    public static decimal Round(decimal amount) =>
        Math.Round(amount, Scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Percent change from <paramref name="baseline"/> to <paramref name="proposed"/>, rounded to one
    /// decimal place. Returns <c>null</c> when the baseline is zero because the change is undefined
    /// (a new line item is "new", not "+∞%").
    /// </summary>
    public static decimal? PercentChange(decimal baseline, decimal proposed)
    {
        if (baseline == 0m)
        {
            return null;
        }

        decimal ratio = (proposed - baseline) / Math.Abs(baseline) * 100m;
        return Math.Round(ratio, 1, MidpointRounding.AwayFromZero);
    }
}
