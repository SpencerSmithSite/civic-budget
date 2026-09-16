using CivicBudget.Web.Components.Pages;

namespace CivicBudget.Web.Tests;

public class HomePageTests : BunitContext
{
    [Fact]
    public void Home_page_renders_the_application_name()
    {
        IRenderedComponent<Home> page = Render<Home>();

        Assert.Equal("CivicBudget", page.Find("h1").TextContent);
    }
}
