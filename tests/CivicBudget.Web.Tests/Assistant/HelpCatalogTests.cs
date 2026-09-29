using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CivicBudget.Application.Assistant;
using CivicBudget.Application.Security;
using CivicBudget.Web.Components.Assistant;
using CivicBudget.Web.Help;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Assistant;

/// <summary>
/// The assistant's knowledge of the app: every admin page describes itself, a plain request finds
/// the right page, the assistant offers and opens only pages the user may open, and its answers
/// can link only inside CivicBudget.
/// </summary>
public class HelpCatalogTests
{
    public static TheoryData<string> AdminPages()
    {
        var data = new TheoryData<string>();
        foreach (Type page in typeof(CivicBudget.Web.Components.App).Assembly.GetTypes()
                     .Where(t => t.Namespace?.StartsWith("CivicBudget.Web.Components.Admin", StringComparison.Ordinal) == true && t.GetCustomAttributes<RouteAttribute>().Any()))
        {
            data.Add(page.FullName!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public void Every_admin_page_says_what_it_is_for(string pageName)
    {
        Type page = typeof(CivicBudget.Web.Components.App).Assembly.GetType(pageName)!;

        HelpTopicAttribute? help = page.GetCustomAttribute<HelpTopicAttribute>();

        Assert.NotNull(help);
        Assert.False(string.IsNullOrWhiteSpace(help.Purpose));
    }

    [Theory]
    [InlineData("print the budget book", "Budget book")]
    [InlineData("I need to change my password", "Change password")]
    [InlineData("update my user settings", "Your profile")]
    [InlineData("run the amended certificate", "Certificate of estimated resources")]
    [InlineData("five year forecast", "Multi-year plan")]
    [InlineData("bring in actuals from the ERP", "Actuals sync")]
    public void A_plain_request_finds_the_page(string request, string expected)
    {
        Assert.Equal(expected, PageCatalog.Search(PageCatalog.Pages, request)[0].Title);
    }

    [Fact]
    public void A_page_matches_its_address_with_the_ids_filled_in()
    {
        PageEntry book = PageCatalog.Pages.Single(p => p.Route == "/admin/budgets/{VersionId:guid}/book");

        Assert.Equal(["VersionId"], book.Parameters);
        Assert.True(book.Matches($"/admin/budgets/{Guid.CreateVersion7()}/book"));
        Assert.True(book.Matches($"/admin/budgets/{Guid.CreateVersion7()}/book?x=1"));
        Assert.False(book.Matches($"/admin/budgets/{Guid.CreateVersion7()}/plan"));
    }

    [Fact]
    public async Task A_viewer_is_not_offered_or_taken_to_an_administrators_page()
    {
        (NavigationTools viewer, AssistantTurn viewerTurn) = Tools(Roles.Viewer);
        (NavigationTools admin, AssistantTurn adminTurn) = Tools(Roles.Admin);

        string found = await Call(viewer, viewerTurn, "find_pages", ("topic", "users and roles"));
        Assert.DoesNotContain("/admin/users", found, StringComparison.Ordinal);
        Assert.Contains("not a page this user can open", await Call(viewer, viewerTurn, "open_page", ("path", "/admin/users")), StringComparison.Ordinal);
        Assert.Null(viewerTurn.NavigateTo);

        Assert.Contains("/admin/users", await Call(admin, adminTurn, "find_pages", ("topic", "users and roles")), StringComparison.Ordinal);
        await Call(admin, adminTurn, "open_page", ("path", "admin/users"));
        Assert.Equal("/admin/users", adminTurn.NavigateTo);
    }

    [Fact]
    public async Task A_page_is_opened_only_with_its_ids_filled_in()
    {
        (NavigationTools tools, AssistantTurn turn) = Tools(Roles.FinanceDirector);

        Assert.Contains("Fill in the placeholders", await Call(tools, turn, "open_page", ("path", "/admin/budgets/{VersionId}/book")), StringComparison.Ordinal);
        Assert.Null(turn.NavigateTo);
    }

    [Theory]
    [InlineData("[the report](/admin/reports/1/fund-summary)", "href=\"/admin/reports/1/fund-summary\"")]
    [InlineData("See [this](https://evil.example/login) now", "See this now")]
    [InlineData("![x](https://evil.example/x.png)", "x")]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;")]
    [InlineData("<https://evil.example>", "https://evil.example")]
    public void An_answer_links_only_inside_CivicBudget(string markdown, string expected)
    {
        string html = AssistantMarkdown.ToHtml(markdown);

        Assert.Contains(expected, html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"https://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
    }

    private static (NavigationTools Tools, AssistantTurn Turn) Tools(string role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddCivicBudgetPolicies();
        IAuthorizationService authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return (new NavigationTools(new FixedAuthentication(new ClaimsPrincipal(identity)), authorization), new AssistantTurn(null));
    }

    private static async Task<string> Call(NavigationTools tools, AssistantTurn turn, string name, params (string Name, object Value)[] arguments)
    {
        AIFunction function = tools.Tools(turn).Single(f => f.Name == name);
        object? result = await function.InvokeAsync(new AIFunctionArguments(arguments.ToDictionary(a => a.Name, a => (object?)a.Value)));
        return JsonSerializer.Serialize(result);
    }

    private sealed class FixedAuthentication(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
