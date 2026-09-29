using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Budgets.Planning;

/// <summary>
/// How one future year of a budget version's multi-year plan follows from the year before it:
/// revenues and transfers in change by <see cref="RevenuePercent"/>, expenditures and transfers out by
/// <see cref="ExpenditurePercent"/>. <see cref="YearOffset"/> counts from the budget year (1 is the
/// year after it). A year with no assumption carries the year before it forward unchanged.
/// </summary>
[Audited]
public sealed class PlanAssumption : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public int YearOffset { get; private set; }
    public decimal RevenuePercent { get; private set; }
    public decimal ExpenditurePercent { get; private set; }

    internal PlanAssumption(Guid governmentId, Guid budgetVersionId, int yearOffset, decimal revenuePercent, decimal expenditurePercent)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        YearOffset = yearOffset;
        RevenuePercent = revenuePercent;
        ExpenditurePercent = expenditurePercent;
    }

    private PlanAssumption()
    {
    }

    internal void Set(decimal revenuePercent, decimal expenditurePercent) =>
        (RevenuePercent, ExpenditurePercent) = (revenuePercent, expenditurePercent);

    internal PlanAssumption CopyTo(Guid targetVersionId, int yearOffset) =>
        new(GovernmentId, targetVersionId, yearOffset, RevenuePercent, ExpenditurePercent);
}
