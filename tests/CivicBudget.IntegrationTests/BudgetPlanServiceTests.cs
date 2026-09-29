using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Portal;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The multi-year plan against the seeded FY2027 draft: who may change the percentages and which
/// lines, how a typed year saves and resets, and that the plan travels into an amendment and onto
/// the portal when a budget is published.
/// </summary>
[Collection(SqlServerTests.Name)]
public class BudgetPlanServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _streets;
    private Guid _police;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Plan");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _streets = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
        _police = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope As(string role, params Guid[] departments)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        foreach (Guid department in departments)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, department.ToString()));
        }

        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    private static IBudgetPlanService Plans(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBudgetPlanService>();

    [Fact]
    public async Task The_seeded_draft_plans_five_years_with_its_project_typed_into_the_third()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);

        BudgetPlanDto plan = (await Plans(scope).GetAsync(_draft2027))!;

        Assert.Equal([2027, 2028, 2029, 2030, 2031], plan.FiscalYears);
        Assert.True(plan.CanEditAssumptions);
        Assert.Equal([3.5m, 3m, 3m, 3m], plan.Rates.Select(r => r.ExpenditurePercent));
        PlanLineDto project = plan.Lines.Single(l => l.AccountNumber == "4901-620-5520");
        Assert.Equal(250_000m, project.Amounts[2]);
        Assert.Equal([false, false, true, false, false], project.Typed);

        // Each fund's ending balance is the next year's beginning balance.
        Assert.All(plan.Funds, f => Assert.All(f.Years.Skip(1).Zip(f.Years), pair => Assert.Equal(pair.Second.EndingBalance, pair.First.BeginningBalance)));
    }

    [Fact]
    public async Task The_fiscal_officer_sets_the_years_and_percentages_and_every_calculated_year_follows()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);

        Result saved = await Plans(scope).SavePlanAsync(new SavePlanRequest(_draft2027, 3, false,
            [new PlanRateDto(1, 2028, 0m, 10m), new PlanRateDto(2, 2029, 0m, 10m)]));

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        BudgetPlanDto plan = (await Plans(scope).GetAsync(_draft2027))!;
        Assert.Equal(3, plan.Years);
        PlanLineDto line = plan.Lines.First(l => l.AccountType == Domain.Accounts.AccountType.Expenditure && !l.Typed.Any(t => t));
        Assert.Equal(Domain.Common.Money.Round(line.Amounts[0] * 1.1m), line.Amounts[1]);
        Assert.Equal(250_000m, plan.Lines.Single(l => l.AccountNumber == "4901-620-5520").Amounts[2]); // typed years inside the plan stay
    }

    [Theory]
    [InlineData(Roles.DepartmentHead)]
    [InlineData(Roles.Viewer)]
    public async Task Only_the_fiscal_authority_changes_the_assumptions(string role)
    {
        await using AsyncServiceScope scope = As(role, _streets);

        Assert.False((await Plans(scope).GetAsync(_draft2027))!.CanEditAssumptions);
        Assert.True((await Plans(scope).SavePlanAsync(new SavePlanRequest(_draft2027, 2, false, []))).IsFailure);
    }

    [Fact]
    public async Task A_department_head_plans_their_own_open_lines_only_and_sees_no_fund_totals()
    {
        await using AsyncServiceScope scope = As(Roles.DepartmentHead, _streets, _police);
        BudgetPlanDto plan = (await Plans(scope).GetAsync(_draft2027))!;

        Assert.Empty(plan.Funds);
        Assert.All(plan.Lines, l => Assert.True(l.DepartmentCode is "620" or "110", l.AccountNumber));
        PlanLineDto street = plan.Lines.First(l => l.DepartmentCode == "620");
        PlanLineDto police = plan.Lines.First(l => l.DepartmentCode == "110");
        Assert.True(street.CanEdit);
        Assert.False(police.CanEdit); // Police has submitted its request

        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, street.LineId, 1, 12_345m)).IsSuccess);
        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, police.LineId, 1, 12_345m)).IsFailure);

        await using AsyncServiceScope fd = As(Roles.FinanceDirector);
        BudgetPlanDto after = (await Plans(fd).GetAsync(_draft2027))!;
        PlanLineDto saved = after.Lines.Single(l => l.LineId == street.LineId);
        Assert.Equal((12_345m, true), (saved.Amounts[1], saved.Typed[1]));
        PlanLineDto outsider = after.Lines.First(l => l.DepartmentCode is "725");
        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, outsider.LineId, 1, 1m)).IsFailure);
    }

    [Fact]
    public async Task A_typed_year_goes_back_to_the_calculation_and_a_bad_year_is_refused()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PlanLineDto project = (await Plans(scope).GetAsync(_draft2027))!.Lines.Single(l => l.AccountNumber == "4901-620-5520");

        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, project.LineId, 2, null)).IsSuccess);
        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, project.LineId, 0, 1m)).IsFailure);  // the budget year is typed in the worksheet
        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, project.LineId, 5, 1m)).IsFailure);  // outside a five-year plan
        Assert.True((await Plans(scope).SetPlannedAmountAsync(_draft2027, project.LineId, 1, -1m)).IsFailure);

        PlanLineDto after = (await Plans(scope).GetAsync(_draft2027))!.Lines.Single(l => l.LineId == project.LineId);
        Assert.DoesNotContain(true, after.Typed);
    }

    [Fact]
    public async Task An_amendment_carries_the_adopted_plan_and_an_adopted_plan_is_locked()
    {
        Guid adopted2026;
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            adopted2026 = (await db.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null && v.VersionNumber > 1)).Id;
        }

        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        Assert.False((await Plans(scope).GetAsync(adopted2026))!.CanEditAssumptions);
        Assert.True((await Plans(scope).SavePlanAsync(new SavePlanRequest(adopted2026, 2, false, []))).IsFailure);

        Result<Guid> amendment = await scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>().CreateAmendmentAsync(adopted2026, "Plan carry test");

        Assert.True(amendment.IsSuccess, string.Join("; ", amendment.Errors.Select(e => e.Message)));
        BudgetPlanDto plan = (await Plans(scope).GetAsync(amendment.Value))!;
        Assert.Equal(BudgetVersion.DefaultPlanYears, plan.Years);
        Assert.All(plan.Rates, r => Assert.Equal((2.5m, 3m), (r.RevenuePercent, r.ExpenditurePercent)));
        Assert.True(plan.CanEditAssumptions);
    }

    [Fact]
    public async Task The_portal_outlook_shows_the_published_plan_and_nothing_for_a_one_year_budget()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        OutlookDto outlook = (await portal.GetOutlookAsync("maple-ridge-oh", 2026))!;
        Assert.Equal([2026, 2027, 2028, 2029, 2030], outlook.Years.Select(y => y.FiscalYear));
        Assert.Equal([0m, 2.5m, 2.5m, 2.5m, 2.5m], outlook.Years.Select(y => y.RevenuePercent));
        Assert.All(outlook.Funds, f => Assert.Equal(5, f.Years.Count));
        Assert.All(outlook.Funds, f => Assert.All(f.Years.Skip(1).Zip(f.Years), pair => Assert.Equal(pair.Second.EndingBalance, pair.First.BeginningBalance)));
        // The budget year matches the portal's own fund totals.
        PortalBudgetDto budget = (await portal.GetBudgetAsync("maple-ridge-oh", 2026))!;
        Assert.Equal(budget.TotalRevenues + budget.TotalTransfersIn, outlook.Years[0].Revenues);

        Assert.Null(await portal.GetOutlookAsync("maple-ridge-oh", 2025)); // budgeted before the plan
    }

    [Fact]
    public async Task Each_snapshot_keeps_one_row_per_fund_per_planned_year()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);

        List<PublishedBudgetSnapshot> snapshots = await db.PublishedBudgetSnapshots.Include(s => s.Funds).Include(s => s.PlanYears).ToListAsync();

        PublishedBudgetSnapshot fy2026 = snapshots.Single(s => s.FiscalYear == 2026);
        Assert.Equal(fy2026.Funds.Count * BudgetVersion.DefaultPlanYears, fy2026.PlanYears.Count);
        Assert.Equal(fy2026.Funds.Count, snapshots.Single(s => s.FiscalYear == 2025).PlanYears.Count); // the budget year only
    }
}
