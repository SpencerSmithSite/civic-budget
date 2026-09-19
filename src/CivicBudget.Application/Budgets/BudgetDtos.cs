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
    /// <summary>The full number in the government's format, "1000-725-121"; what staff type and search by.</summary>
    string AccountNumber,
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

/// <summary>
/// Where one department stands in a version: its request status, who submitted it, and its narrative.
/// One per department with lines in the version, whether or not it has started a request.
/// </summary>
public sealed record DepartmentRequestDto(
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    DepartmentRequestStatus Status,
    string? Narrative,
    DateTimeOffset? SubmittedAtUtc,
    string? SubmittedByUserName,
    string? ReturnNote,
    DateTimeOffset? ReturnedAtUtc,
    int LineCount,
    decimal PriorYearActual,
    decimal CurrentYearBudget,
    decimal Amount,
    /// <summary>Decided for the current user, like <see cref="BudgetLineDto.CanEdit"/>, so pages show buttons without rule logic.</summary>
    bool CanEditNarrative,
    bool CanSubmit,
    bool CanReturn)
{
    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

/// <summary>Everything the budget entry screens need for one version, in one round trip.</summary>
public sealed record BudgetWorkspaceDto(
    BudgetVersionSummaryDto Version,
    /// <summary>How this government writes account numbers; screens use its label for the middle segment.</summary>
    AccountNumberFormat NumberFormat,
    bool IsEditable,
    bool CanAddLines,
    IReadOnlyList<BudgetLineDto> Lines,
    IReadOnlyList<FundBalanceDto> FundBalances,
    IReadOnlyList<LookupDto> Funds,
    IReadOnlyList<LookupDto> Departments,
    IReadOnlyList<AccountLookupDto> Accounts,
    /// <summary>Every department with lines in the version (the user's own, for a department user) and where its request stands.</summary>
    IReadOnlyList<DepartmentRequestDto> DepartmentRequests)
{
    public bool AnyFundBlocksWorkflow => FundBalances.Any(f => f.Limit.BlocksWorkflow);
    public bool AnyFundOverLimit => FundBalances.Any(f => !f.Summary.IsWithinAppropriationLimit);
    public int DepartmentsSubmitted => DepartmentRequests.Count(r => r.Status == DepartmentRequestStatus.Submitted);
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
