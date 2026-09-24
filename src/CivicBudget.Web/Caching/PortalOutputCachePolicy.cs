using Microsoft.AspNetCore.OutputCaching;

namespace CivicBudget.Web.Caching;

/// <summary>
/// Output caching for the public portal only. Anonymous GET requests under <c>/transparency</c> are
/// cached for hours and tagged with the government's slug, so publishing or unpublishing evicts
/// exactly that government's pages (see <see cref="OutputCacheSnapshotInvalidator"/>). Everything
/// else (the admin app, sign-in, health) is never cached. Applied as a base policy, so it does not
/// depend on attributes being honored by Razor component endpoints.
/// <para>
/// Registered with <c>excludeDefaultPolicy: true</c>: the built-in default policy refuses any
/// response marked <c>Cache-Control: no-store</c>, which Blazor puts on every server-rendered page.
/// <see cref="PortalResponseMiddleware"/> fixes the headers for portal pages; this policy keeps the
/// two protections that matter: GET only, and never a response that still sets a cookie.
/// </para>
/// </summary>
public sealed class PortalOutputCachePolicy : IOutputCachePolicy
{
    public const string TagPrefix = "portal:";

    /// <summary>The list of governments at /transparency; any publish, unpublish, or address change can alter it.</summary>
    public const string IndexTag = "portal-index"; // not "portal:" + something, so no government slug can collide with it

    public static readonly Microsoft.Extensions.Primitives.StringValues VaryByQueryKeys = new(["q", "show", "view"]);
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        HttpRequest request = context.HttpContext.Request;
        bool isPortalGet = HttpMethods.IsGet(request.Method)
            && request.Path.StartsWithSegments("/transparency", StringComparison.OrdinalIgnoreCase);

        context.EnableOutputCaching = isPortalGet;
        context.AllowCacheLookup = isPortalGet;
        context.AllowCacheStorage = isPortalGet;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Lifetime;

        if (isPortalGet)
        {
            // Key by the path plus the query values the pages read ("$ | %", the revenue view, search).
            // Not "*": any other key (?x=1, ?x=2, ...) would be a new cache entry, so anyone could skip
            // the cache and make every request rebuild a page from the database.
            context.CacheVaryByRules.QueryKeys = VaryByQueryKeys;
            string? slug = SlugFromPath(request.Path);
            context.Tags.Add(slug is null ? IndexTag : TagPrefix + slug);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        HttpResponse response = context.HttpContext.Response;

        // Only cache successful pages (a not-found for a slug published later must not stick) that
        // set no cookie (a response that sets a cookie is personal by definition). Headers are
        // read-only at this point; PortalResponseMiddleware already cleaned them up.
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>"/transparency/maple-ridge-oh/2026/funds/1000" gives "maple-ridge-oh".</summary>
    public static string? SlugFromPath(PathString path)
    {
        string[] segments = path.Value?.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return segments.Length >= 2 && segments[0].Equals("transparency", StringComparison.OrdinalIgnoreCase)
            ? segments[1].ToLowerInvariant()
            : null;
    }
}
