using AngleSharp.Dom;
using CivicBudget.Application.Portal;
using CivicBudget.Web.Components.Portal.Common;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// The chart is HTML, so its accessibility promises are testable: every bar has a visible label and
/// value, the same numbers appear in a table, and the "$ | %" toggle is a pair of radio buttons
/// that switches the values in place, without a page load or JavaScript.
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
            .Add(x => x.LinkFor, item => $"/funds/{item.Key}"));

        IReadOnlyList<IElement> rows = cut.FindAll(".pt-bar-row");
        Assert.Equal(2, rows.Count);
        Assert.Equal("1000 General Fund", rows[0].QuerySelector(".pt-bar-label a")!.TextContent);
        Assert.Equal("/funds/1000", rows[0].QuerySelector(".pt-bar-label a")!.GetAttribute("href"));
        Assert.Equal("$300.00", rows[0].QuerySelector(".pt-bar-value .pt-show-amt")!.TextContent);
        Assert.Equal("75.0%", rows[0].QuerySelector(".pt-bar-value .pt-show-pct")!.TextContent);
        Assert.Contains("width:100%", rows[0].QuerySelector(".pt-bar-fill")!.GetAttribute("style"));
        Assert.Contains("width:33.3%", rows[1].QuerySelector(".pt-bar-fill")!.GetAttribute("style")); // scaled to the largest bar

        IReadOnlyList<IElement> cells = cut.FindAll("tbody tr:first-child td");
        Assert.Equal(["$300.00", "75.0%", "+20.0%"], cells.Select(c => c.TextContent));
        Assert.Contains("$400.00", cut.Find("tfoot").TextContent);
        Assert.Equal("Fund", cut.Find("thead th").TextContent);
        // Linked bars are a list whose links already read each label and value, so the list is named
        // by its title only, not by the values again.
        Assert.Equal("list", cut.Find(".pt-bars").GetAttribute("role"));
        Assert.Equal("Where does the money go?", cut.Find(".pt-bars").GetAttribute("aria-label"));
    }

    [Fact]
    public void An_unlinked_chart_is_an_image_whose_label_says_when_it_names_only_the_largest_six()
    {
        BreakdownItemDto[] eight = [.. Enumerable.Range(1, 8).Select(i => new BreakdownItemDto($"{i}", $"Department {i}", null, 100m * (9 - i), 0m))];
        IRenderedComponent<Breakdown> cut = Render<Breakdown>(p => p
            .Add(x => x.Title, "By department")
            .Add(x => x.Data, new BreakdownDto("By department", eight.Sum(i => i.Amount), eight))
            .Add(x => x.ItemHeader, "Department")
            );

        string label = cut.Find(".pt-bars").GetAttribute("aria-label")!;
        Assert.Equal("img", cut.Find(".pt-bars").GetAttribute("role"));
        Assert.StartsWith("By department, 8 items, the largest six: Department 1 $800.00", label, StringComparison.Ordinal);
        Assert.DoesNotContain("Department 7", label, StringComparison.Ordinal);
        Assert.EndsWith("The table lists every amount.", label, StringComparison.Ordinal);
    }

    [Fact]
    public void The_toggle_is_a_named_radio_pair_that_starts_on_dollars()
    {
        IRenderedComponent<Breakdown> cut = Render<Breakdown>(p => p
            .Add(x => x.Title, "Where does the money go?")
            .Add(x => x.Data, Data));

        Assert.Equal("Show amounts as", cut.Find(".pt-toggle legend").TextContent);
        IReadOnlyList<IElement> radios = cut.FindAll(".pt-toggle input[type=radio]");
        Assert.Equal(2, radios.Count);
        Assert.All(radios, r => Assert.Equal("bd-where-does-the-money-go-show", r.GetAttribute("name")));
        Assert.True(radios[0].HasAttribute("checked"));
        Assert.False(radios[1].HasAttribute("checked"));
        Assert.Equal(["Dollars", "Percent"], cut.FindAll(".pt-toggle label").Select(l => l.QuerySelector(".visually-hidden")!.TextContent));
        Assert.Equal(radios.Select(r => r.Id), cut.FindAll(".pt-toggle label").Select(l => l.GetAttribute("for")));
        Assert.Empty(cut.FindAll(".pt-toggle a")); // no links, so no page load
    }

    [Fact]
    public void A_page_asked_for_percentages_starts_on_the_percent_radio()
    {
        IRenderedComponent<Breakdown> cut = Render<Breakdown>(p => p
            .Add(x => x.Title, "Where does the money go?")
            .Add(x => x.Data, Data)
            .Add(x => x.ShowPercent, true));

        Assert.True(cut.Find(".pt-toggle-pct").HasAttribute("checked"));
        Assert.Equal(["75.0%", "25.0%"], cut.FindAll(".pt-bar-value .pt-show-pct").Select(v => v.TextContent));
    }

    [Fact]
    public void Two_charts_on_a_page_have_their_own_toggles()
    {
        IRenderedComponent<Breakdown> first = Render<Breakdown>(p => p.Add(x => x.Title, "By department").Add(x => x.Data, Data));
        IRenderedComponent<Breakdown> second = Render<Breakdown>(p => p.Add(x => x.Title, "By category").Add(x => x.Data, Data));

        Assert.NotEqual(first.Find(".pt-toggle-radio").GetAttribute("name"), second.Find(".pt-toggle-radio").GetAttribute("name"));
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
