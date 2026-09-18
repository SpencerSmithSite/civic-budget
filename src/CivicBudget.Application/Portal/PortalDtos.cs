using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Portal;

/// <summary>A published budget year as the portal lists it.</summary>
public sealed record PortalYearDto(int FiscalYear, string VersionLabel, DateTimeOffset PublishedAtUtc);

/// <summary>Header for one published budget: everything the page needs to introduce it and cite it.</summary>
public sealed record PortalBudgetDto(
    Guid SnapshotId,
    string GovernmentSlug,
    string GovernmentName,
    string? GovernmentDescription,
    int FiscalYear,
    string VersionLabel,
    string? AmendmentReason,
    string? ResolutionNumber,
    DateTimeOffset? AdoptedOnUtc,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<PortalYearDto> AvailableYears,
    decimal TotalRevenues,
    decimal TotalExpenditures,
    decimal TotalTransfersIn,
    decimal TotalTransfersOut,
    decimal TotalBeginningBalance,
    decimal PriorYearRevenues,
    decimal PriorYearExpenditures)
{
    /// <summary>Across all funds: beginning balances + revenues + transfers in - expenditures - transfers out.</summary>
    public decimal ProjectedEndingBalance => TotalBeginningBalance + TotalRevenues + TotalTransfersIn - TotalExpenditures - TotalTransfersOut;
}

/// <summary>One bar in a breakdown: a label, its amount, its share of the total, and where clicking it goes.</summary>
public sealed record BreakdownItemDto(string Key, string Label, string? Description, decimal Amount, decimal PriorYearAmount)
{
    public decimal? ChangePercent => Domain.Common.Money.PercentChange(PriorYearAmount, Amount);
}

public sealed record BreakdownDto(string Title, decimal Total, IReadOnlyList<BreakdownItemDto> Items)
{
    public decimal ShareOf(BreakdownItemDto item) => Total == 0m ? 0m : Math.Round(item.Amount / Total * 100m, 1, MidpointRounding.AwayFromZero);
}

/// <summary>A fund's page: its description, balance arithmetic, and breakdowns of what goes in and out.</summary>
public sealed record PortalFundDto(
    string Code,
    string Name,
    FundCategory Category,
    string? Description,
    decimal BeginningBalance,
    decimal Revenues,
    decimal TransfersIn,
    decimal Expenditures,
    decimal TransfersOut,
    BreakdownDto ExpendituresByDepartment,
    BreakdownDto ExpendituresByCategory,
    BreakdownDto RevenuesByCategory)
{
    public decimal EstimatedResources => BeginningBalance + Revenues + TransfersIn;
    public decimal Appropriations => Expenditures + TransfersOut;
    public decimal ProjectedEndingBalance => EstimatedResources - Appropriations;
}

/// <summary>A department within a fund (or across funds when FundCode is null).</summary>
public sealed record PortalDepartmentDto(
    string Code,
    string Name,
    string? Description,
    string? FundCode,
    string? FundName,
    decimal Expenditures,
    BreakdownDto ByCategory,
    IReadOnlyList<PortalLineDto> Lines);

/// <summary>An account line as citizens see it.</summary>
public sealed record PortalLineDto(
    string FundCode,
    string FundName,
    string? DepartmentCode,
    string? DepartmentName,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    ReportingCategory Category,
    decimal Amount,
    decimal PriorYearActual,
    decimal CurrentYearBudget);

public sealed record PortalSearchHitDto(string Kind, string Label, string Url, decimal Amount);

/// <summary>One published year's totals, for the year-over-year view.</summary>
public sealed record YearTotalsDto(int FiscalYear, string VersionLabel, decimal Revenues, decimal Expenditures, decimal EndingBalance);
