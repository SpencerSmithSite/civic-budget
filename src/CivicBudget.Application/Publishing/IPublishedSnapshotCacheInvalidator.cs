namespace CivicBudget.Application.Publishing;

/// <summary>
/// Called after a publish or unpublish so cached portal pages for that government are dropped.
/// Phase 5 implements it with ASP.NET Core output caching (evict by tag); until then the Web project
/// registers a no-op. The Application layer only knows that "something cached must be refreshed".
/// </summary>
public interface IPublishedSnapshotCacheInvalidator
{
    Task InvalidateAsync(string governmentSlug, CancellationToken ct = default);
}
