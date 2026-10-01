using CivicBudget.Application.Portal;
using CivicBudget.Web.Components.Portal;
using CivicBudget.Web.Components.Portal.Common;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// Civic Buddy's floating panel on the portal: it opens without JavaScript, says it is an AI, and
/// every way to ask (the box and each suggested question) is a post the ask page takes as its own.
/// </summary>
public class CivicBuddyTests : BunitContext
{
    private const string Root = "/transparency/maple-ridge-oh/2026";

    private static readonly PortalBudgetDto Budget = new(Guid.NewGuid(), "maple-ridge-oh", "Village of Maple Ridge", null, 2026, "Amendment 1", null, "2026-11",
        null, DateTimeOffset.UtcNow, [new PortalYearDto(2026, "Amendment 1", DateTimeOffset.UtcNow)], 1m, 1m, 0m, 0m, 0m, 0m, 0m);

    private IRenderedComponent<PortalBuddy> RenderBuddy() =>
        Render<PortalBuddy>(p => p.Add(x => x.Budget, Budget).Add(x => x.Root, Root));

    [Fact]
    public void The_launcher_is_a_details_element_named_for_Civic_Buddy_and_closed_at_first()
    {
        IRenderedComponent<PortalBuddy> buddy = RenderBuddy();

        AngleSharp.Dom.IElement details = buddy.Find("details.pt-buddy");
        Assert.False(details.HasAttribute("open"));
        Assert.Contains("Ask Civic Buddy", buddy.Find("summary").TextContent, StringComparison.Ordinal);
        Assert.Equal("AI", buddy.Find(".pt-buddy-title .pt-ai-badge").TextContent);
        Assert.All(buddy.FindAll("svg.cb-buddy-mark"), mark => Assert.Equal("true", mark.GetAttribute("aria-hidden")));
    }

    [Fact]
    public void Every_way_to_ask_posts_to_the_ask_page_as_its_form()
    {
        IRenderedComponent<PortalBuddy> buddy = RenderBuddy();

        IReadOnlyList<AngleSharp.Dom.IElement> forms = buddy.FindAll("form");
        Assert.Equal(1 + BuddySuggestions.Questions.Count, forms.Count);
        Assert.All(forms, form =>
        {
            Assert.Equal("post", form.GetAttribute("method"));
            Assert.Equal($"{Root}/ask#answer", form.GetAttribute("action"));
            Assert.Equal(PortalAsk.FormName, form.QuerySelector("input[name=_handler]")?.GetAttribute("value"));
        });

        Assert.Equal(BuddySuggestions.Questions, buddy.FindAll(".pt-buddy-chips input[name=Question]").Select(i => i.GetAttribute("value")));
        Assert.NotNull(buddy.Find("textarea[name=Question][required]"));
    }

    [Fact]
    public void The_panel_says_answers_come_from_an_AI_and_can_be_wrong()
    {
        string note = RenderBuddy().Find(".pt-buddy-note").TextContent;

        Assert.Contains("is an AI and can be wrong", note, StringComparison.Ordinal);
        Assert.Contains("not stored here", note, StringComparison.Ordinal);
    }
}
