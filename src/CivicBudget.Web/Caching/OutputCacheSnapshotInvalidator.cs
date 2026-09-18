using CivicBudget.Application.Publishing;
using Microsoft.AspNetCore.OutputCaching;

namespace CivicBudget.Web.Caching;

/// <summary>Publishing calls this; it drops every cached portal page for that government by tag.</summary>
public sealed class OutputCacheSnapshotInvalidator(IOutputCacheStore store) : IPublishedSnapshotCacheInvalidator
{
    public async Task InvalidateAsync(string governmentSlug, CancellationToken ct = default) =>
        await store.EvictByTagAsync(PortalOutputCachePolicy.TagPrefix + governmentSlug.ToLowerInvariant(), ct);
}
