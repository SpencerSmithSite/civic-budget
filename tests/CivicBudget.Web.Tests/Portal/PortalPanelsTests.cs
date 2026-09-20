using CivicBudget.Web.Components.Portal.Common;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>Two sections on a sliding track, switched by radio tabs and nothing else: no script, both panels in the page.</summary>
public class PortalPanelsTests : BunitContext
{
    [Fact]
    public void Renders_two_radio_tabs_with_the_first_checked_and_both_panels_present()
    {
        IRenderedComponent<PortalPanels> panels = Render<PortalPanels>(p => p
            .Add(x => x.Name, "money")
            .Add(x => x.FirstTitle, "Where does the money go?")
            .Add(x => x.SecondTitle, "Where does it come from?")
            .Add(x => x.First, b => b.AddMarkupContent(0, "<p>spending</p>"))
            .Add(x => x.Second, b => b.AddMarkupContent(0, "<p>revenue</p>")));

        Assert.True(panels.Find("#money-1").HasAttribute("checked"));
        Assert.False(panels.Find("#money-2").HasAttribute("checked"));
        Assert.Equal("Where does the money go?", panels.Find("label[for=money-1]").TextContent);
        Assert.Equal(2, panels.FindAll(".pt-panel").Count);
        Assert.Contains("revenue", panels.FindAll(".pt-panel")[1].TextContent);
        Assert.Empty(panels.FindAll("script"));
    }

    [Fact]
    public void Can_open_on_the_second_panel_for_links_that_land_there()
    {
        IRenderedComponent<PortalPanels> panels = Render<PortalPanels>(p => p
            .Add(x => x.Name, "money").Add(x => x.FirstTitle, "A").Add(x => x.SecondTitle, "B").Add(x => x.SecondFirst, true)
            .Add(x => x.First, b => b.AddMarkupContent(0, "a")).Add(x => x.Second, b => b.AddMarkupContent(0, "b")));

        Assert.True(panels.Find("#money-2").HasAttribute("checked"));
    }
}
