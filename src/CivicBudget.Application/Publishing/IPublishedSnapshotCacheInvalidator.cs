namespace CivicBudget.Application.Publishing;

/// <summary>
/// Called after a publish or unpublish so cached portal pages for that government are dropped.
/// The Web project implements it with ASP.NET Core output caching (evict by tag, ADR-0021);
/// Infrastructure registers a no-op for hosts without a web pipeline (tests). The Application
/// layer only knows that "something cached must be refreshed".
/// </summary>
public interface IPublishedSnapshotCacheInvalidator
{
    Task InvalidateAsync(string governmentSlug, CancellationToken ct = default);
}
