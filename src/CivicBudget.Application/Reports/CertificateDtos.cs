using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Reports;

/// <summary>The document controls the certificate prints at the top and signs at the bottom.</summary>
public sealed record CertificateHeaderDto(
    Guid BudgetVersionId,
    string GovernmentName,
    string? County,
    int FiscalYear,
    string VersionLabel,
    int VersionNumber,
    BudgetStatus Status,
    string? ResolutionNumber,
    DateTimeOffset? AdoptedOnUtc,
    string? FiscalOfficerName,
    string FiscalOfficerTitle,
    string GeneratedBy,
    DateTimeOffset GeneratedAtUtc)
{
    /// <summary>An amendment of the budget goes with an amended certificate, numbered the same way.</summary>
    public string Title => VersionNumber == 1 ? "Certificate of Estimated Resources" : $"Amended Certificate of Estimated Resources No. {VersionNumber - 1}";
}

/// <summary>
/// One fund on the certificate. The detailed schedule shows every column; the issued certificate
/// condenses the same row to the balance, the revenue columns, and the total, so the two views
/// cannot disagree.
/// </summary>
public sealed record CertificateRowDto(
    string FundCode,
    string FundName,
    FundCategory? Category,
    /// <summary>Cash at the end of the prior year, from the ERP; null when the carryover is the budget's estimate.</summary>
    decimal? Cash,
    decimal? Encumbrances,
    decimal Nonspendable,
    decimal Reserves,
    /// <summary>Positive for a fund that lent, negative for a fund that borrowed.</summary>
    decimal UnpaidAdvances,
    /// <summary>The unencumbered balance available for appropriation at the start of the year.</summary>
    decimal Carryover,
    /// <summary>One amount per revenue column (the report groups), in the columns' order.</summary>
    IReadOnlyList<decimal> RevenueColumns,
    decimal OtherSources,
    decimal Appropriations,
    /// <summary>What the budget itself starts the fund with, to reconcile against the carryover.</summary>
    decimal BudgetBeginningBalance)
{
    public decimal EstimatedRevenue => RevenueColumns.Sum() + OtherSources;
    public decimal TotalAvailable => Carryover + EstimatedRevenue;
    public bool IsWithinLimit => Appropriations <= TotalAvailable;
    public bool BalanceMatchesBudget => Carryover == BudgetBeginningBalance;
}

/// <summary>A fund type with its funds and their subtotal.</summary>
public sealed record CertificateSectionDto(string Label, IReadOnlyList<CertificateRowDto> Funds, CertificateRowDto Subtotal);

/// <summary>A reconciliation the certificate shows, passed or not, with the figures behind it.</summary>
public sealed record CertificateCheckDto(string Label, bool Passed, string Detail);

/// <summary>A revenue estimate that moved since the prior certificate, with the reason typed on the line.</summary>
public sealed record RevenueChangeDto(string FundCode, string AccountNumber, string AccountName, decimal Prior, decimal Now, string? Justification)
{
    public decimal Change => Now - Prior;
}

public sealed record CertificateReportDto(
    CertificateHeaderDto Header,
    string BalanceLabel,
    IReadOnlyList<string> RevenueColumnLabels,
    string OtherSourcesLabel,
    /// <summary>True when the carryover comes from the ERP's closed prior year; false when it is the budget's estimate.</summary>
    bool CarryoverFromErp,
    DateOnly CarryoverAsOf,
    IReadOnlyList<CertificateSectionDto> Sections,
    CertificateRowDto Total,
    IReadOnlyList<CertificateCheckDto> Checks,
    /// <summary>The version this one amends, or null for an original certificate.</summary>
    string? PriorLabel,
    IReadOnlyList<RevenueChangeDto> Changes,
    IReadOnlyList<string> Notes)
{
    public IEnumerable<CertificateRowDto> Funds => Sections.SelectMany(s => s.Funds);
}

// ---- what the builder is given --------------------------------------------------------------

/// <summary>A resource line (revenue or transfer in) of the version, or of the version it amends.</summary>
public sealed record CertificateResourceLine(Guid FundId, Guid? DepartmentId, Guid AccountId, string AccountNumber, string AccountName, AccountType Type, decimal Amount, string? Justification);

/// <summary>A fund as the certificate needs it.</summary>
public sealed record CertificateFund(Guid Id, string Code, string Name, FundCategory Category, decimal BudgetBeginningBalance, decimal Appropriations);

/// <summary>A revenue column: its heading and the accounts that feed it.</summary>
public sealed record CertificateColumn(string Label, IReadOnlySet<Guid> AccountIds);

/// <summary>The prior year's closing figures from the ERP, when it has closed that year.</summary>
public sealed record CertificateCarryover(DateOnly AsOf, IReadOnlyDictionary<Guid, decimal> Cash, IReadOnlyDictionary<Guid, decimal> Encumbrances);

public sealed record CertificateAdjustment(decimal Nonspendable, decimal Reserves, decimal UnpaidAdvances);

public sealed record CertificateInputs(
    CertificateHeaderDto Header,
    string BalanceLabel,
    string OtherSourcesLabel,
    IReadOnlyList<CertificateColumn> Columns,
    /// <summary>True when no columns are saved and the report is using the Ohio default (accounts categorized as Taxes).</summary>
    bool DefaultColumns,
    IReadOnlyList<CertificateFund> Funds,
    IReadOnlyList<CertificateResourceLine> Resources,
    DateOnly FiscalYearStart,
    CertificateCarryover? Carryover,
    IReadOnlyDictionary<Guid, CertificateAdjustment> Adjustments,
    string? PriorLabel,
    IReadOnlyList<CertificateResourceLine> PriorResources);
