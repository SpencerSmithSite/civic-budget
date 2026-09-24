using CivicBudget.Application.Publishing;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// The default when there is no web pipeline and so no cached pages to drop (tests, the seeder).
/// The Web project replaces it with the output-cache version, so services can always call it.
/// </summary>
public sealed class NoOpSnapshotCacheInvalidator : IPublishedSnapshotCacheInvalidator
{
    public Task InvalidateAsync(string governmentSlug, CancellationToken ct = default) => Task.CompletedTask;
}
