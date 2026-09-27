using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Reports;

/// <summary>A revenue account the settings page can put in a column.</summary>
public sealed record ReportAccountDto(Guid Id, string Code, string Name, ReportingCategory Category, bool IsActive);

/// <summary>A revenue column on the certificate: its heading and the accounts that feed it.</summary>
public sealed record ReportColumnDto(string Label, IReadOnlyList<Guid> AccountIds);

public sealed record CertificateSettingsDto(
    string? County,
    string? FiscalOfficerName,
    string FiscalOfficerTitle,
    string BalanceLabel,
    string OtherSourcesLabel,
    IReadOnlyList<ReportColumnDto> Columns,
    /// <summary>Nothing saved yet: the columns shown are the Ohio default, one "Taxes" column of the accounts categorized as taxes.</summary>
    bool UsingDefaultColumns,
    IReadOnlyList<ReportAccountDto> RevenueAccounts);

public sealed record SaveCertificateSettingsRequest(
    string? County,
    string? FiscalOfficerName,
    string FiscalOfficerTitle,
    string BalanceLabel,
    string OtherSourcesLabel,
    IReadOnlyList<ReportColumnDto> Columns);

/// <summary>One fund's reserves and advances for the certificate's year.</summary>
public sealed record FundAdjustmentDto(Guid FundId, string FundCode, string FundName, decimal Nonspendable, decimal Reserves, decimal UnpaidAdvances);

public sealed record SaveFundAdjustmentsRequest(Guid VersionId, IReadOnlyList<FundAdjustmentDto> Funds);

/// <summary>
/// The certificate of estimated resources for a budget version, its settings, and the per-fund
/// reserves and advances it needs. The certificate covers every fund, so department users (who
/// see only their departments' lines) do not get one; the Administrator, Fiscal Officer, and
/// Viewer do. Settings and adjustments are the Administrator's and Fiscal Officer's to change.
/// </summary>
public interface ICertificateService
{
    /// <summary>Null when the version does not exist here or the user may not see every fund.</summary>
    Task<CertificateReportDto?> GetAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<IReadOnlyList<FundAdjustmentDto>?> GetAdjustmentsAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<Result> SaveAdjustmentsAsync(SaveFundAdjustmentsRequest request, CancellationToken ct = default);

    Task<CertificateSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<Result> SaveSettingsAsync(SaveCertificateSettingsRequest request, CancellationToken ct = default);
}

/// <summary>Draws the certificate as a PDF. Implemented in Infrastructure with MigraDoc.</summary>
public interface ICertificatePdfRenderer
{
    byte[] Render(CertificateReportDto certificate);
}
