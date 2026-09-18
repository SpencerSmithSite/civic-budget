using CivicBudget.Web.Components.Pages;

namespace CivicBudget.Web.Tests;

public class HomePageTests : BunitContext
{
    [Fact]
    public void Home_page_states_the_product_and_offers_sign_in_to_anonymous_visitors()
    {
        AddAuthorization().SetNotAuthorized(); // the page contains an AuthorizeView

        IRenderedComponent<Home> page = Render<Home>();

        Assert.Contains("Budgeting and public transparency", page.Find("h1").TextContent);
        Assert.Contains("Sign in", page.Find("a.btn-primary").TextContent);
    }
}
