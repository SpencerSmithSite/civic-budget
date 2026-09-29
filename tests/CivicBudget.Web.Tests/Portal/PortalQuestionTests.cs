using CivicBudget.Application.Assistant;
using CivicBudget.Application.Common;
using CivicBudget.Web.Components.Assistant;
using CivicBudget.Web.Components.Portal.Common;
using CivicBudget.Web.Security;
using Microsoft.AspNetCore.Http;

namespace CivicBudget.Web.Tests.Portal;

/// <summary>
/// The portal question box's Web pieces: a question posted with no model connected is turned away
/// before anything else runs, and an answer keeps only links into this government's own portal.
/// </summary>
public class PortalQuestionTests : BunitContext
{
    private sealed class FakeQuestions(bool configured) : IPortalQuestionService
    {
        public bool ModelConfigured => configured;

        public Task<bool> IsAvailableAsync(string slug, CancellationToken ct = default) => Task.FromResult(configured);

        public Task<Result<PortalAnswer>> AskAsync(string slug, int fiscalYear, string question, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new PortalAnswer("An answer.", [])));
    }

    private static async Task<(int Status, bool Continued)> PostAsync(string path, bool configured)
    {
        bool continued = false;
        var gate = new PortalQuestionGate(_ => { continued = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { Request = { Method = "POST", Path = path } };
        context.Response.Body = new MemoryStream();

        await gate.InvokeAsync(context, new FakeQuestions(configured));

        return (context.Response.StatusCode, continued);
    }

    [Fact]
    public async Task Without_a_model_a_posted_question_is_not_found_and_goes_no_further()
    {
        Assert.Equal((404, false), await PostAsync("/transparency/maple-ridge-oh/2026/ask", configured: false));
        Assert.Equal((200, true), await PostAsync("/transparency/maple-ridge-oh/2026/ask", configured: true));
        Assert.Equal((200, true), await PostAsync("/Account/Login", configured: false)); // nothing else is touched
    }

    [Fact]
    public void An_answer_links_only_into_this_governments_portal()
    {
        var answer = new PortalAnswer(
            "See [police](/transparency/maple-ridge-oh/2026/funds/1000/departments/110), [Pine Hollow](/transparency/pine-hollow-twp-oh/2026), and [the admin](/admin/budgets).",
            ["Searched FY2026 for \"police\""]);

        IRenderedComponent<PortalAnswerView> view = Render<PortalAnswerView>(p => p
            .Add(x => x.Question, "How much for police?")
            .Add(x => x.Answer, answer)
            .Add(x => x.LinkPrefix, "/transparency/maple-ridge-oh/"));

        Assert.Equal(["/transparency/maple-ridge-oh/2026/funds/1000/departments/110"], view.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.Contains("Pine Hollow", view.Markup, StringComparison.Ordinal); // the words stay, the link goes
        Assert.Contains("How much for police?", view.Find(".pt-asked").TextContent, StringComparison.Ordinal);
        Assert.Contains("Looked at: Searched FY2026", view.Find(".pt-answer-steps").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_prefix_the_staff_assistant_keeps_any_local_link()
    {
        Assert.Contains("href=\"/admin/budgets\"", AssistantMarkdown.ToHtml("[budgets](/admin/budgets)"), StringComparison.Ordinal);
        Assert.DoesNotContain("href", AssistantMarkdown.ToHtml("[budgets](/admin/budgets)", "/transparency/x/"), StringComparison.Ordinal);
    }
}
