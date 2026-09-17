using CivicBudget.Application.Publishing;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>Placeholder until Phase 5 adds output caching for the portal; publishing calls it regardless.</summary>
public sealed class NoOpSnapshotCacheInvalidator : IPublishedSnapshotCacheInvalidator
{
    public Task InvalidateAsync(string governmentSlug, CancellationToken ct = default) => Task.CompletedTask;
}
