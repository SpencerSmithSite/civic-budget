using System.Net;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicBudget.Web.Tests.Security;

/// <summary>
/// A form posted with a token the current keys cannot read (a page opened before the nightly reset)
/// gets a page that says so and links back, not an empty 400. Every other request reaches its
/// endpoint, including a form that opts out of antiforgery.
/// </summary>
public class ExpiredFormMiddlewareTests
{
    [Fact]
    public async Task Shows_the_expired_page_for_a_rejected_token()
    {
        DefaultHttpContext http = Post("/Account/Login", "?ReturnUrl=%2Fadmin%2Fbudgets", tokenValid: false, PageEndpoint());

        bool reachedNext = false;

        await Middleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.False(reachedNext);
        Assert.Equal(400, http.Response.StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl);
        Assert.StartsWith("text/html", http.Response.ContentType);
        string body = await Body(http);
        Assert.Contains("This page expired", body);
        Assert.Contains("href=\"/Account/Login?ReturnUrl=%2Fadmin%2Fbudgets\"", body);
        Assert.DoesNotContain("<script", body);
    }

    [Theory]
    [InlineData("?ReturnUrl=%2F%2Fevil.example", "/Account/Login")]
    [InlineData("?ReturnUrl=https%3A%2F%2Fevil.example&expired=true", "/Account/Login?expired=true")]
    [InlineData("?returnUrl=admin", "/Account/Login?returnUrl=admin")]
    [InlineData("", "/Account/Login")]
    public void Keeps_a_local_return_url_and_drops_one_that_leaves_the_site(string query, string expected)
    {
        DefaultHttpContext http = Post("/Account/Login", query, tokenValid: false, PageEndpoint());

        Assert.Equal(expected, ExpiredFormMiddleware.ReturnLink(http));
    }

    [Fact]
    public void Sends_a_post_only_endpoint_home()
    {
        var logout = new Endpoint(null, new EndpointMetadataCollection(new HttpMethodMetadata([HttpMethods.Post])), "logout");
        DefaultHttpContext http = Post("/Account/Logout", "", tokenValid: false, logout);

        Assert.Equal("/", ExpiredFormMiddleware.ReturnLink(http));
    }

    [Fact]
    public void Sends_a_path_that_is_not_local_home()
    {
        DefaultHttpContext http = Post("//evil.example", "", tokenValid: false, PageEndpoint());

        Assert.Equal("/", ExpiredFormMiddleware.ReturnLink(http));
    }

    [Theory]
    [InlineData(true)]  // the token checked out
    [InlineData(null)]  // no check ran: a GET, or a form that opts out (the portal's question box)
    public async Task Lets_every_other_request_reach_the_endpoint(bool? tokenValid)
    {
        DefaultHttpContext http = Post("/transparency/maple-ridge-oh/ask", "", tokenValid, PageEndpoint());
        bool reachedNext = false;

        await Middleware(_ => { reachedNext = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.True(reachedNext);
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Equal("", await Body(http));
    }

    /// <summary>
    /// The real antiforgery middleware and a real endpoint: proves the feature is set where this
    /// middleware looks for it, and that the page reaches the browser instead of the empty 400.
    /// </summary>
    [Fact]
    public async Task A_post_with_an_unreadable_token_gets_the_page_through_the_real_pipeline()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAntiforgery();
        await using WebApplication app = builder.Build();
        app.UseAntiforgery();
        app.UseMiddleware<ExpiredFormMiddleware>();
        app.MapMethods("/form", [HttpMethods.Get, HttpMethods.Post], ([FromForm] string name) => $"saved {name}");
        app.MapPost("/open", ([FromForm] string name) => $"saved {name}").DisableAntiforgery();
        await app.StartAsync(CancellationToken.None);
        string address = app.Urls.First(); // with the port Kestrel picked
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var stale = new FormUrlEncodedContent([new("name", "x"), new("__RequestVerificationToken", "CfDJ8-sealed-with-a-key-that-is-gone")]);
        using HttpResponseMessage rejected = await client.PostAsync("/form?step=2", stale, CancellationToken.None);
        using HttpResponseMessage open = await client.PostAsync("/open", new FormUrlEncodedContent([new("name", "x")]), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        string body = await rejected.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("This page expired", body);
        Assert.Contains("href=\"/form?step=2\"", body);
        Assert.Equal("saved x", await open.Content.ReadAsStringAsync(CancellationToken.None));
        await app.StopAsync(CancellationToken.None);
    }

    private static ExpiredFormMiddleware Middleware(RequestDelegate next) =>
        new(next, NullLogger<ExpiredFormMiddleware>.Instance);

    // What a Blazor page's endpoint carries: it answers GET and POST.
    private static Endpoint PageEndpoint() =>
        new(null, new EndpointMetadataCollection(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head, HttpMethods.Post])), "page");

    private static DefaultHttpContext Post(string path, string query, bool? tokenValid, Endpoint endpoint)
    {
        var http = new DefaultHttpContext
        {
            Request = { Method = HttpMethods.Post, Path = path, QueryString = new QueryString(query) },
            Response = { Body = new MemoryStream() },
        };
        http.SetEndpoint(endpoint);
        if (tokenValid is bool valid)
        {
            http.Features.Set<IAntiforgeryValidationFeature>(new ValidationResult(valid));
        }

        return http;
    }

    private static async Task<string> Body(HttpContext http)
    {
        http.Response.Body.Position = 0;
        return await new StreamReader(http.Response.Body).ReadToEndAsync();
    }

    private sealed record ValidationResult(bool IsValid) : IAntiforgeryValidationFeature
    {
        public Exception? Error => IsValid ? null : new AntiforgeryValidationException("The antiforgery token could not be decrypted.");
    }
}
