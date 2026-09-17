using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Budgets;

/// <summary>One row of the version picker.</summary>
public sealed record BudgetVersionSummaryDto(
    Guid Id,
    int Year,
    int VersionNumber,
    string Label,
    BudgetStatus Status,
    string? AmendmentReason,
    string? ResolutionNumber,
    int LineCount);

/// <summary>
/// A budget line as the screens see it: codes and names denormalized, derived figures computed,
/// and <see cref="CanEdit"/> already decided for the current user so components need no rule logic.
/// </summary>
public sealed record BudgetLineDto(
    Guid Id,
    Guid FundId,
    string FundCode,
    string FundName,
    Guid? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    ReportingCategory Category,
    decimal Amount,
    decimal PriorYearActual,
    decimal CurrentYearBudget,
    string? Justification,
    bool CanEdit)
{
    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

/// <summary>Per-fund balance with the appropriation limit already evaluated for the government's mode.</summary>
public sealed record FundBalanceDto(
    Guid FundId,
    string FundCode,
    string FundName,
    FundCategory Category,
    FundBalanceSummary Summary,
    AppropriationLimitResult Limit,
    bool CanEditBeginningBalance);

/// <summary>Everything the budget entry screens need for one version, in one round trip.</summary>
public sealed record BudgetWorkspaceDto(
    BudgetVersionSummaryDto Version,
    bool IsEditable,
    bool CanAddLines,
    IReadOnlyList<BudgetLineDto> Lines,
    IReadOnlyList<FundBalanceDto> FundBalances,
    IReadOnlyList<LookupDto> Funds,
    IReadOnlyList<LookupDto> Departments,
    IReadOnlyList<AccountLookupDto> Accounts)
{
    public bool AnyFundBlocksWorkflow => FundBalances.Any(f => f.Limit.BlocksWorkflow);
    public bool AnyFundOverLimit => FundBalances.Any(f => !f.Summary.IsWithinAppropriationLimit);
}

public sealed record LookupDto(Guid Id, string Code, string Name)
{
    public string Display => $"{Code} {Name}";
}

public sealed record AccountLookupDto(Guid Id, string Code, string Name, AccountType Type, ReportingCategory Category)
{
    public string Display => $"{Code} {Name}";
}

public sealed record AddBudgetLineRequest(
    Guid BudgetVersionId,
    Guid FundId,
    Guid? DepartmentId,
    Guid AccountId,
    decimal Amount,
    decimal PriorYearActual,
    decimal CurrentYearBudget,
    string? Justification);
