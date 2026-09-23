using CivicBudget.Web.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// The caching rules are the portal's main safety property: an anonymous citizen page may be cached
/// for hours, but nothing personal (the admin app, sign-in, a response that sets a cookie) ever is.
/// The policy is plain code over an <see cref="OutputCacheContext"/>, so it tests without a server.
/// </summary>
public class PortalOutputCachePolicyTests
{
    private readonly PortalOutputCachePolicy _policy = new();

    [Theory]
    [InlineData("/transparency/maple-ridge-oh", "maple-ridge-oh")]
    [InlineData("/transparency/Maple-Ridge-OH/2026/funds/1000", "maple-ridge-oh")]
    [InlineData("/transparency/pine-hollow-twp-oh/2026/search", "pine-hollow-twp-oh")]
    public async Task Portal_get_requests_are_cached_and_tagged_with_the_government_slug(string path, string slug)
    {
        OutputCacheContext context = Context("GET", path, "?show=pct");

        await _policy.CacheRequestAsync(context, CancellationToken.None);

        Assert.True(context.EnableOutputCaching);
        Assert.True(context.AllowCacheLookup);
        Assert.True(context.AllowCacheStorage);
        Assert.Equal(PortalOutputCachePolicy.Lifetime, context.ResponseExpirationTimeSpan);
        Assert.Equal("q,show,view", string.Join(",", context.CacheVaryByRules.QueryKeys.ToArray())); // only the keys pages read, so ?x=random is not a new entry
        Assert.Equal(["portal:" + slug], context.Tags);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/admin/budgets")]
    [InlineData("GET", "/Account/Login")]
    [InlineData("GET", "/health")]
    [InlineData("POST", "/transparency/maple-ridge-oh/2026/search")]
    public async Task Everything_else_bypasses_the_cache(string method, string path)
    {
        OutputCacheContext context = Context(method, path);

        await _policy.CacheRequestAsync(context, CancellationToken.None);

        Assert.False(context.EnableOutputCaching);
        Assert.False(context.AllowCacheLookup);
        Assert.False(context.AllowCacheStorage);
        Assert.Empty(context.Tags);
    }

    [Fact]
    public async Task The_portal_index_is_cached_under_its_own_tag_so_publishing_can_refresh_it()
    {
        OutputCacheContext context = Context("GET", "/transparency");

        await _policy.CacheRequestAsync(context, CancellationToken.None);

        Assert.True(context.EnableOutputCaching);
        Assert.Equal([PortalOutputCachePolicy.IndexTag], context.Tags);
    }

    [Theory]
    [InlineData(StatusCodes.Status404NotFound, false)]
    [InlineData(StatusCodes.Status500InternalServerError, false)]
    [InlineData(StatusCodes.Status200OK, true)]
    public async Task Only_successful_responses_are_stored(int statusCode, bool stored)
    {
        OutputCacheContext context = Context("GET", "/transparency/nowhere-oh");
        await _policy.CacheRequestAsync(context, CancellationToken.None);
        context.HttpContext.Response.StatusCode = statusCode;

        await _policy.ServeResponseAsync(context, CancellationToken.None);

        Assert.Equal(stored, context.AllowCacheStorage);
    }

    [Fact]
    public async Task A_response_that_sets_a_cookie_is_never_stored()
    {
        OutputCacheContext context = Context("GET", "/transparency/maple-ridge-oh");
        await _policy.CacheRequestAsync(context, CancellationToken.None);
        context.HttpContext.Response.Headers.SetCookie = ".AspNetCore.Identity.Application=abc; path=/";

        await _policy.ServeResponseAsync(context, CancellationToken.None);

        Assert.False(context.AllowCacheStorage);
    }

    [Theory]
    [InlineData("/transparency/maple-ridge-oh/2026", "maple-ridge-oh")]
    [InlineData("/TRANSPARENCY/Maple-Ridge-OH", "maple-ridge-oh")]
    [InlineData("/transparency/", null)]
    [InlineData("/transparency", null)]
    [InlineData("/admin/maple-ridge-oh", null)]
    [InlineData("", null)]
    public void Slug_is_the_second_path_segment_lowercased(string path, string? expected) =>
        Assert.Equal(expected, PortalOutputCachePolicy.SlugFromPath(new PathString(path)));

    private static OutputCacheContext Context(string method, string path, string query = "")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        return new OutputCacheContext { HttpContext = http };
    }
}
