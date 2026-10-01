using CivicBudget.Web.Components.Common;
using CivicBudget.Web.Components.Portal.Common;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// The navy band that opens every portal page: one h1, the breadcrumbs above it, a lead only when
/// there is one, and room for the headline cards when asked. Its icons are decoration only.
/// </summary>
public class PortalHeroTests : BunitContext
{
    [Fact]
    public void Opens_the_page_with_its_breadcrumbs_and_one_heading()
    {
        IRenderedComponent<PortalHero> hero = Render<PortalHero>(p => p
            .Add(x => x.Title, "Fund balances")
            .Add(x => x.Eyebrow, "Special revenue fund 2011")
            .Add(x => x.Crumbs, [new Crumb("2026 budget", "/transparency/x/2026"), new Crumb("Funds")])
            .Add(x => x.Lead, b => b.AddContent(0, "Each fund carries its balance forward.")));

        Assert.Equal("Fund balances", hero.Find("h1").TextContent);
        Assert.Single(hero.FindAll("h1"));
        Assert.Equal("Funds", hero.Find(".pt-crumbs [aria-current=page]").TextContent);
        Assert.Equal("Each fund carries its balance forward.", hero.Find(".pt-hero-lead").TextContent);
        Assert.DoesNotContain("pt-hero--overlap", hero.Find(".pt-hero").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_plain_lead_draws_nothing_and_overlap_leaves_room_for_the_cards()
    {
        IRenderedComponent<PortalHero> hero = Render<PortalHero>(p => p
            .Add(x => x.Title, "Street Construction")
            .Add(x => x.LeadText, null)
            .Add(x => x.Overlap, true));

        Assert.Empty(hero.FindAll(".pt-hero-lead"));
        Assert.Empty(hero.FindAll(".pt-crumbs"));
        Assert.Contains("pt-hero--overlap", hero.Find(".pt-hero").ClassName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("in")]
    [InlineData("ask")]
    [InlineData("download")]
    public void Icons_are_decoration_hidden_from_screen_readers(string name)
    {
        IRenderedComponent<PortalIcon> icon = Render<PortalIcon>(p => p.Add(x => x.Name, name));

        Assert.Equal("true", icon.Find("svg").GetAttribute("aria-hidden"));
        Assert.NotEmpty(icon.Find("svg").InnerHtml);
    }
}
