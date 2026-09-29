using System.Text.Json;
using CivicBudget.Application.Assistant;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Portal;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The question box on the transparency portal, with a scripted model against the seeded snapshots.
/// The rules: it reads only this government's published budget (no draft, no other government, no
/// tool that writes); a portal that is switched off, or has no model, answers nothing and a portal
/// with no model never touches the database; and the monthly cap holds, even for questions asked at
/// the same moment.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class PortalQuestionTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string Maple = "maple-ridge-oh";
    private const string Pine = "pine-hollow-twp-oh";

    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_PortalQuestions");

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Noon Eastern on September 29, 2026.
    private static readonly DateTimeOffset September = new(2026, 9, 29, 16, 0, 0, TimeSpan.Zero);

    private static PortalQuestionService Service(AsyncServiceScope scope, IChatClient? model, int perMonth = 1000, TimeProvider? clock = null) => new(
        scope.ServiceProvider.GetRequiredService<ICivicBudgetDbContextFactory>(),
        scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>(),
        model is null ? [] : [new FunctionInvokingChatClient(model)],
        new PortalQuestionLimits(perMonth),
        clock ?? new Clock(September),
        NullLogger<PortalQuestionService>.Instance);

    private async Task<(int Month, int Asked)> CountAsync(string slug)
    {
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        Government g = await db.Governments.SingleAsync(g => g.PublicSlug == slug);
        return (g.PortalQuestionsMonth, g.PortalQuestionsAsked);
    }

    [Fact]
    public async Task Answers_come_from_this_governments_published_budget_and_nothing_else()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        var model = new ScriptedModel(
            ("fund", new() { ["fundCode"] = "1000" }),
            ("budget_overview", new() { ["fiscalYear"] = 2027 }),
            ("search", new() { ["text"] = "police" }));

        Result<PortalAnswer> answer = await Service(scope, model).AskAsync(Maple, 2026, "How much is the General Fund?");

        Assert.True(answer.IsSuccess, string.Join("; ", answer.Errors.Select(e => e.Message)));
        using (JsonDocument fund = JsonDocument.Parse(model.ToolResults[0]))
        {
            Assert.Equal("/transparency/maple-ridge-oh/2026/funds/1000", fund.RootElement.GetProperty("page").GetString());
        }

        Assert.Contains("FY2027 has no published budget", model.ToolResults[1], StringComparison.Ordinal); // the draft is not reachable
        Assert.Contains("Police", model.ToolResults[2], StringComparison.Ordinal);
        Assert.All(model.ToolResults, r => Assert.DoesNotContain("pine-hollow", r, StringComparison.Ordinal));
        Assert.Equal(["Fund 1000 General Fund, FY2026", "Searched FY2026 for \"police\""], answer.Value.LookedAt);
        Assert.Contains("Answer only from the tools", model.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tools_only_read()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        PortalBudgetDto budget = (await scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>().GetBudgetAsync(Maple, 2026))!;

        IEnumerable<string> names = new PortalTools(scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>(), budget).Tools().Select(t => t.Name);

        Assert.Equal(["budget_overview", "spending_breakdown", "revenue_breakdown", "fund", "department", "search", "outlook", "year_over_year", "glossary"], names);
    }

    [Fact]
    public async Task A_department_is_found_in_every_fund_it_spends_from_with_its_published_narrative()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        var model = new ScriptedModel(("department", new() { ["departmentCode"] = "620" }));

        await Service(scope, model).AskAsync(Maple, 2026, "What does Streets spend?");

        using JsonDocument shown = JsonDocument.Parse(model.ToolResults.Single());
        List<string?> funds = [.. shown.RootElement.GetProperty("funds").EnumerateArray().Select(f => f.GetProperty("fund").GetString())];
        Assert.Contains(funds, f => f!.StartsWith("1000 ", StringComparison.Ordinal));
        Assert.Contains(funds, f => f!.StartsWith("2011 ", StringComparison.Ordinal)); // Streets spends from the General and Street funds, as seeded
    }

    [Fact]
    public async Task A_portal_switched_off_answers_nothing_and_counts_nothing()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        PortalQuestionService service = Service(scope, new ScriptedModel());

        Assert.False(await service.IsAvailableAsync(Pine)); // off for Pine Hollow, as seeded
        Result<PortalAnswer> answer = await service.AskAsync(Pine, (await scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>().GetBudgetAsync(Pine, null))!.FiscalYear, "Hello?");

        Assert.Equal(PortalQuestionService.Unavailable, answer.Errors.Single().Message);
        Assert.Equal((0, 0), await CountAsync(Pine));
    }

    [Fact]
    public async Task Without_a_model_nothing_reads_the_database()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        var service = new PortalQuestionService(new ThrowingFactory(), null!, [], new PortalQuestionLimits(10), TimeProvider.System, NullLogger<PortalQuestionService>.Instance);

        Assert.False(service.ModelConfigured);
        Assert.False(await service.IsAvailableAsync(Maple));
        Assert.Equal(PortalQuestionService.Unavailable, (await service.AskAsync(Maple, 2026, "Hello?")).Errors.Single().Message);
    }

    [Fact]
    public async Task The_monthly_cap_holds_and_a_new_month_starts_again()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        var clock = new Clock(September);
        PortalQuestionService service = Service(scope, new ScriptedModel(), perMonth: 2, clock);

        Assert.True((await service.AskAsync(Maple, 2026, "One")).IsSuccess);
        Assert.True((await service.AskAsync(Maple, 2026, "Two")).IsSuccess);
        Result<PortalAnswer> third = await service.AskAsync(Maple, 2026, "Three");
        Assert.Contains("all the questions it can this month", third.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Equal((202609, 2), await CountAsync(Maple));

        clock.Now = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        Assert.True((await service.AskAsync(Maple, 2026, "Four")).IsSuccess);
        Assert.Equal((202610, 1), await CountAsync(Maple));
    }

    [Fact]
    public async Task Questions_asked_at_the_same_moment_cannot_pass_the_cap_together()
    {
        const int cap = 3;
        Result<PortalAnswer>[] answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using AsyncServiceScope scope = _database.CreateScope();
            return await Service(scope, new ScriptedModel(), perMonth: cap).AskAsync(Maple, 2026, $"Question {i}");
        }));

        Assert.Equal(cap, answers.Count(a => a.IsSuccess));
        Assert.Equal((202609, cap), await CountAsync(Maple));
    }

    [Fact]
    public async Task A_long_question_is_refused_before_it_is_counted()
    {
        await using AsyncServiceScope scope = _database.CreateScope();

        Result<PortalAnswer> answer = await Service(scope, new ScriptedModel()).AskAsync(Maple, 2026, new string('x', PortalQuestionService.QuestionMaxLength + 1));

        Assert.True(answer.IsFailure);
        Assert.Equal((0, 0), await CountAsync(Maple));
    }

    [Fact]
    public async Task Only_an_administrator_turns_portal_questions_on_and_it_is_audited()
    {
        Guid pine;
        await using (CivicBudgetDbContext db = _database.CreateContext(tenant: null))
        {
            pine = (await db.Governments.SingleAsync(g => g.PublicSlug == Pine)).Id;
        }

        await using (AsyncServiceScope fiscal = _database.CreateScopeAs(Roles.FinanceDirector, pine))
        {
            Assert.True((await fiscal.ServiceProvider.GetRequiredService<IAssistantService>().SetPortalQuestionsEnabledAsync(true)).IsFailure);
        }

        await using AsyncServiceScope admin = _database.CreateScopeAs(Roles.Admin, pine);
        IAssistantService assistant = admin.ServiceProvider.GetRequiredService<IAssistantService>();
        Assert.True((await assistant.SetPortalQuestionsEnabledAsync(true)).IsSuccess);

        Assert.True((await assistant.GetSettingsAsync()).PortalQuestionsEnabled);
        await using CivicBudgetDbContext check = _database.CreateContext(pine);
        Assert.True(await check.AuditEntries.AnyAsync(a => a.EntityId == pine && a.Description == "Turned on questions from the public on the portal"));
    }

    private sealed class ThrowingFactory : ICivicBudgetDbContextFactory
    {
        public Task<ICivicBudgetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The database was read.");
    }
}
