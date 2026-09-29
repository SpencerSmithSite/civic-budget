using System.Security.Claims;
using CivicBudget.Application.Assistant;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The assistant's actions against the seeded database with a scripted model. The rules: a tool only
/// proposes, and nothing is saved until the user confirms; a proposal runs once, only in the tab that
/// made it, and not if the budget moved on since the preview; a user who could not make the change on
/// the page gets no proposal; and a confirmed change says in the audit trail that it came through
/// the assistant.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class AssistantActionTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _police;
    private Guid _draft;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_AssistantActions");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext maple = _database.CreateContext(_mapleRidge);
        _police = (await maple.Departments.SingleAsync(d => d.Code == "110")).Id;
        _draft = (await maple.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static AssistantService Assistant(AsyncServiceScope scope, ScriptedModel model) => new(
        scope.ServiceProvider.GetRequiredService<ICivicBudgetDbContextFactory>(),
        scope.ServiceProvider.GetRequiredService<ICurrentUser>(),
        [new FunctionInvokingChatClient(model)],
        scope.ServiceProvider.GetServices<IAssistantToolProvider>(),
        new AssistantUsageLimiter(TimeProvider.System),
        scope.ServiceProvider.GetRequiredService<AssistantProposals>(),
        scope.ServiceProvider.GetRequiredService<ISecurityEventLog>(), scope.ServiceProvider.GetRequiredService<Application.Publishing.IPublishedSnapshotCacheInvalidator>(),
        TimeProvider.System,
        NullLogger<AssistantService>.Instance);

    private static async Task<AssistantReply> AskAsync(AssistantService assistant, string question)
    {
        Result<AssistantReply> reply = await assistant.AskAsync(new AssistantRequest([], question, null));
        Assert.True(reply.IsSuccess, string.Join("; ", reply.Errors.Select(e => e.Message)));
        return reply.Value;
    }

    private async Task<Dictionary<Guid, decimal>> UtilitiesAsync()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        return await db.BudgetLines.Where(l => l.BudgetVersionId == _draft && l.Account.Code == "5320").ToDictionaryAsync(l => l.Id, l => l.Amount);
    }

    [Fact]
    public async Task A_five_year_plan_is_proposed_saved_only_on_confirm_and_audited_as_the_assistants()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        AssistantService assistant = Assistant(scope, new ScriptedModel(("propose_multi_year_plan", new() { ["years"] = 5, ["revenuePercent"] = 4m, ["expenditurePercent"] = 4m })));
        IBudgetPlanService plans = scope.ServiceProvider.GetRequiredService<IBudgetPlanService>();
        int yearsBefore = (await plans.GetAsync(_draft))!.Years;

        AssistantReply reply = await AskAsync(assistant, "Build a five-year plan where each year goes up 4%");

        AssistantProposalDto proposal = Assert.Single(reply.Proposals);
        Assert.StartsWith("Plan FY2027 Original for 5 years: revenues +4% and spending +4% a year", proposal.Title, StringComparison.Ordinal);
        Assert.Contains(proposal.Rows, r => r.Label.StartsWith("1000 General Fund, ending FY2031", StringComparison.Ordinal));
        Assert.Equal(yearsBefore, (await plans.GetAsync(_draft))!.Years); // nothing saved by proposing

        Result<ProposalOutcome> confirmed = await assistant.ConfirmAsync(proposal.Id);

        Assert.True(confirmed.IsSuccess, string.Join("; ", confirmed.Errors.Select(e => e.Message)));
        BudgetPlanDto saved = (await plans.GetAsync(_draft))!;
        Assert.Equal(5, saved.Years);
        Assert.All(saved.Rates, r => Assert.Equal((4m, 4m), (r.RevenuePercent, r.ExpenditurePercent)));
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == _draft && a.Description == "Through the assistant: " + proposal.Title));

        Result<ProposalOutcome> again = await assistant.ConfirmAsync(proposal.Id);
        Assert.Contains("already used", again.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lines_are_raised_together_and_a_preview_the_budget_has_moved_past_is_refused_whole()
    {
        Dictionary<Guid, decimal> before = await UtilitiesAsync();
        Assert.True(before.Count >= 2, "The seed has utilities in several departments.");
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        AssistantService assistant = Assistant(scope, new ScriptedModel(("propose_line_changes", new() { ["account"] = "utilities", ["changePercent"] = 5m })));

        AssistantReply reply = await AskAsync(assistant, "Raise utilities 5% everywhere");
        AssistantProposalDto proposal = Assert.Single(reply.Proposals);
        Assert.Equal(before.Count, proposal.Rows.Count);
        Assert.Equal(before, await UtilitiesAsync());

        // Someone changes one of the lines on the page before the proposal is confirmed.
        (Guid movedId, decimal movedAmount) = before.First();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().UpdateLineAmountAsync(_draft, movedId, movedAmount + 1m)).IsSuccess);

        Result<ProposalOutcome> stale = await assistant.ConfirmAsync(proposal.Id);

        Assert.True(stale.IsFailure);
        Assert.Contains("changed since", stale.Errors[0].Message, StringComparison.Ordinal);
        Dictionary<Guid, decimal> after = await UtilitiesAsync();
        Assert.All(before.Keys.Where(id => id != movedId), id => Assert.Equal(before[id], after[id])); // none of the others moved

        // Asked again, the fresh proposal goes through.
        AssistantReply fresh = await AskAsync(assistant, "Try again");
        Assert.True((await assistant.ConfirmAsync(fresh.Proposals.Single().Id)).IsSuccess);
        after = await UtilitiesAsync();
        Assert.Equal(Domain.Common.Money.Round((movedAmount + 1m) * 1.05m), after[movedId]);
    }

    [Fact]
    public async Task A_viewer_gets_no_proposal_to_confirm()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.Viewer, _mapleRidge);
        var model = new ScriptedModel(
            ("propose_multi_year_plan", new() { ["years"] = 5, ["revenuePercent"] = 4m, ["expenditurePercent"] = 4m }),
            ("propose_line_changes", new() { ["account"] = "utilities", ["changePercent"] = 5m }),
            ("propose_start_budget", new() { ["fiscalYear"] = 2028 }),
            ("propose_budget_message", new() { ["body"] = "Dear Council," }));

        AssistantReply reply = await AskAsync(Assistant(scope, model), "Change everything");

        Assert.Empty(reply.Proposals);
        Assert.Equal(4, model.ToolResults.Count);
        Assert.All(model.ToolResults, r => Assert.Contains("problem", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_department_head_can_propose_for_their_open_request_only()
    {
        Guid streets;
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            streets = (await db.Departments.SingleAsync(d => d.Code == "620")).Id;
        }

        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-streets"));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Pat Rivera"));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.DepartmentHead));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        identity.AddClaim(new Claim(ClaimNames.DepartmentId, streets.ToString()));
        identity.AddClaim(new Claim(ClaimNames.DepartmentId, _police.ToString()));
        await using AsyncServiceScope scope = _database.CreateScope(user: new ClaimsPrincipal(identity));
        var model = new ScriptedModel(
            ("propose_line_changes", new() { ["departmentCode"] = "725", ["changePercent"] = 10m }),
            ("propose_multi_year_plan", new() { ["years"] = 5, ["revenuePercent"] = 4m, ["expenditurePercent"] = 4m }),
            ("propose_department_narrative", new() { ["departmentCode"] = "110", ["narrative"] = "Police asks for two cruisers." }),
            ("propose_department_narrative", new() { ["departmentCode"] = "620", ["narrative"] = "Streets asks for a new plow." }));

        AssistantReply reply = await AskAsync(Assistant(scope, model), "Raise Finance 10%, set the plan, and write our narratives");

        Assert.Contains("No line matches", model.ToolResults[0], StringComparison.Ordinal);       // another department's lines are not theirs
        Assert.Contains("\"problem\"", model.ToolResults[1], StringComparison.Ordinal);          // the plan belongs to the Fiscal Officer
        Assert.Contains("submitted request is locked", model.ToolResults[2], StringComparison.Ordinal); // Police is submitted, as seeded
        Assert.StartsWith("Save this as Streets & Service's narrative", Assert.Single(reply.Proposals).Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_proposal_belongs_to_the_tab_that_made_it()
    {
        Guid id;
        await using (AsyncServiceScope first = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge))
        {
            AssistantReply reply = await AskAsync(Assistant(first, new ScriptedModel(("propose_line_changes", new() { ["account"] = "utilities", ["newAmount"] = 1m }))), "Set utilities to a dollar");
            id = reply.Proposals.Single().Id;
        }

        await using AsyncServiceScope other = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        Result<ProposalOutcome> confirmed = await Assistant(other, new ScriptedModel()).ConfirmAsync(id);

        Assert.True(confirmed.IsFailure);
        Assert.DoesNotContain(1m, (await UtilitiesAsync()).Values);
    }

    [Fact]
    public async Task A_cancelled_proposal_cannot_be_confirmed()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        AssistantService assistant = Assistant(scope, new ScriptedModel(("propose_line_changes", new() { ["account"] = "utilities", ["newAmount"] = 1m })));
        AssistantReply reply = await AskAsync(assistant, "Set utilities to a dollar");

        assistant.Discard(reply.Proposals.Single().Id);

        Assert.True((await assistant.ConfirmAsync(reply.Proposals.Single().Id)).IsFailure);
        Assert.DoesNotContain(1m, (await UtilitiesAsync()).Values);
    }

    [Fact]
    public async Task A_budget_message_is_saved_word_for_word_on_confirm()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        const string body = "Members of Council,\n\nThis budget holds the line on the General Fund.";
        AssistantService assistant = Assistant(scope, new ScriptedModel(("propose_budget_message", new() { ["body"] = body, ["signedBy"] = "Rebecca Lang", ["signerTitle"] = "Mayor" })));
        Application.Reports.IBudgetBookService books = scope.ServiceProvider.GetRequiredService<Application.Reports.IBudgetBookService>();
        string? before = (await books.GetAsync(_draft))!.MessageBody;

        AssistantProposalDto proposal = (await AskAsync(assistant, "Write the budget message")).Proposals.Single();
        Assert.Equal(body, proposal.Text);
        Assert.Equal(before, (await books.GetAsync(_draft))!.MessageBody);

        Assert.True((await assistant.ConfirmAsync(proposal.Id)).IsSuccess);
        Application.Reports.BudgetBookPageDto saved = (await books.GetAsync(_draft))!;
        Assert.Equal((body, "Rebecca Lang", "Mayor"), (saved.MessageBody, saved.MessageSignedBy, saved.MessageSignerTitle));
    }

    [Fact]
    public async Task Actuals_are_previewed_from_the_ERP_and_brought_in_on_confirm()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        AssistantService assistant = Assistant(scope, new ScriptedModel(("propose_fetch_actuals", new() { ["fiscalYear"] = 2026 })));

        AssistantProposalDto proposal = (await AskAsync(assistant, "Bring in this year's actuals")).Proposals.Single();

        Assert.StartsWith("Bring in FY2026 actuals from", proposal.Title, StringComparison.Ordinal);
        Assert.Contains(proposal.Rows, r => r.Label == "1000 General Fund" && r.After.StartsWith("received $", StringComparison.Ordinal));
        Result<ProposalOutcome> confirmed = await assistant.ConfirmAsync(proposal.Id);
        Assert.True(confirmed.IsSuccess, string.Join("; ", confirmed.Errors.Select(e => e.Message)));
        Assert.StartsWith("FY2026 actuals are in", confirmed.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starting_a_year_that_already_has_a_budget_is_refused_before_anything_is_proposed()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        var model = new ScriptedModel(("propose_start_budget", new() { ["fiscalYear"] = 2027 }), ("propose_start_budget", new() { ["fiscalYear"] = 2040 }));

        AssistantReply reply = await AskAsync(Assistant(scope, model), "Start next year's budget");

        Assert.Empty(reply.Proposals);
        Assert.Contains("FY2027 already has a budget", model.ToolResults[0], StringComparison.Ordinal);
        Assert.Contains("FY2040 is not set up yet", model.ToolResults[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fixing_the_street_fund_brings_it_exactly_to_its_limit()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        var model = new ScriptedModel(("propose_fund_fix", new() { ["fundCode"] = "2011" }), ("propose_fund_fix", new() { ["fundCode"] = "1000" }));
        AssistantService assistant = Assistant(scope, model);
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();

        // Read, not hard-coded, so a change to the seeded personnel does not break a test about the assistant.
        decimal over = (await entry.GetWorkspaceAsync(_draft))!.FundBalances.Single(f => f.FundCode == "2011").Limit.AmountOverLimit;

        AssistantReply reply = await AskAsync(assistant, "Fix the Street fund");

        AssistantProposalDto proposal = Assert.Single(reply.Proposals);
        Assert.Equal($"Bring fund 2011 Street Construction, Maintenance & Repair within its limit: cut ${over.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-US"))}", proposal.Title);
        Assert.Contains("is within its limit", model.ToolResults[1], StringComparison.Ordinal);
        Assert.True((await assistant.ConfirmAsync(proposal.Id)).IsSuccess);
        FundBalanceDto street = (await entry.GetWorkspaceAsync(_draft))!.FundBalances.Single(f => f.FundCode == "2011");
        Assert.Equal(0m, street.Limit.AmountOverLimit);
        Assert.Equal(street.Summary.EstimatedResources, street.Summary.Appropriations);
    }
}
