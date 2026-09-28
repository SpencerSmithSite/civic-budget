using CivicBudget.Domain.Common;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Domain.Tests.Personnel;

public class PositionCostCalculatorTests
{
    private readonly PersonnelTestData data = new();

    private PositionCost Price(PositionDetails position) => PositionCostCalculator.Calculate(position, data.Rules);

    private static decimal Part(PositionCost cost, CostKind kind) => cost.Parts.Where(p => p.Kind == kind).Sum(p => p.Amount);

    [Fact]
    public void Salaried_position_pays_retirement_medicare_and_workers_comp_on_its_salary()
    {
        PositionCost cost = Price(data.Clerk());

        Assert.Equal(60_000m, Part(cost, CostKind.BasePay));
        Assert.Equal(8_400m, Part(cost, CostKind.Retirement));   // OPERS 14%
        Assert.Equal(870m, Part(cost, CostKind.Medicare));       // 1.45%
        Assert.Equal(1_200m, Part(cost, CostKind.WorkersComp));  // BWC 2%
        Assert.Equal(70_470m, cost.Total);
        Assert.Equal(60_000m, cost.Pay);
        Assert.Equal(10_470m, cost.Benefits);
    }

    [Fact]
    public void Each_cost_lands_on_its_own_account()
    {
        PositionCost cost = Price(data.Clerk());

        Assert.Equal(PersonnelTestData.SalariesAccount, cost.Parts.Single(p => p.Kind == CostKind.BasePay).AccountId);
        Assert.Equal(PersonnelTestData.RetirementAccount, cost.Parts.Single(p => p.Kind == CostKind.Retirement).AccountId);
        Assert.Equal(PersonnelTestData.MedicareAccount, cost.Parts.Single(p => p.Kind == CostKind.Medicare).AccountId);
        Assert.Equal(PersonnelTestData.WorkersCompAccount, cost.Parts.Single(p => p.Kind == CostKind.WorkersComp).AccountId);
    }

    [Fact]
    public void Hourly_position_raise_counts_from_its_month()
    {
        // $25 an hour for 2,080 hours is $52,000; 3% from month 7 is $25.75, or $53,560 a year, for six months.
        PositionCost cost = Price(data.Clerk() with { Basis = PayBasis.Hourly, Rate = 25m, RaisePercent = 3m, RaiseMonth = 7 });

        Assert.Equal(52_780m, Part(cost, CostKind.BasePay));
        Assert.Equal(25m, cost.StartingRate);
        Assert.Equal(25.75m, cost.EndingRate);
    }

    [Fact]
    public void Vacancy_filled_in_month_4_is_paid_nine_months()
    {
        PositionCost cost = Price(data.Clerk() with { EmployeeName = null, Rate = 48_000m, FirstMonth = 4 });

        Assert.Equal(36_000m, Part(cost, CostKind.BasePay));
    }

    [Fact]
    public void Pay_scale_step_increase_takes_effect_in_its_month()
    {
        PositionCost cost = Price(data.Clerk() with { Rate = 0m, PayScaleId = data.Scale("Non-union"), Grade = "B", Step = 1, StepIncreaseMonth = 4 });

        // Three months at step 1 ($50,000) and nine at step 2 ($52,000).
        Assert.Equal(51_500m, Part(cost, CostKind.BasePay));
        Assert.Equal(52_000m, cost.EndingRate);
    }

    [Fact]
    public void Top_step_has_nowhere_to_go()
    {
        PositionCost cost = Price(data.Clerk() with { Rate = 0m, PayScaleId = data.Scale("Non-union"), Grade = "B", Step = 2, StepIncreaseMonth = 4 });

        Assert.Equal(52_000m, Part(cost, CostKind.BasePay));
    }

    [Theory]
    [InlineData("2023-03-01", 0)]       // 3 years: before the first step
    [InlineData("2020-01-01", 500)]     // 7 years
    [InlineData("2016-06-15", 750)]     // 10 years on January 1
    [InlineData("2001-01-01", 1_000)]   // 26 years
    public void Step_table_longevity_pays_the_highest_step_reached(string hired, int expected)
    {
        PositionCost cost = Price(data.Clerk() with { HireDate = DateOnly.Parse(hired, System.Globalization.CultureInfo.InvariantCulture), LongevityScheduleId = data.Longevity("Step table") });

        Assert.Equal(expected, Part(cost, CostKind.Longevity));
    }

    [Fact]
    public void Percent_longevity_is_a_share_of_base_pay()
    {
        PositionCost cost = Price(data.Clerk() with { HireDate = new DateOnly(2015, 1, 1), LongevityScheduleId = data.Longevity("Percent") });

        Assert.Equal(12, cost.YearsOfService);
        Assert.Equal(2_400m, Part(cost, CostKind.Longevity)); // 4% of $60,000
    }

    [Theory]
    [InlineData("2023-06-01", 0)]       // 4 years at year end
    [InlineData("2020-06-01", 700)]     // 7 years
    [InlineData("2000-06-01", 2_000)]   // 27 years, counted up to 20
    public void Per_year_longevity_counts_every_year_up_to_the_limit(string hired, int expected)
    {
        PositionCost cost = Price(data.Clerk() with { HireDate = DateOnly.Parse(hired, System.Globalization.CultureInfo.InvariantCulture), LongevityScheduleId = data.Longevity("Per year") });

        Assert.Equal(expected, Part(cost, CostKind.Longevity));
    }

    [Fact]
    public void Years_of_service_count_on_the_day_the_schedule_says()
    {
        var hired = new DateOnly(2017, 7, 1);

        Assert.Equal(9, PositionCostCalculator.CompletedYears(hired, new DateOnly(2027, 1, 1)));
        Assert.Equal(10, PositionCostCalculator.CompletedYears(hired, new DateOnly(2027, 12, 31)));
        Assert.Equal(0, PositionCostCalculator.CompletedYears(new DateOnly(2027, 3, 1), new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Longevity_is_pensionable_pay()
    {
        PositionCost cost = Price(data.Clerk() with { HireDate = new DateOnly(2016, 1, 1), LongevityScheduleId = data.Longevity("Step table") });

        Assert.Equal(Money.Round(60_750m * 0.14m), Part(cost, CostKind.Retirement));
        Assert.Equal(Money.Round(60_750m * 0.0145m), Part(cost, CostKind.Medicare));
    }

    [Fact]
    public void Extra_pay_follows_its_kind_and_whether_it_is_pensionable_or_taxable()
    {
        PositionDetails officer = data.Clerk() with
        {
            Basis = PayBasis.Hourly,
            Rate = 25m,
            ExtraPay =
            [
                new ExtraPayAmount(data.Extra("Overtime"), 100m),                          // 100 × $25 × 1.5, pensionable
                new ExtraPayAmount(data.Extra("Uniform allowance"), 800m),                 // taxable, not pensionable
                new ExtraPayAmount(data.Extra("Deferred compensation match (457)"), 2m),   // 2% of base, neither
            ],
        };

        PositionCost cost = Price(officer);

        Assert.Equal(3_750m, cost.Parts.Single(p => p.Label == "Overtime").Amount);
        Assert.Equal(800m, cost.Parts.Single(p => p.Label == "Uniform allowance").Amount);
        Assert.Equal(1_040m, cost.Parts.Single(p => p.Label == "Deferred compensation match (457)").Amount);
        Assert.Equal(PersonnelTestData.OvertimeAccount, cost.Parts.Single(p => p.Label == "Overtime").AccountId);
        Assert.Equal(PersonnelTestData.SalariesAccount, cost.Parts.Single(p => p.Label == "Uniform allowance").AccountId);
        Assert.Equal(7_805m, Part(cost, CostKind.Retirement));       // 14% of 52,000 + 3,750
        Assert.Equal(819.98m, Part(cost, CostKind.Medicare));        // 1.45% of 52,000 + 3,750 + 800
    }

    [Fact]
    public void Picking_up_the_employee_share_adds_the_employee_rate()
    {
        PositionCost cost = Price(data.Clerk() with { PicksUpEmployeeShare = true });

        Assert.Equal(8_400m + 6_000m, Part(cost, CostKind.Retirement));
        Assert.Equal(2, cost.Parts.Count(p => p.Kind == CostKind.Retirement));
    }

    [Fact]
    public void Police_pay_op_and_f()
    {
        PositionCost cost = Price(data.Clerk() with { RetirementPlanId = data.Plan("OP&F police") });

        Assert.Equal(11_700m, Part(cost, CostKind.Retirement)); // 19.5%
    }

    [Fact]
    public void Insurance_is_the_tier_premium_less_the_employee_share_for_each_month_paid()
    {
        PositionDetails covered = data.Clerk() with { Coverages = [new Coverage(data.Insurance("Medical"), CoverageTier.Family), new Coverage(data.Insurance("Life"), CoverageTier.EmployeeOnly)] };

        Assert.Equal(20_400m + 120m, Part(Price(covered), CostKind.Insurance));                      // $2,000 × 85% × 12, plus $10 × 12
        Assert.Equal(15_300m + 90m, Part(Price(covered with { FirstMonth = 4 }), CostKind.Insurance)); // nine months
    }

    [Fact]
    public void A_tier_the_plan_does_not_offer_is_a_problem()
    {
        PositionDetails covered = data.Clerk() with { Coverages = [new Coverage(data.Insurance("Life"), CoverageTier.Family)] };

        Assert.Contains(covered.Problems(data.Rules), p => p.Message == "Life does not offer that coverage.");
        Assert.Throws<DomainException>(() => Price(covered));
    }

    [Fact]
    public void Funds_share_each_cost_and_never_lose_a_cent()
    {
        PositionDetails split = data.Clerk() with
        {
            Rate = 10_000.01m,
            Funds = [new FundShare(PersonnelTestData.GeneralFund, 40m), new FundShare(PersonnelTestData.StreetFund, 60m)],
        };

        PositionCost cost = Price(split);

        // 40% of 10,000.01 is 4,000.004, so 4,000.00; the Street fund has the larger share and takes the rest.
        Assert.Equal(4_000.00m, Salaries(cost, PersonnelTestData.GeneralFund));
        Assert.Equal(6_000.01m, Salaries(cost, PersonnelTestData.StreetFund));
        Assert.Equal(cost.Total, cost.ByFund.Sum(c => c.Amount));
    }

    [Fact]
    public void A_split_comes_out_the_same_whatever_order_the_funds_are_listed_in()
    {
        PositionDetails split = data.Clerk() with
        {
            Rate = 10_000.01m,
            Funds = [new FundShare(PersonnelTestData.GeneralFund, 50m), new FundShare(PersonnelTestData.StreetFund, 50m)],
        };

        PositionCost listed = Price(split);
        PositionCost reversed = Price(split with { Funds = [.. split.Funds.Reverse()] });

        Assert.Equal(Salaries(listed, PersonnelTestData.GeneralFund), Salaries(reversed, PersonnelTestData.GeneralFund));
        Assert.Equal(Salaries(listed, PersonnelTestData.StreetFund), Salaries(reversed, PersonnelTestData.StreetFund));
        Assert.Equal(10_000.01m, Salaries(listed, PersonnelTestData.GeneralFund) + Salaries(listed, PersonnelTestData.StreetFund));
    }

    private static decimal Salaries(PositionCost cost, Guid fund) =>
        cost.ByFund.Single(c => c.FundId == fund && c.AccountId == PersonnelTestData.SalariesAccount).Amount;

    [Fact]
    public void Shares_must_add_to_100()
    {
        PositionDetails split = data.Clerk() with { Funds = [new FundShare(PersonnelTestData.GeneralFund, 60m), new FundShare(PersonnelTestData.StreetFund, 30m)] };

        Assert.Contains(split.Problems(data.Rules), p => p.Field == nameof(PositionDetails.Funds));
    }

    [Fact]
    public void Department_lines_sum_positions_and_count_them()
    {
        PositionDetails clerk = data.Clerk();
        PositionDetails splitWorker = data.Clerk() with
        {
            Title = "Laborer",
            Rate = 40_000m,
            Funds = [new FundShare(PersonnelTestData.GeneralFund, 25m), new FundShare(PersonnelTestData.StreetFund, 75m)],
        };

        IReadOnlyList<PersonnelLineCost> lines = PositionCostCalculator.SumByLine([clerk, splitWorker], data.Rules);

        PersonnelLineCost generalSalaries = lines.Single(l => l.FundId == PersonnelTestData.GeneralFund && l.AccountId == PersonnelTestData.SalariesAccount);
        PersonnelLineCost streetSalaries = lines.Single(l => l.FundId == PersonnelTestData.StreetFund && l.AccountId == PersonnelTestData.SalariesAccount);
        Assert.Equal(70_000m, generalSalaries.Amount);
        Assert.Equal(2, generalSalaries.PositionCount);
        Assert.Equal(30_000m, streetSalaries.Amount);
        Assert.Equal(1, streetSalaries.PositionCount);
    }

    [Fact]
    public void Missing_title_and_rate_are_problems_with_their_fields()
    {
        PositionDetails blank = data.Clerk() with { Title = " ", Rate = 0m };

        IReadOnlyList<(string Field, string Message)> problems = blank.Problems(data.Rules);

        Assert.Contains(problems, p => p.Field == nameof(PositionDetails.Title));
        Assert.Contains(problems, p => p.Field == nameof(PositionDetails.Rate));
    }
}
