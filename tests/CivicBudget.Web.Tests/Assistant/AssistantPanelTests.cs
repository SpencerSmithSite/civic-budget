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
            [new AssistantStep("Budget against actual, FY2026 Amendment 1 (Adopted)")], null, []);
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
        _assistant.Reply = new AssistantReply("Opening the budget book.", [], "/admin/budgets/1/book", []);
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

    [Fact]
    public void A_proposal_shows_the_change_and_only_the_click_confirms_it()
    {
        var proposal = new AssistantProposalDto(Guid.NewGuid(), "Change 2 lines in FY2027 Original: utilities +5%",
            [new ProposalRow("1000-110-5320 Utilities", "$1,000.00", "$1,050.00"), new ProposalRow("1000-620-5320 Utilities", "$2,000.00", "$2,100.00")],
            0, null, "Change 2 lines", "Total $3,000.00 becomes $3,150.00 (+$150.00).");
        _assistant.Reply = new AssistantReply("I can raise both utilities lines 5%. Confirm on the card below.", [], null, [proposal]);
        _assistant.Outcome = Result.Success(new ProposalOutcome("2 lines are updated.", "/admin/budgets/1"));
        IRenderedComponent<AssistantPanel> panel = OpenPanel();
        int generation = _state.PageGeneration;

        panel.Find("textarea").Input("Raise utilities 5%");
        panel.Find("form").Submit();

        Assert.Contains("$1,050.00", panel.Find(".cb-proposal table").TextContent);
        Assert.Null(_assistant.Confirmed);
        panel.Find(".cb-proposal-actions .btn-primary").Click();

        Assert.Equal(proposal.Id, _assistant.Confirmed);
        panel.WaitForAssertion(() => Assert.Contains("2 lines are updated.", panel.Find(".cb-proposal-status").TextContent));
        Assert.Empty(panel.FindAll(".cb-proposal-actions"));
        Assert.Equal(generation + 1, _state.PageGeneration); // the page beneath reloads to show the change
    }

    [Fact]
    public void A_cancelled_proposal_is_dropped_and_the_model_hears_what_happened()
    {
        var proposal = new AssistantProposalDto(Guid.NewGuid(), "Plan FY2027 Original for 5 years", [], 0, null, "Save the plan", null);
        _assistant.Reply = new AssistantReply("Confirm below.", [], null, [proposal]);
        IRenderedComponent<AssistantPanel> panel = OpenPanel();
        panel.Find("textarea").Input("Five years at 4%");
        panel.Find("form").Submit();

        panel.Find(".cb-proposal-actions .btn-outline-secondary").Click();
        panel.Find("textarea").Input("Never mind");
        panel.Find("form").Submit();

        Assert.Equal(proposal.Id, _assistant.Discarded);
        Assert.Contains("Cancelled. Nothing changed.", panel.Find(".cb-proposal-status").TextContent);
        Assert.Contains("The user cancelled", _assistant.Asked!.History[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_confirmation_says_nothing_changed()
    {
        var proposal = new AssistantProposalDto(Guid.NewGuid(), "Change 1 line", [new ProposalRow("Utilities", "$1.00", "$2.00")], 0, null, "Change 1 line", null);
        _assistant.Reply = new AssistantReply("Confirm below.", [], null, [proposal]);
        _assistant.Outcome = Result.Failure<ProposalOutcome>("5320 Utilities (Police) changed since the change was worked out.");
        IRenderedComponent<AssistantPanel> panel = OpenPanel();
        int generation = _state.PageGeneration;
        panel.Find("textarea").Input("Raise it");
        panel.Find("form").Submit();

        panel.Find(".cb-proposal-actions .btn-primary").Click();

        panel.WaitForAssertion(() => Assert.Contains("Nothing changed. 5320 Utilities (Police) changed since", panel.Find(".cb-proposal-status").TextContent));
        Assert.Equal(generation, _state.PageGeneration);
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
        public AssistantReply Reply { get; set; } = new("An answer.", [], null, []);
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

        public Task<Result> SetPortalQuestionsEnabledAsync(bool enabled, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Result<ProposalOutcome> Outcome { get; set; } = Result.Success(new ProposalOutcome("Done.", null));
        public Guid? Confirmed { get; private set; }
        public Guid? Discarded { get; private set; }

        public Task<Result<ProposalOutcome>> ConfirmAsync(Guid proposalId, CancellationToken ct = default)
        {
            Confirmed = proposalId;
            return Task.FromResult(Outcome);
        }

        public void Discard(Guid proposalId) => Discarded = proposalId;
    }
}
