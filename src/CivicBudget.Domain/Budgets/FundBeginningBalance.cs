using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Budgets;

/// <summary>
/// The estimated unencumbered fund balance at the start of the fiscal year, per fund, per version.
/// Together with revenue and transfer-in lines this forms the fund's estimated resources.
/// </summary>
public sealed class FundBeginningBalance : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public Guid FundId { get; private set; }
    public decimal Amount { get; private set; }

    internal FundBeginningBalance(Guid governmentId, Guid budgetVersionId, Guid fundId, decimal amount)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        FundId = fundId;
        Amount = Money.Round(amount);
    }

    private FundBeginningBalance()
    {
    }

    internal void SetAmount(decimal amount) => Amount = Money.Round(amount);

    internal FundBeginningBalance CopyTo(Guid targetVersionId) => new(GovernmentId, targetVersionId, FundId, Amount);
}
