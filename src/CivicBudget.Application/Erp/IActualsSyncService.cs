using CivicBudget.Application.Common;

namespace CivicBudget.Application.Erp;

/// <summary>A fiscal year whose actuals are here, and how far into it they run.</summary>
public sealed record ActualsYearDto(int FiscalYear, int ThroughPeriod, DateOnly AsOf, DateTimeOffset SyncedAtUtc, string UserName, string SourceName)
{
    public bool IsClosed => ThroughPeriod == 12;
}

/// <summary>What the actuals page needs up front: whether an API connection exists, which years it can fetch, and what is already here.</summary>
public sealed record ActualsStatusDto(string? ApiName, IReadOnlyList<int> FetchableYears, IReadOnlyList<ActualsYearDto> Years);

/// <summary>One fund's figures in a preview.</summary>
public sealed record ActualsFundDto(string FundCode, string FundName, decimal Receipts, decimal Disbursements, decimal Encumbered, decimal? Cash);

/// <summary>A budget line whose prior-year actual the sync would change.</summary>
public sealed record PriorActualChangeDto(string Version, string AccountNumber, string AccountName, decimal Before, decimal After);

/// <summary>What a sync would bring in and what it would change, before anything is written.</summary>
public sealed record ActualsPreviewDto(
    string SourceName,
    string? FileName,
    int FiscalYear,
    int ThroughPeriod,
    DateOnly AsOf,
    int ActivityRows,
    decimal Receipts,
    decimal Disbursements,
    decimal Encumbered,
    decimal Cash,
    IReadOnlyList<ActualsFundDto> Funds,
    IReadOnlyList<PriorActualChangeDto> PriorActualChanges,
    /// <summary>Things worth reading before applying: an older export than what is here, a negative total left alone.</summary>
    IReadOnlyList<string> Notes,
    /// <summary>The figures this sync would replace, when the year is already here.</summary>
    ActualsYearDto? Replaces)
{
    public bool IsYearClosed => ThroughPeriod == 12;
}

/// <summary>One row of the actuals sync log.</summary>
public sealed record ActualsSyncDto(
    Guid Id, int FiscalYear, int ThroughPeriod, DateOnly AsOf, string SourceName, string? FileName, DateTimeOffset SyncedAtUtc, string UserName,
    int ActivityRows, decimal Receipts, decimal Disbursements, decimal Encumbered, decimal Cash, int PriorActualsUpdated);

/// <summary>
/// Brings a fiscal year of actuals in from the parent ERP, from an uploaded export or straight from
/// its API. Preview shows the totals by fund and every prior-year actual the sync would change;
/// commit replaces that year's figures, writes the changed prior-year actuals into open budgets
/// (audited like any other change), and logs the sync. Administrators and the Fiscal Officer.
/// </summary>
public interface IActualsSyncService
{
    Task<ActualsStatusDto> StatusAsync(CancellationToken ct = default);

    Task<Result<ActualsPreviewDto>> PreviewFileAsync(string fileName, Stream content, CancellationToken ct = default);

    Task<Result<ActualsSyncDto>> CommitFileAsync(string fileName, Stream content, CancellationToken ct = default);

    Task<Result<ActualsPreviewDto>> PreviewFromErpAsync(int fiscalYear, CancellationToken ct = default);

    Task<Result<ActualsSyncDto>> CommitFromErpAsync(int fiscalYear, CancellationToken ct = default);

    Task<IReadOnlyList<ActualsSyncDto>> HistoryAsync(CancellationToken ct = default);
}
