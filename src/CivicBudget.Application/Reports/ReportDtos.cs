using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Reports;

/// <summary>Printed at the top of every report: which budget, which version, who ran it and when.</summary>
public sealed record ReportHeaderDto(
    Guid BudgetVersionId,
    string GovernmentName,
    int FiscalYear,
    string VersionLabel,
    BudgetStatus Status,
    string? ResolutionNumber,
    string GeneratedBy,
    DateTimeOffset GeneratedAtUtc)
{
    public string Title => $"FY{FiscalYear} {VersionLabel}";
}

/// <summary>One fund on the Budget Summary by Fund: the Ohio certificate arithmetic and whether it passes the limit.</summary>
public sealed record FundSummaryRowDto(
    string FundCode,
    string FundName,
    FundCategory? Category,
    decimal BeginningBalance,
    decimal Revenues,
    decimal TransfersIn,
    decimal Expenditures,
    decimal TransfersOut,
    bool IsWithinAppropriationLimit)
{
    public decimal EstimatedResources => BeginningBalance + Revenues + TransfersIn;
    public decimal Appropriations => Expenditures + TransfersOut;
    public decimal ProjectedEndingBalance => EstimatedResources - Appropriations;
}

public sealed record FundSummaryReportDto(ReportHeaderDto Header, IReadOnlyList<FundSummaryRowDto> Funds, FundSummaryRowDto Total)
{
    public int FundsOverLimit => Funds.Count(f => !f.IsWithinAppropriationLimit);
}

/// <summary>One account line on the Department Budget Detail.</summary>
public sealed record DetailLineDto(
    string FundCode,
    string FundName,
    string AccountCode,
    string AccountName,
    string AccountNumber,
    AccountType AccountType,
    ReportingCategory Category,
    decimal PriorYearActual,
    decimal CurrentYearBudget,
    decimal Amount,
    string? Justification)
{
    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

/// <summary>
/// A department's section: every line recorded against it, but subtotals of its expenditures only.
/// Revenue and transfer lines print for reference and stay out of the total, the same rule as every
/// other screen (<see cref="AccountTypeExtensions.CountsTowardDepartmentTotal"/>).
/// </summary>
public sealed record DepartmentDetailDto(
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    /// <summary>The department's budget message for this version, printed under its heading.</summary>
    string? Narrative,
    IReadOnlyList<DetailLineDto> Lines)
{
    public decimal PriorYearActual => Lines.Where(l => l.AccountType.CountsTowardDepartmentTotal()).Sum(l => l.PriorYearActual);
    public decimal CurrentYearBudget => Lines.Where(l => l.AccountType.CountsTowardDepartmentTotal()).Sum(l => l.CurrentYearBudget);
    public decimal Amount => Lines.Where(l => l.AccountType.CountsTowardDepartmentTotal()).Sum(l => l.Amount);
    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

public sealed record DepartmentDetailReportDto(
    ReportHeaderDto Header,
    IReadOnlyList<DepartmentDetailDto> Departments,
    /// <summary>Every department the user may pick, for the filter; department users see only their own.</summary>
    IReadOnlyList<(Guid Id, string Code, string Name)> AvailableDepartments,
    Guid? SelectedDepartmentId)
{
    public decimal TotalAmount => Departments.Sum(d => d.Amount);
    public decimal TotalCurrentYearBudget => Departments.Sum(d => d.CurrentYearBudget);
    public decimal TotalPriorYearActual => Departments.Sum(d => d.PriorYearActual);
}

/// <summary>One category on the Revenue vs. Expenditure report, with the same three comparatives as the lines.</summary>
public sealed record CategoryRowDto(string Label, decimal PriorYearActual, decimal CurrentYearBudget, decimal Amount)
{
    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

/// <summary>Revenues by source above, expenditures by category below, and the net at the bottom.</summary>
public sealed record CategoryReportDto(
    ReportHeaderDto Header,
    IReadOnlyList<CategoryRowDto> Revenues,
    CategoryRowDto RevenueTotal,
    IReadOnlyList<CategoryRowDto> Expenditures,
    CategoryRowDto ExpenditureTotal)
{
    /// <summary>Revenues less expenditures, before transfers and beginning balances (the fund report carries those).</summary>
    public CategoryRowDto Net => new(
        "Revenues less expenditures",
        RevenueTotal.PriorYearActual - ExpenditureTotal.PriorYearActual,
        RevenueTotal.CurrentYearBudget - ExpenditureTotal.CurrentYearBudget,
        RevenueTotal.Amount - ExpenditureTotal.Amount);
}
