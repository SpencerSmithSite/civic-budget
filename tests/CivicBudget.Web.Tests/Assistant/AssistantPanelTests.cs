using Bunit.TestDoubles;
using CivicBudget.Application.Assistant;
using CivicBudget.Application.Common;
using CivicBudget.Web.Components.Assistant;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Assistant;

/// <summary>
/// The assistant panel: hidden without a model, a plain reason when switched off, suggestions to
/// start, the answer with what it looked at, a page opened when asked, and the conversation kept
/// between questions.
/// </summary>
public class AssistantPanelTests : BunitContext
{
    private readonly FakeAssistant _assistant = new();
    private readonly AssistantPanelState _state = new();

    public AssistantPanelTests()
    {
        Services.AddSingleton<IAssistantService>(_assistant);
        Services.AddSingleton(_state);
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Without_a_model_there_is_no_button()
    {
        _assistant.Status = new AssistantStatus(Configured: false, Enabled: false);

        IRenderedComponent<AssistantToggle> toggle = Render<AssistantToggle>();

        Assert.Empty(toggle.FindAll("button"));
    }

    [Fact]
    public void Switched_off_the_panel_says_who_can_turn_it_on()
    {
        _assistant.Status = new AssistantStatus(Configured: true, Enabled: false);
        IRenderedComponent<AssistantToggle> toggle = Render<AssistantToggle>();
        IRenderedComponent<AssistantPanel> panel = Render<AssistantPanel>();

        toggle.Find("button").Click();

        panel.WaitForAssertion(() => Assert.Contains("An Administrator can turn it on", panel.Markup));
        Assert.Empty(panel.FindAll("textarea"));
        Assert.Equal("true", toggle.Find("button").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_suggestion_is_asked_and_the_answer_shows_what_it_looked_at()
    {
        _assistant.Reply = new AssistantReply("Spending is at **71%** of the budget. See [Budget vs. Actual](/admin/reports/1/budget-vs-actual).",
            [new AssistantStep("Budget against actual, FY2026 Amendment 1 (Adopted)")], null);
        IRenderedComponent<AssistantPanel> panel = OpenPanel();

        panel.FindAll(".cb-assistant-suggestions button")[0].Click();

        panel.WaitForAssertion(() => Assert.Contains("<strong>71%</strong>", panel.Markup));
        Assert.Contains("href=\"/admin/reports/1/budget-vs-actual\"", panel.Markup);
        Assert.Contains("Looked at: Budget against actual", panel.Find(".cb-assistant-steps").TextContent);
        Assert.Equal("How are actuals compared to the budget so far this year?", _assistant.Asked!.Question);
        Assert.Equal("/", _assistant.Asked.CurrentPath);
    }

    [Fact]
    public void The_conversation_goes_back_with_the_next_question_and_a_requested_page_opens()
    {
        _assistant.Reply = new AssistantReply("Opening the budget book.", [], "/admin/budgets/1/book");
        IRenderedComponent<AssistantPanel> panel = OpenPanel();

        panel.Find("textarea").Input("First question");
        panel.Find("form").Submit();
        panel.Find("textarea").Input("Take me to the budget book");
        panel.Find("form").Submit();

        Assert.Equal([true, false], _assistant.Asked!.History.Select(m => m.FromUser));
        Assert.EndsWith("/admin/budgets/1/book", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_is_shown_as_a_message_and_not_sent_back_as_conversation()
    {
        _assistant.Refusal = "That is 40 questions in the last hour.";
        IRenderedComponent<AssistantPanel> panel = OpenPanel();

        panel.Find("textarea").Input("Hello");
        panel.Find("form").Submit();

        Assert.Contains("40 questions in the last hour", panel.Find(".cb-assistant-error").TextContent);
        panel.Find("textarea").Input("Again");
        panel.Find("form").Submit();
        Assert.Equal([true], _assistant.Asked!.History.Select(m => m.FromUser)); // the error is left out
    }

    private IRenderedComponent<AssistantPanel> OpenPanel()
    {
        _assistant.Status = new AssistantStatus(Configured: true, Enabled: true);
        _state.Status = _assistant.Status;
        IRenderedComponent<AssistantPanel> panel = Render<AssistantPanel>();
        _state.SetOpen(true);
        panel.WaitForAssertion(() => panel.Find("textarea"));
        return panel;
    }

    private sealed class FakeAssistant : IAssistantService
    {
        public AssistantStatus Status { get; set; } = new(true, true);
        public AssistantReply Reply { get; set; } = new("An answer.", [], null);
        public string? Refusal { get; set; }
        public AssistantRequest? Asked { get; private set; }

        public Task<AssistantStatus> StatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

        public Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken ct = default)
        {
            Asked = request;
            return Task.FromResult(Refusal is null ? Result.Success(Reply) : Result.Failure<AssistantReply>(Refusal));
        }

        public Task<AssistantSettingsDto> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(new AssistantSettingsDto(Status.Configured, Status.Enabled));

        public Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }
}
