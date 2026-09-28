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
[Audited]
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

    /// <summary>
    /// What was actually received or spent in the last closed year (two before the budget year), for
    /// comparison. The ERP's ledger is the source of truth: once the ERP has sent that year closed, the
    /// actuals sync writes it here; until then it is typed in or imported.
    /// </summary>
    public decimal PriorYearActual { get; private set; }

    /// <summary>Current fiscal year's budget, for comparison.</summary>
    public decimal CurrentYearBudget { get; private set; }

    public string? Justification { get; private set; }

    /// <summary>
    /// How many of the department's positions this line's amount is calculated from, or null for a
    /// line whose amount is typed. A calculated line changes only when its positions or the year's
    /// personnel settings do, so nobody can type over it and leave the positions saying something else.
    /// </summary>
    public int? PositionCount { get; private set; }

    public bool IsFromPersonnel => PositionCount is not null;

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
        Amount = ValidAmount(amount, nameof(amount));
        PriorYearActual = ValidAmount(priorYearActual, nameof(priorYearActual));
        CurrentYearBudget = ValidAmount(currentYearBudget, nameof(currentYearBudget));
        Justification = NormalizeJustification(justification);
    }

    private BudgetLine()
    {
    }

    internal void SetAmount(decimal amount) => Amount = ValidAmount(amount, nameof(amount));

    internal void SetFromPersonnel(decimal amount, int positionCount)
    {
        Guard.Against(positionCount < 1, "A calculated line comes from at least one position.");
        Amount = ValidAmount(amount, nameof(amount));
        PositionCount = positionCount;
    }

    /// <summary>
    /// No position costs into this line any more: it goes back to being a typed line, at zero, so the
    /// comparison with this year and last stays on the page and the line can be typed into or removed.
    /// </summary>
    internal void ReleaseFromPersonnel()
    {
        Amount = 0m;
        PositionCount = null;
    }

    internal void SetComparatives(decimal priorYearActual, decimal currentYearBudget)
    {
        decimal prior = ValidAmount(priorYearActual, nameof(priorYearActual));
        decimal current = ValidAmount(currentYearBudget, nameof(currentYearBudget));
        (PriorYearActual, CurrentYearBudget) = (prior, current);
    }

    /// <summary>
    /// Every amount on a line is a positive number: revenues and expenditures alike are entered as
    /// what was (or will be) received or spent, and the account type says which way it counts. A
    /// refund or correction is a lower amount, not a negative one. Services and the import check
    /// this first so the user sees a message; this is the rule itself.
    /// </summary>
    private static decimal ValidAmount(decimal amount, string name)
    {
        Guard.Against(amount < 0m, $"Budget amounts cannot be negative ({name}).");
        Guard.Against(!Money.IsStorable(amount), $"{Money.TooLargeMessage} ({name})");
        return Money.Round(amount);
    }

    internal void SetJustification(string? justification) => Justification = NormalizeJustification(justification);

    /// <summary>
    /// Copies this line into another version (used when creating an amendment).
    /// Requires the navigation properties to be loaded, which the amendment service guarantees.
    /// </summary>
    internal BudgetLine CopyTo(Guid targetVersionId) =>
        new(GovernmentId, targetVersionId, Fund, Department, Account, Amount, PriorYearActual, CurrentYearBudget, Justification)
        {
            PositionCount = PositionCount,
        };

    private static string? NormalizeJustification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guard.MaxLength(value.Trim(), JustificationMaxLength, nameof(Justification));
    }
}
