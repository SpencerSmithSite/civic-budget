namespace CivicBudget.Web.Caching;

/// <summary>
/// Makes portal responses cacheable. Blazor's static SSR endpoint marks every page
/// <c>Cache-Control: no-cache, no-store</c> and issues an antiforgery cookie, both of which are
/// right for the admin app and wrong for anonymous, form-free portal pages. This middleware sits
/// before <c>UseOutputCache</c>, so its <c>OnStarting</c> callback fires on both a cache miss (the
/// rendered page) and a cache hit (the stored page): either way the client gets a public max-age
/// and no antiforgery cookie. <see cref="PortalOutputCachePolicy"/> decides what may be stored.
/// </summary>
public sealed class PortalResponseMiddleware(RequestDelegate next)
{
    public static readonly TimeSpan BrowserMaxAge = TimeSpan.FromMinutes(10);

    public Task InvokeAsync(HttpContext context)
    {
        // The whole portal, the /transparency index included: its page is as public as the rest.
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/transparency", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(static state =>
            {
                HttpResponse response = (HttpResponse)state;
                if (response.StatusCode == StatusCodes.Status200OK)
                {
                    response.Headers.CacheControl = $"public, max-age={(int)BrowserMaxAge.TotalSeconds}";
                    response.Headers.Remove("Pragma");
                    if (response.Headers.TryGetValue("Set-Cookie", out Microsoft.Extensions.Primitives.StringValues cookies)
                        && cookies.All(c => c is not null && c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)))
                    {
                        response.Headers.Remove("Set-Cookie");
                    }
                }

                return Task.CompletedTask;
            }, context.Response);
        }

        return next(context);
    }
}
