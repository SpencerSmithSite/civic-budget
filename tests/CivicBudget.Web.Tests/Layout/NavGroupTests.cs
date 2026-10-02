using CivicBudget.Web.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace CivicBudget.Web.Tests.Layout;

/// <summary>
/// A sidebar group's heading is a button that says whether its links are showing and which links it
/// controls; folded, the links stay in the page (the icon rail still shows them) but are hidden.
/// </summary>
public class NavGroupTests : BunitContext
{
    private IRenderedComponent<NavGroup> RenderGroup(bool open, Action<string>? onToggle = null) =>
        Render<NavGroup>(p => p
            .Add(x => x.Key, SidebarGroups.Setup)
            .Add(x => x.Title, "Setup")
            .Add(x => x.Open, open)
            .Add(x => x.OnToggle, EventCallback.Factory.Create<string>(this, onToggle ?? (_ => { })))
            .AddChildContent("<a class=\"nav-link\" href=\"admin/funds\">Funds</a>"));

    [Fact]
    public void An_open_group_says_so_and_names_the_links_it_controls()
    {
        IRenderedComponent<NavGroup> group = RenderGroup(open: true);

        AngleSharp.Dom.IElement button = group.Find("button.cb-navgroup");
        Assert.Equal("true", button.GetAttribute("aria-expanded"));
        Assert.Equal("nav-setup", button.GetAttribute("aria-controls"));
        Assert.Contains("Setup", button.TextContent, StringComparison.Ordinal);
        AngleSharp.Dom.IElement nav = group.Find("nav#nav-setup");
        Assert.Equal("Setup", nav.GetAttribute("aria-label"));
        Assert.DoesNotContain("cb-nav-folded", nav.ClassList);
    }

    [Fact]
    public void A_folded_group_keeps_its_links_in_the_page_but_hidden()
    {
        IRenderedComponent<NavGroup> group = RenderGroup(open: false);

        Assert.Equal("false", group.Find("button.cb-navgroup").GetAttribute("aria-expanded"));
        Assert.Contains("cb-nav-folded", group.Find("nav").ClassList);
        Assert.NotNull(group.Find("nav a[href='admin/funds']"));
    }

    [Fact]
    public void Pressing_the_heading_asks_to_toggle_its_own_group()
    {
        string? toggled = null;
        IRenderedComponent<NavGroup> group = RenderGroup(open: true, key => toggled = key);

        group.Find("button.cb-navgroup").Click();

        Assert.Equal(SidebarGroups.Setup, toggled);
    }
}
