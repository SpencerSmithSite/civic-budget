using Bunit.TestDoubles;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Budgets;

/// <summary>
/// The multi-year plan page: fund balances by year with an overspent year said in words, typed
/// years that can go back to the calculation, the assumptions form for the fiscal officer, and a
/// read-only view for everyone else.
/// </summary>
public class BudgetPlanPageTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly Guid LineId = Guid.CreateVersion7();
    private readonly FakePlans _plans = new();

    public BudgetPlanPageTests()
    {
        Services.AddSingleton<IBudgetPlanService>(_plans);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static BudgetPlanDto Plan(bool canEdit = true, int years = 3) => new(
        VersionId, 2027, "Original", BudgetStatus.Draft, years, true,
        [.. Enumerable.Range(1, years - 1).Select(o => new PlanRateDto(o, 2027 + o, 2m, 3m))],
        canEdit,
        [new PlanFundDto(Guid.CreateVersion7(), "2011", "Street Fund",
            [new PlanFundYearDto(2027, 10_000m, 100_000m, 0m, 105_000m, 0m, 5_000m, false, 0m),
             new PlanFundYearDto(2028, 5_000m, 102_000m, 0m, 108_150m, 0m, -1_150m, true, 1_150m),
             new PlanFundYearDto(2029, -1_150m, 104_040m, 0m, 111_394m, 0m, -8_504m, true, 8_504m)])],
        [new PlanLineDto(LineId, "2011", "Street Fund", Guid.CreateVersion7(), "620", "Streets", "5520", "Road Repair", "2011-620-5520",
            AccountType.Expenditure, canEdit, [105_000m, 108_150m, 250_000m], [false, false, true])]);

    private IRenderedComponent<BudgetPlan> RenderPage() => Render<BudgetPlan>(p => p.Add(x => x.VersionId, VersionId));

    [Fact]
    public void Every_year_shows_with_an_overspent_year_said_in_words()
    {
        _plans.Plan = Plan();

        IRenderedComponent<BudgetPlan> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("Multi-year plan", page.Markup));
        string balances = page.Find("table[aria-label='Projected ending balance by fund and year']").TextContent;
        Assert.Contains("FY2029", balances);
        Assert.Contains("Over by $1,150.00", balances);
        Assert.Contains("Fund 2011 spends more than it has in FY2028, FY2029", page.Markup);
    }

    [Fact]
    public void A_future_year_saves_and_a_typed_year_goes_back_to_the_calculation()
    {
        _plans.Plan = Plan();
        IRenderedComponent<BudgetPlan> page = RenderPage();
        page.WaitForAssertion(() => page.Find("table[aria-label='Budget lines by year']"));

        page.Find("input[aria-label='FY2028 plan for 2011-620-5520 Road Repair']").Change("110000");
        Assert.Equal((LineId, 1, (decimal?)110_000m), _plans.LastSet);

        page.Find("button[aria-label='Typed amount. Use the calculated FY2029 amount for 2011-620-5520 instead']").Click();
        Assert.Equal((LineId, 2, (decimal?)null), _plans.LastSet);
    }

    [Fact]
    public void The_fiscal_officer_changes_the_length_and_fills_every_year_at_once()
    {
        _plans.Plan = Plan();
        IRenderedComponent<BudgetPlan> page = RenderPage();
        page.WaitForAssertion(() => page.Find("#plan-years"));

        page.Find("#plan-years").Change("5");
        page.Find("#fill-revenue").Change("1.5");
        page.Find("#fill-expenditure").Change("4");
        page.FindAll("button").Single(b => b.TextContent == "Use for every year").Click();
        page.FindAll("button").Single(b => b.TextContent == "Save the plan").Click();

        SavePlanRequest saved = _plans.Saved!;
        Assert.Equal(5, saved.Years);
        Assert.Equal([1, 2, 3, 4], saved.Rates.Select(r => r.YearOffset));
        Assert.All(saved.Rates, r => Assert.Equal((1.5m, 4m), (r.RevenuePercent, r.ExpenditurePercent)));
    }

    [Fact]
    public void Someone_who_may_not_change_it_reads_the_plan()
    {
        _plans.Plan = Plan(canEdit: false);

        IRenderedComponent<BudgetPlan> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("The Administrator or Fiscal Officer sets these.", page.Markup));
        Assert.Empty(page.FindAll("#plan-years"));
        Assert.Empty(page.FindAll("input.cb-amount-input"));
        Assert.Contains("+3.0%", page.Find("table[aria-label='Change from the year before']").TextContent);
        Assert.Contains("typed", page.Find("table[aria-label='Budget lines by year']").TextContent);
    }

    private sealed class FakePlans : IBudgetPlanService
    {
        public BudgetPlanDto? Plan { get; set; }
        public SavePlanRequest? Saved { get; private set; }
        public (Guid LineId, int Offset, decimal? Amount)? LastSet { get; private set; }

        public Task<BudgetPlanDto?> GetAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Plan);

        public Task<Result> SavePlanAsync(SavePlanRequest request, CancellationToken ct = default)
        {
            Saved = request;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SetPlannedAmountAsync(Guid versionId, Guid lineId, int yearOffset, decimal? amount, CancellationToken ct = default)
        {
            LastSet = (lineId, yearOffset, amount);
            return Task.FromResult(Result.Success());
        }
    }
}
