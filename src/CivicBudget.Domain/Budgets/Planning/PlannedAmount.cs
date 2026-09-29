using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Budgets.Planning;

/// <summary>
/// A future year's amount typed over the calculation for one budget line. Only typed amounts are
/// stored; every other future year is worked out from the year before it
/// (<see cref="MultiYearPlanCalculator"/>), so a change to this year's budget or to a percentage flows
/// through the years nobody has typed. <see cref="YearOffset"/> counts from the budget year.
/// </summary>
[Audited]
public sealed class PlannedAmount : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid BudgetLineId { get; private set; }
    public int YearOffset { get; private set; }
    public decimal Amount { get; private set; }

    internal PlannedAmount(Guid governmentId, Guid budgetLineId, int yearOffset, decimal amount)
    {
        GovernmentId = governmentId;
        BudgetLineId = budgetLineId;
        YearOffset = yearOffset;
        Amount = amount;
    }

    private PlannedAmount()
    {
    }

    internal void SetAmount(decimal amount) => Amount = amount;

    internal PlannedAmount CopyTo(Guid targetLineId, int yearOffset) => new(GovernmentId, targetLineId, yearOffset, Amount);
}
