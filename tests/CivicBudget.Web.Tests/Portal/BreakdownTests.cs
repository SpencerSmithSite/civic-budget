using AngleSharp.Dom;
using CivicBudget.Application.Portal;
using CivicBudget.Web.Components.Portal.Common;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// The chart is HTML, so its accessibility promises are testable: every bar has a visible label and
/// value, the same numbers appear in a table, and the "$ | %" toggle is two ordinary links.
/// </summary>
public class BreakdownTests : BunitContext
{
    private static readonly BreakdownDto Data = new("Expenditures by fund", 400m,
    [
        new("1000", "1000 General Fund", null, 300m, 250m),
        new("2011", "2011 Street", "Gas tax", 100m, 100m),
    ]);

    [Fact]
    public void Renders_a_labeled_bar_per_item_and_a_table_twin_with_totals()
    {
        IRenderedComponent<Breakdown> cut = Render<Breakdown>(p => p
            .Add(x => x.Title, "Where does the money go?")
            .Add(x => x.Data, Data)
            .Add(x => x.ItemHeader, "Fund")
            .Add(x => x.PageHref, "/transparency/maple-ridge-oh/2026")
            .Add(x => x.LinkFor, item => $"/funds/{item.Key}"));

        IReadOnlyList<IElement> rows = cut.FindAll(".pt-bar-row");
        Assert.Equal(2, rows.Count);
        Assert.Equal("1000 General Fund", rows[0].QuerySelector(".pt-bar-label a")!.TextContent);
        Assert.Equal("/funds/1000", rows[0].QuerySelector(".pt-bar-label a")!.GetAttribute("href"));
        Assert.Equal("$300.00", rows[0].QuerySelector(".pt-bar-value")!.TextContent);
        Assert.Contains("width:100%", rows[0].QuerySelector(".pt-bar-fill")!.GetAttribute("style"));
        Assert.Contains("width:33.3%", rows[1].QuerySelector(".pt-bar-fill")!.GetAttribute("style")); // scaled to the largest bar

        IReadOnlyList<IElement> cells = cut.FindAll("tbody tr:first-child td");
        Assert.Equal(["$300.00", "75.0%", "+20.0%"], cells.Select(c => c.TextContent));
        Assert.Contains("$400.00", cut.Find("tfoot").TextContent);
        Assert.Equal("Fund", cut.Find("thead th").TextContent);
        Assert.Contains("1000 General Fund $300.00", cut.Find(".pt-bars").GetAttribute("aria-label"));
    }

    [Fact]
    public void Percent_mode_swaps_the_bar_values_and_the_toggle_is_a_pair_of_links()
    {
        IRenderedComponent<Breakdown> cut = Render<Breakdown>(p => p
            .Add(x => x.Title, "Where does the money go?")
            .Add(x => x.Data, Data)
            .Add(x => x.ShowPercent, true)
            .Add(x => x.PageHref, "/transparency/maple-ridge-oh/2026"));

        Assert.Equal(["75.0%", "25.0%"], cut.FindAll(".pt-bar-value").Select(v => v.TextContent));

        IReadOnlyList<IElement> toggle = cut.FindAll(".pt-toggle a");
        Assert.StartsWith("/transparency/maple-ridge-oh/2026#", toggle[0].GetAttribute("href"));
        Assert.StartsWith("/transparency/maple-ridge-oh/2026?show=pct#", toggle[1].GetAttribute("href"));
        Assert.Equal("true", toggle[1].GetAttribute("aria-current"));
        Assert.Null(toggle[0].GetAttribute("aria-current"));
    }

    [Fact]
    public void Says_so_when_nothing_is_budgeted()
    {
        IRenderedComponent<Breakdown> cut = Render<Breakdown>(p => p
            .Add(x => x.Title, "Revenues")
            .Add(x => x.Data, new BreakdownDto("Revenues", 0m, [])));

        Assert.Contains("Nothing budgeted here.", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
    }
}
