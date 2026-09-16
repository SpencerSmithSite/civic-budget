namespace CivicBudget.Domain.Budgets;

/// <summary>
/// Calculated, never stored. For one fund within one budget version:
/// <code>
/// Estimated resources      = Beginning balance + Revenues + Transfers in
/// Appropriations           = Expenditures + Transfers out
/// Projected ending balance = Estimated resources − Appropriations
/// </code>
/// </summary>
public sealed record FundBalanceSummary(
    Guid FundId,
    decimal BeginningBalance,
    decimal Revenues,
    decimal TransfersIn,
    decimal Expenditures,
    decimal TransfersOut)
{
    public decimal EstimatedResources => BeginningBalance + Revenues + TransfersIn;
    public decimal Appropriations => Expenditures + TransfersOut;
    public decimal ProjectedEndingBalance => EstimatedResources - Appropriations;

    /// <summary>The Ohio rule: appropriations may not exceed estimated resources.</summary>
    public bool IsWithinAppropriationLimit => Appropriations <= EstimatedResources;

    public decimal AmountOverLimit => Math.Max(0m, Appropriations - EstimatedResources);
}
