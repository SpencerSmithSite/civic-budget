using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Erp;

namespace CivicBudget.Web.Tests.Chart;

/// <summary>A chart source status the list pages can read; local by default so existing tests see the edit buttons.</summary>
internal sealed class FakeChartSyncService(ChartSource source = ChartSource.Local, DateTimeOffset? lastSync = null) : IChartSyncService
{
    public Task<ChartSourceStatusDto> StatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new ChartSourceStatusDto(source, lastSync, lastSync is null ? null : "Dana Whitfield", lastSync is null ? null : "ERP chart export file"));

    public Task<Result<ChartSyncPreviewDto>> PreviewAsync(string fileName, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result<ChartSyncDto>> CommitAsync(string fileName, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ChartSyncDto>> HistoryAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChartSyncDto>>([]);
    public Task<Result> SetChartSourceAsync(ChartSource source, CancellationToken ct = default) => throw new NotSupportedException();
}
