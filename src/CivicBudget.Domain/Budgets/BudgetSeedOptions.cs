using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Budgets;

/// <summary>Which lines a percentage change applies to when a budget is started from last year's.</summary>
public enum SeedAdjustmentScope
{
    /// <summary>Every line, revenue estimates and appropriations alike.</summary>
    AllLines = 1,

    /// <summary>Expenditures and transfers out; revenue estimates carry over unchanged.</summary>
    AppropriationsOnly = 2,

    /// <summary>Revenues and transfers in; appropriations carry over unchanged.</summary>
    RevenueEstimatesOnly = 3,
}

/// <summary>
/// How a new year's budget is started from the prior year's adopted one: every line's amount becomes
/// the new year's comparative ("current year budget") and, changed by <see cref="AdjustmentPercent"/>,
/// its starting request. 3 turns an adopted $100,000 into a $103,000 request; 0 copies it as is.
/// </summary>
public sealed record BudgetSeedOptions(decimal AdjustmentPercent, SeedAdjustmentScope Scope, bool RoundToWholeDollars)
{
    /// <summary>A cut of everything (-100%) or more than doubling is almost certainly a typo.</summary>
    public const decimal MinPercent = -99.99m;

    public const decimal MaxPercent = 100m;

    public static readonly BudgetSeedOptions CopyAsIs = new(0m, SeedAdjustmentScope.AllLines, RoundToWholeDollars: false);

    public decimal Apply(decimal amount, AccountType type)
    {
        bool applies = Scope switch
        {
            SeedAdjustmentScope.AppropriationsOnly => type.IsAppropriation(),
            SeedAdjustmentScope.RevenueEstimatesOnly => type.IsResource(),
            _ => true,
        };
        decimal adjusted = applies ? amount * (1m + AdjustmentPercent / 100m) : amount;
        return RoundToWholeDollars
            ? Math.Round(adjusted, 0, MidpointRounding.AwayFromZero)
            : Money.Round(adjusted);
    }
}
