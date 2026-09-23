using CivicBudget.Web.Caching;
using Microsoft.AspNetCore.OutputCaching;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>Publishing must drop exactly that government's pages: same tag the policy wrote, slug normalized the same way.</summary>
public class OutputCacheSnapshotInvalidatorTests
{
    [Fact]
    public async Task Evicts_the_governments_pages_and_the_portal_index()
    {
        var store = new RecordingStore();
        var invalidator = new OutputCacheSnapshotInvalidator(store);

        await invalidator.InvalidateAsync("Maple-Ridge-OH");

        Assert.Equal(["portal:maple-ridge-oh", PortalOutputCachePolicy.IndexTag], store.EvictedTags); // the index lists who has published
    }

    private sealed class RecordingStore : IOutputCacheStore
    {
        public List<string> EvictedTags { get; } = [];

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            EvictedTags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
