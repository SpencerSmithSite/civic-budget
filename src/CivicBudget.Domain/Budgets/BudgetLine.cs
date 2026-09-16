using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Budgets;

/// <summary>
/// One budgeted amount for a fund / department / account combination within a version.
/// Lines are created and mutated only through <see cref="BudgetVersion"/>, which owns the
/// "is this version still editable?" rule; that is why the setters here are internal.
/// </summary>
public sealed class BudgetLine : Entity, ITenantOwned
{
    public const int JustificationMaxLength = 2000;

    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public Guid FundId { get; private set; }

    /// <summary>Required for expenditure accounts; optional for revenue and transfer accounts.</summary>
    public Guid? DepartmentId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The amount budgeted (for appropriations: appropriated) in this version.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Prior fiscal year's actual — an entered/imported figure; there is no general ledger.</summary>
    public decimal PriorYearActual { get; private set; }

    /// <summary>Current fiscal year's budget, for comparison.</summary>
    public decimal CurrentYearBudget { get; private set; }

    public string? Justification { get; private set; }

    // Navigation properties. Loaded by Infrastructure when a query needs them (e.g. grouping by
    // account type); the domain never assumes they are populated.
    public Fund Fund { get; private set; } = null!;
    public Department? Department { get; private set; }
    public Account Account { get; private set; } = null!;

    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Money.PercentChange(CurrentYearBudget, Amount);

    internal BudgetLine(
        Guid governmentId,
        Guid budgetVersionId,
        Fund fund,
        Department? department,
        Account account,
        decimal amount,
        decimal priorYearActual,
        decimal currentYearBudget,
        string? justification)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        Fund = fund;
        FundId = fund.Id;
        Department = department;
        DepartmentId = department?.Id;
        Account = account;
        AccountId = account.Id;
        Amount = Money.Round(amount);
        PriorYearActual = Money.Round(priorYearActual);
        CurrentYearBudget = Money.Round(currentYearBudget);
        Justification = NormalizeJustification(justification);
    }

    private BudgetLine()
    {
    }

    internal void SetAmount(decimal amount) => Amount = Money.Round(amount);

    internal void SetComparatives(decimal priorYearActual, decimal currentYearBudget)
    {
        PriorYearActual = Money.Round(priorYearActual);
        CurrentYearBudget = Money.Round(currentYearBudget);
    }

    internal void SetJustification(string? justification) => Justification = NormalizeJustification(justification);

    /// <summary>
    /// Copies this line into another version (used when creating an amendment).
    /// Requires the navigation properties to be loaded, which the amendment service guarantees.
    /// </summary>
    internal BudgetLine CopyTo(Guid targetVersionId) =>
        new(GovernmentId, targetVersionId, Fund, Department, Account, Amount, PriorYearActual, CurrentYearBudget, Justification);

    private static string? NormalizeJustification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guard.MaxLength(value.Trim(), JustificationMaxLength, nameof(Justification));
    }
}
