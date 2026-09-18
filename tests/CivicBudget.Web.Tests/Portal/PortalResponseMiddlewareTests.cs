using CivicBudget.Web.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// The middleware rewrites headers in an <c>OnStarting</c> callback. <see cref="DefaultHttpContext"/>'s
/// stock response feature ignores those callbacks, so the tests swap in one that records and runs them,
/// which is how a real server behaves when the first byte is written.
/// </summary>
public class PortalResponseMiddlewareTests
{
    [Fact]
    public async Task Portal_pages_become_public_and_lose_the_antiforgery_cookie()
    {
        (HttpContext http, StartingResponseFeature response) = Context("GET", "/transparency/maple-ridge-oh/2026");
        var middleware = new PortalResponseMiddleware(ctx =>
        {
            // What Blazor's static SSR endpoint writes for every page.
            ctx.Response.Headers.CacheControl = "no-cache, no-store";
            ctx.Response.Headers.Pragma = "no-cache";
            ctx.Response.Headers.SetCookie = ".AspNetCore.Antiforgery.abc=token; path=/; httponly";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http);
        await response.FireOnStartingAsync();

        Assert.Equal("public, max-age=600", http.Response.Headers.CacheControl);
        Assert.False(http.Response.Headers.ContainsKey("Pragma"));
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task A_real_cookie_is_left_alone_even_on_a_portal_page()
    {
        (HttpContext http, StartingResponseFeature response) = Context("GET", "/transparency/maple-ridge-oh");
        var middleware = new PortalResponseMiddleware(ctx =>
        {
            ctx.Response.Headers.SetCookie = new StringValues([".AspNetCore.Antiforgery.abc=token", ".AspNetCore.Identity.Application=session"]);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http);
        await response.FireOnStartingAsync();

        Assert.Equal(2, http.Response.Headers.SetCookie.Count); // the policy then refuses to cache it
    }

    [Fact]
    public async Task Not_found_pages_keep_their_no_store_headers()
    {
        (HttpContext http, StartingResponseFeature response) = Context("GET", "/transparency/nowhere-oh");
        var middleware = new PortalResponseMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.Headers.CacheControl = "no-cache, no-store";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http);
        await response.FireOnStartingAsync();

        Assert.Equal("no-cache, no-store", http.Response.Headers.CacheControl);
    }

    [Theory]
    [InlineData("GET", "/admin/budgets")]
    [InlineData("GET", "/transparency")]
    [InlineData("POST", "/transparency/maple-ridge-oh/2026/search")]
    public async Task Other_requests_are_not_touched(string method, string path)
    {
        (HttpContext http, StartingResponseFeature response) = Context(method, path);
        var middleware = new PortalResponseMiddleware(ctx =>
        {
            ctx.Response.Headers.CacheControl = "no-cache, no-store";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http);

        Assert.Empty(response.Callbacks);
        Assert.Equal("no-cache, no-store", http.Response.Headers.CacheControl);
    }

    private static (HttpContext, StartingResponseFeature) Context(string method, string path)
    {
        var http = new DefaultHttpContext();
        var feature = new StartingResponseFeature();
        http.Features.Set<IHttpResponseFeature>(feature);
        http.Request.Method = method;
        http.Request.Path = path;
        return (http, feature);
    }

    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        public List<(Func<object, Task> Callback, object State)> Callbacks { get; } = [];

        public override void OnStarting(Func<object, Task> callback, object state) => Callbacks.Add((callback, state));

        public async Task FireOnStartingAsync()
        {
            foreach ((Func<object, Task> callback, object state) in Callbacks)
            {
                await callback(state);
            }
        }
    }
}
