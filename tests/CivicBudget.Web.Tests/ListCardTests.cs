using CivicBudget.Web.Components.Common;

namespace CivicBudget.Web.Tests;

/// <summary>Phase 15: the phone card for one list row carries code, title, summary, aside, and menu in fixed places.</summary>
public class ListCardTests : BunitContext
{
    [Fact]
    public void Renders_code_title_summary_and_aside()
    {
        IRenderedComponent<ListCard> card = Render<ListCard>(p => p
            .Add(x => x.Code, "1000").Add(x => x.Title, "General Fund").Add(x => x.Href, "admin/funds/1")
            .Add(x => x.Summary, b => b.AddMarkupContent(0, "General · Beginning balance $655,000"))
            .Add(x => x.Aside, b => b.AddMarkupContent(0, "<span class=\"cb-pill\">Active</span>")));

        Assert.Equal("1000", card.Find(".cb-list-card-title .cb-code").TextContent);
        Assert.Equal("General Fund", card.Find(".cb-list-card-title a").TextContent);
        Assert.Contains("Beginning balance", card.Find(".cb-list-card-meta").TextContent);
        Assert.Contains("Active", card.Find(".cb-list-card-aside").TextContent);
        Assert.Empty(card.FindAll(".cb-list-card-lead"));
    }

    [Fact]
    public void Leading_slot_appears_only_when_given()
    {
        IRenderedComponent<ListCard> card = Render<ListCard>(p => p.Add(x => x.Title, "Dana").Add(x => x.Leading, b => b.AddMarkupContent(0, "<span class=\"cb-avatar\">DW</span>")));
        Assert.Contains("DW", card.Find(".cb-list-card-lead").TextContent);
    }
}
