using CivicBudget.Web.Components.Pages;

namespace CivicBudget.Web.Tests;

public class HomePageTests : BunitContext
{
    [Fact]
    public void Home_page_renders_the_application_name_and_a_login_link_for_anonymous_visitors()
    {
        AddAuthorization().SetNotAuthorized(); // the page contains an AuthorizeView

        IRenderedComponent<Home> page = Render<Home>();

        Assert.Equal("CivicBudget", page.Find("h1").TextContent);
        Assert.Contains("Log in", page.Find("a.btn").TextContent);
    }
}
