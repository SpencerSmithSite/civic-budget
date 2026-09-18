using CivicBudget.Application.Common;
using CivicBudget.Domain.Publishing;

namespace CivicBudget.Application.Publishing;

public sealed record SnapshotSummaryDto(
    Guid Id,
    Guid BudgetVersionId,
    int FiscalYear,
    string VersionLabel,
    SnapshotStatus Status,
    DateTimeOffset PublishedAtUtc,
    string PublishedByUserName,
    DateTimeOffset? StatusChangedAtUtc,
    int LineCount);

/// <summary>
/// Publish an adopted version to the public portal as an immutable snapshot, or withdraw one.
/// Finance Director only. Publishing a fiscal year that already has an active snapshot supersedes
/// it, so citizens always see exactly one budget per year and history is never lost.
/// </summary>
public interface IPublishingService
{
    Task<IReadOnlyList<SnapshotSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<Result<Guid>> PublishAsync(Guid versionId, CancellationToken ct = default);
    Task<Result> UnpublishAsync(Guid snapshotId, CancellationToken ct = default);
}
