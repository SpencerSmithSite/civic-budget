using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Budgets.Planning;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Tests.Budgets;

/// <summary>
/// The multi-year plan: each future year follows from the year before by that year's percentage
/// (revenues and expenditures separately), a typed amount replaces the calculation, and each fund's
/// ending balance becomes the next year's beginning balance.
/// </summary>
public class MultiYearPlanTests
{
    private sealed record Setup(BudgetVersion Version, Fund General, Department Police, BudgetLine Salaries, BudgetLine Tax);

    private static Setup Draft(decimal salaries = 100_000m, decimal tax = 150_000m, decimal beginning = 20_000m)
    {
        Fund general = TestData.GeneralFund();
        Department police = TestData.Police();
        BudgetVersion version = TestData.DraftVersion();
        BudgetLine salaryLine = version.AddLine(general, police, TestData.Salaries(), salaries);
        BudgetLine taxLine = version.AddLine(general, null, TestData.PropertyTax(), tax);
        version.SetBeginningBalance(general, beginning);
        return new Setup(version, general, police, salaryLine, taxLine);
    }

    private static IReadOnlyList<decimal> AmountsOf(MultiYearProjection plan, BudgetLine line) =>
        plan.Lines.Single(l => l.LineId == line.Id).Amounts;

    [Fact]
    public void A_new_budget_plans_five_years_with_no_change_until_percentages_are_set()
    {
        Setup s = Draft();

        MultiYearProjection plan = MultiYearPlanCalculator.Project(s.Version);

        Assert.Equal(BudgetVersion.DefaultPlanYears, plan.Years);
        Assert.Equal([100_000m, 100_000m, 100_000m, 100_000m, 100_000m], AmountsOf(plan, s.Salaries));
    }

    [Fact]
    public void Each_future_year_is_the_year_before_changed_by_its_own_percentage()
    {
        Setup s = Draft();
        s.Version.SetPlan(4, wholeDollars: false, [new PlanRate(1, 2m, 3m), new PlanRate(2, 2m, 3m), new PlanRate(3, 0m, -10m)]);

        MultiYearProjection plan = MultiYearPlanCalculator.Project(s.Version);

        // Expenditures: 100,000 then +3%, +3%, then -10%, each on the year before.
        Assert.Equal([100_000m, 103_000m, 106_090m, 95_481m], AmountsOf(plan, s.Salaries));
        // Revenues follow the revenue percentage.
        Assert.Equal([150_000m, 153_000m, 156_060m, 156_060m], AmountsOf(plan, s.Tax));
    }

    [Fact]
    public void Transfers_follow_their_side_of_the_budget()
    {
        Setup s = Draft();
        BudgetLine transferIn = s.Version.AddLine(s.General, null, TestData.TransferIn(), 1_000m);
        BudgetLine transferOut = s.Version.AddLine(s.General, null, TestData.TransferOut(), 1_000m);
        s.Version.SetPlan(2, false, [new PlanRate(1, 10m, 50m)]);

        MultiYearProjection plan = MultiYearPlanCalculator.Project(s.Version);

        Assert.Equal(1_100m, AmountsOf(plan, transferIn)[1]);
        Assert.Equal(1_500m, AmountsOf(plan, transferOut)[1]);
    }

    [Fact]
    public void Whole_dollar_rounding_applies_to_calculated_years()
    {
        Setup s = Draft(salaries: 1_234.56m);
        s.Version.SetPlan(2, wholeDollars: true, [new PlanRate(1, 0m, 3m)]);

        Assert.Equal(1_272m, AmountsOf(MultiYearPlanCalculator.Project(s.Version), s.Salaries)[1]); // 1,271.5968 rounds up
    }

    [Fact]
    public void A_typed_year_replaces_the_calculation_and_the_years_after_follow_from_it()
    {
        Setup s = Draft();
        s.Version.SetPlan(4, false, [new PlanRate(1, 0m, 10m), new PlanRate(2, 0m, 10m), new PlanRate(3, 0m, 10m)]);

        s.Version.SetPlannedAmount(s.Salaries.Id, 2, 200_000m);
        PlanLineProjection line = MultiYearPlanCalculator.Project(s.Version).Lines.Single(l => l.LineId == s.Salaries.Id);

        Assert.Equal([100_000m, 110_000m, 200_000m, 220_000m], line.Amounts);
        Assert.Equal([false, false, true, false], line.Typed);

        s.Version.SetPlannedAmount(s.Salaries.Id, 2, null); // back to the calculation
        Assert.Equal(121_000m, AmountsOf(MultiYearPlanCalculator.Project(s.Version), s.Salaries)[2]);
    }

    [Fact]
    public void A_change_to_the_budget_year_flows_into_every_calculated_year()
    {
        Setup s = Draft();
        s.Version.SetPlan(3, false, [new PlanRate(1, 0m, 10m), new PlanRate(2, 0m, 10m)]);

        s.Version.UpdateLineAmount(s.Salaries.Id, 50_000m);

        Assert.Equal([50_000m, 55_000m, 60_500m], AmountsOf(MultiYearPlanCalculator.Project(s.Version), s.Salaries));
    }

    [Fact]
    public void Each_funds_ending_balance_is_the_next_years_beginning_and_a_year_that_overspends_is_flagged()
    {
        // Resources 20,000 + 150,000; spending 100,000 growing 40% a year against revenue growing 0%.
        Setup s = Draft();
        s.Version.SetPlan(3, false, [new PlanRate(1, 0m, 40m), new PlanRate(2, 0m, 40m)]);

        PlanFundYear[] years = [.. MultiYearPlanCalculator.Project(s.Version).Funds.Where(f => f.FundId == s.General.Id).OrderBy(f => f.YearOffset)];

        Assert.Equal([20_000m, 70_000m, 80_000m], years.Select(y => y.Summary.BeginningBalance));
        Assert.Equal([70_000m, 80_000m, 34_000m], years.Select(y => y.Summary.ProjectedEndingBalance));
        Assert.All(years, y => Assert.True(y.Summary.IsWithinAppropriationLimit));

        s.Version.SetPlan(4, false, [new PlanRate(1, 0m, 40m), new PlanRate(2, 0m, 40m), new PlanRate(3, 0m, 40m)]);
        PlanFundYear fourth = MultiYearPlanCalculator.Project(s.Version).Funds.Single(f => f.FundId == s.General.Id && f.YearOffset == 3);
        Assert.False(fourth.Summary.IsWithinAppropriationLimit); // 274,400 spent against 184,000 available
        Assert.Equal(90_400m, fourth.Summary.AmountOverLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void A_plan_covers_one_to_ten_years(int years)
    {
        Setup s = Draft();

        Assert.Throws<DomainException>(() => s.Version.SetPlan(years, false, []));
    }

    [Fact]
    public void Percentages_must_fall_inside_the_plan_and_the_allowed_range()
    {
        Setup s = Draft();

        Assert.Throws<DomainException>(() => s.Version.SetPlan(3, false, [new PlanRate(3, 1m, 1m)]));    // year 3 is outside a 3-year plan
        Assert.Throws<DomainException>(() => s.Version.SetPlan(3, false, [new PlanRate(1, 1m, 1m), new PlanRate(1, 2m, 2m)]));
        Assert.Throws<DomainException>(() => s.Version.SetPlan(3, false, [new PlanRate(1, 101m, 0m)]));
        Assert.Throws<DomainException>(() => s.Version.SetPlannedAmount(s.Salaries.Id, 5, 1m)); // default plan is years 0 to 4
        Assert.Throws<DomainException>(() => s.Version.SetPlannedAmount(s.Salaries.Id, 1, -1m));
    }

    [Fact]
    public void Shortening_the_plan_drops_typed_years_it_no_longer_covers()
    {
        Setup s = Draft();
        s.Version.SetPlannedAmount(s.Salaries.Id, 4, 1m);
        s.Version.SetPlannedAmount(s.Salaries.Id, 1, 2m);

        s.Version.SetPlan(3, false, []);

        Assert.Equal([1], s.Salaries.PlannedAmounts.Select(p => p.YearOffset));
    }

    [Fact]
    public void An_adopted_budgets_plan_is_locked()
    {
        BudgetVersion adopted = TestData.AdoptedVersion();

        Assert.Throws<DomainException>(() => adopted.SetPlan(5, false, []));
    }

    [Fact]
    public void An_amendment_keeps_the_whole_plan()
    {
        Setup s = Draft();
        s.Version.SetPlan(6, true, [new PlanRate(1, 2m, 3m)]);
        s.Version.SetPlannedAmount(s.Salaries.Id, 3, 123_000m);
        s.Version.Propose();
        s.Version.Adopt("2027-1", "user", DateTimeOffset.UnixEpoch);

        BudgetVersion amendment = s.Version.CreateAmendment("Grant");

        Assert.Equal((6, true), (amendment.PlanYears, amendment.PlanInWholeDollars));
        Assert.Equal((2m, 3m), (amendment.PlanAssumptions.Single().RevenuePercent, amendment.PlanAssumptions.Single().ExpenditurePercent));
        Assert.Equal(123_000m, amendment.Lines.Single(l => l.AccountId == s.Salaries.AccountId).PlannedAmounts.Single(p => p.YearOffset == 3).Amount);
    }

    [Fact]
    public void Next_years_budget_keeps_the_plan_one_year_on()
    {
        Setup s = Draft();
        s.Version.SetPlan(5, false, [new PlanRate(1, 1m, 1m), new PlanRate(2, 2m, 2m), new PlanRate(3, 3m, 3m), new PlanRate(4, 4m, 4m)]);
        s.Version.SetPlannedAmount(s.Salaries.Id, 1, 111m);
        s.Version.SetPlannedAmount(s.Salaries.Id, 3, 333m);
        s.Version.Propose();
        s.Version.Adopt("2027-1", "user", DateTimeOffset.UnixEpoch);

        (BudgetVersion next, _) = BudgetVersion.CreateOriginalFrom(s.Version, new FiscalYear(TestData.GovernmentId, 2028, 1).Id,
            new Dictionary<Guid, Fund> { [s.General.Id] = s.General }, BudgetSeedOptions.CopyAsIs);

        Assert.Equal(5, next.PlanYears);
        // Old years 2, 3, 4 become 1, 2, 3; the new year 4 repeats the old last year.
        Assert.Equal([(1, 2m), (2, 3m), (3, 4m), (4, 4m)], next.PlanAssumptions.OrderBy(a => a.YearOffset).Select(a => (a.YearOffset, a.ExpenditurePercent)));
        // The old year 1 is now the budget year, so its typed amount goes; year 3 becomes year 2.
        Assert.Equal([(2, 333m)], next.Lines.Single(l => l.AccountId == s.Salaries.AccountId).PlannedAmounts.Select(p => (p.YearOffset, p.Amount)));
    }
}
