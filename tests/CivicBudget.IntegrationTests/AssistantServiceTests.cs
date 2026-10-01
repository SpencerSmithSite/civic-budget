using System.Security.Claims;
using System.Text.Json;
using CivicBudget.Application.Assistant;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The assistant end to end with a scripted model: the model asks for a tool, the real tool-calling
/// layer runs it against the seeded database as the signed-in user, and the test reads exactly what
/// the model was shown. That is where permissions matter: a department head's tools return only
/// their departments, and a whole-government report is not shown to them at all.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class AssistantServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;
    private Guid _police;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Assistant");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
        await using CivicBudgetDbContext maple = _database.CreateContext(_mapleRidge);
        _police = (await maple.Departments.SingleAsync(d => d.Code == "110")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope DepartmentHead()
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-police"));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Chief Morgan Hale"));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.DepartmentHead));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        identity.AddClaim(new Claim(ClaimNames.DepartmentId, _police.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    // The service as the web host builds it, with the scripted model behind the real tool-calling layer.
    private static AssistantService Assistant(AsyncServiceScope scope, ScriptedModel? model, AssistantUsageLimiter? limiter = null) => new(
        scope.ServiceProvider.GetRequiredService<ICivicBudgetDbContextFactory>(),
        scope.ServiceProvider.GetRequiredService<ICurrentUser>(),
        model is null ? [] : [new FunctionInvokingChatClient(model)],
        scope.ServiceProvider.GetServices<IAssistantToolProvider>(),
        limiter ?? new AssistantUsageLimiter(TimeProvider.System),
        scope.ServiceProvider.GetRequiredService<AssistantProposals>(),
        scope.ServiceProvider.GetRequiredService<ISecurityEventLog>(), scope.ServiceProvider.GetRequiredService<Application.Publishing.IPublishedSnapshotCacheInvalidator>(),
        TimeProvider.System,
        NullLogger<AssistantService>.Instance);

    private static AssistantRequest Ask(string question, string? path = null) => new([], question, path);

    [Fact]
    public async Task A_question_about_the_year_uses_budget_against_actual_as_the_user_and_says_where_to_look()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        var model = new ScriptedModel(("budget_vs_actual", new() { ["versionId"] = null, ["fundCode"] = null }));

        Result<AssistantReply> reply = await Assistant(scope, model).AskAsync(Ask("How are actuals compared to our budget so far this year?"));

        Assert.True(reply.IsSuccess, string.Join("; ", reply.Errors.Select(e => e.Message)));
        string shown = model.ToolResults.Single();
        Assert.Contains("FY2026 Amendment 1 (Adopted)", shown, StringComparison.Ordinal); // the current year's adopted budget
        Assert.Contains("1000 General Fund", shown, StringComparison.Ordinal);
        Assert.Contains("/budget-vs-actual", shown, StringComparison.Ordinal);
        Assert.Contains(reply.Value.Steps, s => s.Summary.StartsWith("Budget against actual", StringComparison.Ordinal));
        Assert.Contains("You are helping Test FinanceDirector (Fiscal Officer)", model.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_department_head_is_shown_only_their_department_and_no_whole_government_report()
    {
        await using AsyncServiceScope scope = DepartmentHead();
        var model = new ScriptedModel(
            ("department_budget", new() { ["versionId"] = null, ["departmentCode"] = null }),
            ("fund_summary", new() { ["versionId"] = null }),
            ("certificate", new() { ["versionId"] = null }),
            ("department_budget", new() { ["versionId"] = null, ["departmentCode"] = "725" }));

        Result<AssistantReply> reply = await Assistant(scope, model).AskAsync(Ask("What is every department's budget?"));

        Assert.True(reply.IsSuccess);
        Assert.Contains("110 Police", model.ToolResults[0], StringComparison.Ordinal);
        Assert.DoesNotContain("725 Finance", model.ToolResults[0], StringComparison.Ordinal);
        // The fund summary is exactly what the Fund Summary page shows this user, from the same service.
        Application.Reports.FundSummaryReportDto page = (await scope.ServiceProvider.GetRequiredService<Application.Reports.IReportService>()
            .FundSummaryAsync((await scope.ServiceProvider.GetRequiredService<Application.Budgets.IBudgetEntryService>().ListVersionsAsync())
                .Where(v => v.Status == Domain.Budgets.BudgetStatus.Adopted && v.Year == 2026).MaxBy(v => v.VersionNumber)!.Id))!;
        using (JsonDocument summary = JsonDocument.Parse(model.ToolResults[1]))
        {
            Assert.Equal(page.Funds.Select(f => $"{f.FundCode} {f.FundName}").Append("All funds"),
                summary.RootElement.GetProperty("funds").EnumerateArray().Select(f => f.GetProperty("fund").GetString()));
        }

        Assert.Contains("not available to this user", model.ToolResults[2], StringComparison.Ordinal);
        Assert.Contains("that this user can see", model.ToolResults[3], StringComparison.Ordinal);
        Assert.Contains("department user for 110 Police", model.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task This_page_means_the_budget_on_it()
    {
        Guid draft;
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            draft = (await db.BudgetVersions.SingleAsync(v => v.Status == Domain.Budgets.BudgetStatus.Draft)).Id;
        }

        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        var model = new ScriptedModel(("fund_summary", new() { ["versionId"] = null }));

        await Assistant(scope, model).AskAsync(Ask("Which funds are over their limit?", $"/admin/budgets/{draft}/plan"));

        using JsonDocument shown = JsonDocument.Parse(model.ToolResults.Single());
        Assert.Equal("FY2027 Original (Draft)", shown.RootElement.GetProperty("version").GetString());
        Assert.Equal(["2011 Street Construction, Maintenance & Repair"], shown.RootElement.GetProperty("funds").EnumerateArray()
            .Where(f => !f.GetProperty("withinLimit").GetBoolean() && f.GetProperty("fund").GetString() != "All funds").Select(f => f.GetProperty("fund").GetString())); // as seeded
    }

    [Fact]
    public async Task On_next_years_draft_how_the_year_is_going_is_about_the_year_under_way()
    {
        Guid draft;
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            draft = (await db.BudgetVersions.SingleAsync(v => v.Status == Domain.Budgets.BudgetStatus.Draft)).Id;
        }

        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        var model = new ScriptedModel(("budget_vs_actual", new()), ("fund_summary", new()));

        await Assistant(scope, model).AskAsync(Ask("How are we doing?", $"/admin/budgets/{draft}/book"));

        Assert.Contains("FY2026 Amendment 1 (Adopted)", model.ToolResults[0], StringComparison.Ordinal); // the year with books
        Assert.Contains("FY2027 Original (Draft)", model.ToolResults[1], StringComparison.Ordinal);      // the page's budget otherwise
    }

    [Fact]
    public async Task Every_question_is_in_the_security_log_with_the_tools_but_not_the_words()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        var model = new ScriptedModel(("list_budget_versions", new()), ("certificate", new() { ["versionId"] = null }));

        await Assistant(scope, model).AskAsync(Ask("A private question about the certificate"));

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        SecurityEvent logged = await db.SecurityEvents.SingleAsync(e => e.Kind == SecurityEventKind.AssistantUsed);
        Assert.Equal("Looked at: list_budget_versions, certificate", logged.Detail);
        Assert.DoesNotContain("private", logged.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_model_or_with_the_government_switched_off_there_is_no_answer()
    {
        await using (AsyncServiceScope maple = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge))
        {
            AssistantStatus none = await Assistant(maple, model: null).StatusAsync();
            Assert.Equal((false, false), (none.Configured, none.Available));
        }

        await using AsyncServiceScope pine = _database.CreateScopeAs(Roles.FinanceDirector, _pineHollow); // seeded with the assistant off
        AssistantService assistant = Assistant(pine, new ScriptedModel());
        Assert.Equal((true, false), ((await assistant.StatusAsync()).Configured, (await assistant.StatusAsync()).Available));
        Assert.Contains("An Administrator can turn it on", (await assistant.AskAsync(Ask("Hello?"))).Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_an_administrator_turns_it_on_and_that_is_audited()
    {
        await using (AsyncServiceScope officer = _database.CreateScopeAs(Roles.FinanceDirector, _pineHollow))
        {
            Assert.True((await Assistant(officer, new ScriptedModel()).SetEnabledAsync(true)).IsFailure);
        }

        await using AsyncServiceScope admin = _database.CreateScopeAs(Roles.Admin, _pineHollow);
        AssistantService assistant = Assistant(admin, new ScriptedModel());
        Assert.True((await assistant.SetEnabledAsync(true)).IsSuccess);
        Assert.True((await assistant.StatusAsync()).Available);

        await using CivicBudgetDbContext db = _database.CreateContext(_pineHollow);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Description == "Turned Civic Buddy on"));
    }

    [Fact]
    public async Task A_person_who_asks_too_often_is_told_to_wait()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.Viewer, _mapleRidge);
        var limiter = new AssistantUsageLimiter(TimeProvider.System);
        for (int i = 0; i < AssistantUsageLimiter.QuestionsPerHour; i++)
        {
            limiter.TryTake("user-Viewer");
        }

        Result<AssistantReply> reply = await Assistant(scope, new ScriptedModel(), limiter).AskAsync(Ask("One more?"));

        Assert.Contains("questions in the last hour", reply.Errors.Single().Message, StringComparison.Ordinal);
    }
}
