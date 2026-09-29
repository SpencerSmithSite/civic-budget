using System.Text.Encodings.Web;
using CivicBudget.Web.Components.Account;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace CivicBudget.Web.Security;

/// <summary>
/// Answers a form post whose antiforgery token no longer checks out with a page that says the form
/// expired and links back to where it came from, instead of the endpoint's bare 400 (an empty body in
/// production, which the browser shows as a blank white page). The token is sealed with the Data
/// Protection keys, which live in the database, so the nightly demo reset (and a deploy that rotates
/// keys) leaves every page opened before it holding a token the app cannot read. The status-code pages
/// cannot help, because re-executing a POST fails the same check.
/// <para>
/// It sits right after <c>UseAntiforgery</c>, which has already checked the token and recorded the
/// outcome in <see cref="IAntiforgeryValidationFeature"/>, and answers before the endpoint runs: an
/// endpoint that asked for the check would only refuse the request. A form that opts out (the
/// portal's question box) is never checked, so it has no such feature and passes straight through.
/// </para>
/// </summary>
public sealed class ExpiredFormMiddleware(RequestDelegate next, ILogger<ExpiredFormMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false })
        {
            await next(context);
            return;
        }

        // The framework logs why the token failed; this line says what the visitor saw instead.
        logger.LogInformation("Form post to {Path} refused by the antiforgery check; showed the page-expired page.", context.Request.Path);
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status400BadRequest;
        response.Headers[HeaderNames.CacheControl] = "no-store";
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(ExpiredFormPage.Render(ReturnLink(context)), context.RequestAborted);
    }

    /// <summary>
    /// The address to open again: the one the form was posted to, which for a Blazor form is the page
    /// it sits on, query and all (so sign-in keeps its ReturnUrl). An endpoint that only takes posts,
    /// such as sign-out, has no page to reopen, so that goes home. A ReturnUrl that leaves the site is
    /// dropped, and anything that still is not a local address (a path like "//host") goes home too.
    /// </summary>
    public static string ReturnLink(HttpContext context)
    {
        if (!AnswersGet(context.GetEndpoint()))
        {
            return "/";
        }

        string path = context.Request.PathBase.Add(context.Request.Path).ToUriComponent();
        IEnumerable<KeyValuePair<string, StringValues>> query = QueryHelpers.ParseQuery(context.Request.QueryString.Value)
            .Where(pair => !pair.Key.Equals("ReturnUrl", StringComparison.OrdinalIgnoreCase) || pair.Value.All(LocalUrl.IsLocal));
        string link = QueryHelpers.AddQueryString(path, query);
        return LocalUrl.IsLocal(link) ? link : "/";
    }

    // Razor component pages take GET and POST; a minimal API MapPost takes POST alone. An endpoint
    // with no method metadata takes any method.
    private static bool AnswersGet(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<IHttpMethodMetadata>() is not { HttpMethods.Count: > 0 } methods
        || methods.HttpMethods.Contains(HttpMethods.Get, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The expired-form page on the shared <see cref="PlainPage"/> shell. No script: a link is all it needs.</summary>
public static class ExpiredFormPage
{
    public static string Render(string link) => PlainPage.Render(
        "Page expired · CivicBudget",
        Card.Replace("{{link}}", HtmlEncoder.Default.Encode(link), StringComparison.Ordinal),
        Styles);

    private const string Styles = """
        .button { display: inline-block; margin-top: 8px; padding: 8px 16px; border-radius: 6px; background: var(--teal); color: #fff;
                  font-weight: 600; font-size: .9375rem; text-decoration: none; }
        .button:hover { background: var(--navy); }
        .button:focus-visible { outline: 3px solid var(--teal-2); outline-offset: 2px; }
        """;

    private const string Card = """
        <div class="card">
            <p class="eyebrow">Page expired</p>
            <h1>This page expired</h1>
            <p>The site was updated or reset after this page was opened, so the form was not accepted and nothing changed.</p>
            <p>Open the page again and try once more.</p>
            <a class="button" href="{{link}}">Open the page again</a>
        </div>
        """;
}
