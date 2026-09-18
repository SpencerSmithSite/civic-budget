using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests;

/// <summary>Phase 8 moved explanations out of subtitles and into tips; the tip must stay reachable by keyboard and screen reader.</summary>
public class InfoTipTests : BunitContext
{
    [Fact]
    public void Tip_is_focusable_and_its_text_is_the_accessible_name()
    {
        IRenderedComponent<InfoTip> tip = Render<InfoTip>(p => p.Add(x => x.Text, "Ohio law caps appropriations at estimated resources."));

        AngleSharp.Dom.IElement root = tip.Find(".cb-tip");
        Assert.Equal("0", root.GetAttribute("tabindex"));
        Assert.Equal("Ohio law caps appropriations at estimated resources.", root.GetAttribute("aria-label"));
        Assert.Equal("true", tip.Find(".cb-tip-text").GetAttribute("aria-hidden")); // read once, via the label, not twice
    }

    [Fact]
    public void Page_header_and_kpi_card_render_a_tip_only_when_given_one()
    {
        Services.AddSingleton<AdminPageState>();

        IRenderedComponent<PageHeader> withTip = Render<PageHeader>(p => p.Add(x => x.Title, "Funds").Add(x => x.Tip, "Codes follow the UAN chart."));
        IRenderedComponent<PageHeader> without = Render<PageHeader>(p => p.Add(x => x.Title, "Departments"));
        IRenderedComponent<KpiCard> kpi = Render<KpiCard>(p => p.Add(x => x.Label, "Estimated resources").Add(x => x.Value, "$1").Add(x => x.Tip, "Beginning balance plus revenues."));

        Assert.Single(withTip.FindAll(".cb-tip"));
        Assert.Empty(without.FindAll(".cb-tip"));
        Assert.Single(kpi.FindAll(".cb-eyebrow .cb-tip"));
    }
}
