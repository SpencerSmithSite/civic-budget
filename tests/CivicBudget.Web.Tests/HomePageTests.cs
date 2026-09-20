using CivicBudget.Web.Components.Pages;

namespace CivicBudget.Web.Tests;

public class HomePageTests : BunitContext
{
    [Fact]
    public void Home_page_offers_sign_in_and_the_portal_and_says_nothing_about_the_project()
    {
        AddAuthorization().SetNotAuthorized(); // the page contains an AuthorizeView

        IRenderedComponent<Home> page = Render<Home>();

        Assert.Contains("Budgeting and public transparency", page.Find("h1").TextContent);
        Assert.Contains("Sign in to view and enter data", page.Find(".cb-login-card p").TextContent);
        Assert.Contains("Sign in", page.Find("a.btn-primary").TextContent);
        Assert.Equal("transparency", page.Find("a.btn-outline-primary").GetAttribute("href"));
        // Ready for market, not a showcase: no project, demo, or stack talk on the front door.
        Assert.DoesNotContain("portfolio", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Blazor", page.Markup);
    }

    [Fact]
    public void Signed_in_visitors_continue_into_the_app()
    {
        AddAuthorization().SetAuthorized("Dana Whitfield");

        IRenderedComponent<Home> page = Render<Home>();

        Assert.Equal("admin", page.Find("a.btn-primary").GetAttribute("href"));
    }
}
