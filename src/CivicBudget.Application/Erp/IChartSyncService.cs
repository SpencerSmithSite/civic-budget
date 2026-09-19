using CivicBudget.Application.Common;
using CivicBudget.Domain.Erp;

namespace CivicBudget.Application.Erp;

/// <summary>Where the chart comes from today and when it last arrived; the setup screens read this to decide what to show.</summary>
public sealed record ChartSourceStatusDto(ChartSource Source, DateTimeOffset? LastSyncedAtUtc, string? LastSyncedBy, string? LastSyncSource);

/// <summary>One row of the sync log.</summary>
public sealed record ChartSyncDto(Guid Id, string SourceName, string FileName, DateTimeOffset SyncedAtUtc, string UserName,
    int Added, int Updated, int Deactivated, int Reactivated, int Unchanged, IReadOnlyList<ChartChange> Changes);

/// <summary>
/// Brings the chart of accounts in from the parent ERP. Preview shows what would change; commit
/// applies it through the fund, department, and account entities (so the audit interceptor records
/// every field change), writes a sync log row and an audit event, and switches the government's
/// chart source to the ERP. Codes that the ERP no longer lists are deactivated, never deleted.
/// Administrators and the Fiscal Officer (<c>Policies.CanMaintainSetup</c>).
/// </summary>
public interface IChartSyncService
{
    Task<ChartSourceStatusDto> StatusAsync(CancellationToken ct = default);

    Task<Result<ChartSyncPreviewDto>> PreviewAsync(string fileName, Stream content, CancellationToken ct = default);

    Task<Result<ChartSyncDto>> CommitAsync(string fileName, Stream content, CancellationToken ct = default);

    Task<IReadOnlyList<ChartSyncDto>> HistoryAsync(CancellationToken ct = default);

    /// <summary>Administrator only: hand maintenance back to the setup screens, or to the ERP.</summary>
    Task<Result> SetChartSourceAsync(ChartSource source, CancellationToken ct = default);
}
