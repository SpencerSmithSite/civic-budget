using CivicBudget.Application.Erp;
using CivicBudget.Application.Personnel;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Tests.Personnel;

/// <summary>
/// Matching the ERP's payroll to a budget's positions: what the ERP owns is refreshed, what the budget
/// planned is kept, new hires fill vacancies, and anything unmatched refuses the whole sync.
/// </summary>
public class EmployeeMatcherTests
{
    private static readonly Guid Police = Guid.CreateVersion7();
    private static readonly Guid Finance = Guid.CreateVersion7();
    private static readonly Guid General = Guid.CreateVersion7();
    private static readonly Guid Street = Guid.CreateVersion7();
    private static readonly Guid Account = Guid.CreateVersion7();

    private readonly PayrollRules _rules;
    private readonly EmployeeChart _chart = new(
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["110"] = Police, ["725"] = Finance },
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["1000"] = General, ["2011"] = Street });

    public EmployeeMatcherTests()
    {
        var settings = PersonnelSettings.CreateDefault(Guid.CreateVersion7(), 2027, Account, null, Account, Account, Account);
        settings.SaveInsurancePlan(null, "Medical", 650m, 1_300m, 1_760m, 15m, Account);
        settings.SaveLongevitySchedule(null, "FOP", LongevityMethod.FlatAmount, ServiceCountedOn.FirstDayOfYear, null, null, [new(5, 600m)]);
        settings.SavePayScale(null, "FOP", PayBasis.Hourly, [new("PO", 1, 25.10m), new("PO", 2, 26.60m)]);
        _rules = settings.ToRules(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
    }

    private Guid Opers => _rules.RetirementPlans.Single(p => p.Name == "OPERS").Id;

    private static ErpEmployee Clerk(decimal rate = 23.10m, string id = "E1033", string department = "725") =>
        new(id, "Casey Lin", "Payroll clerk", department, PayBasis.Hourly, rate, 1_560m, new DateOnly(2019, 6, 10), null, null, "OPERS", false,
            [new ErpFundShare("1000", 100m)], []);

    private PositionDetails Budgeted(ErpEmployee e) => new()
    {
        Title = e.Title,
        EmployeeName = e.Name,
        EmployeeId = e.EmployeeId,
        HireDate = e.HireDate,
        Basis = e.Basis,
        Rate = e.Rate,
        AnnualHours = e.AnnualHours ?? 2080m,
        RaisePercent = 3m,
        RaiseMonth = 7,
        RetirementPlanId = Opers,
        Funds = [new FundShare(General, 100m)],
    };

    private SyncPlan Plan(IReadOnlyList<ErpEmployee> payroll, params ExistingPosition[] positions) =>
        EmployeeMatcher.Plan(new ErpEmployees(new DateOnly(2026, 9, 27), payroll), _chart, _rules, positions);

    [Fact]
    public void An_employee_who_matches_the_budget_is_unchanged()
    {
        ExistingPosition clerk = new(Guid.CreateVersion7(), Finance, Budgeted(Clerk()));

        SyncStep step = Plan([Clerk()], clerk).Steps.Single();

        Assert.Equal(SyncAction.Unchanged, step.Action);
        Assert.Empty(step.Changes);
    }

    [Fact]
    public void A_raise_in_the_erp_updates_the_rate_and_keeps_the_budgets_planned_raise()
    {
        ExistingPosition clerk = new(Guid.CreateVersion7(), Finance, Budgeted(Clerk()));

        SyncStep step = Plan([Clerk(rate: 23.60m)], clerk).Steps.Single();

        Assert.Equal(SyncAction.Update, step.Action);
        Assert.Equal(clerk.Id, step.PositionId);
        Assert.Equal(["Pay: $23.10 an hour × 1,560 hours → $23.60 an hour × 1,560 hours"], step.Changes);
        Assert.Equal((23.60m, 3m, 7), (step.Details.Rate, step.Details.RaisePercent, step.Details.RaiseMonth));
    }

    [Fact]
    public void A_new_hire_fills_the_vacancy_with_the_same_title()
    {
        PositionDetails vacancy = new()
        {
            Title = "Patrol officer",
            PayScaleId = _rules.PayScales[0].Id,
            Grade = "PO",
            Step = 1,
            FirstMonth = 4,
            LongevityScheduleId = _rules.Longevity[0].Id,
            RetirementPlanId = Opers,
            Funds = [new FundShare(General, 100m)],
        };
        ExistingPosition open = new(Guid.CreateVersion7(), Police, vacancy);
        ErpEmployee hire = new("E1047", "Jamie Ortiz", "patrol officer", "110", PayBasis.Hourly, 0m, null, new DateOnly(2026, 9, 8), "PO", 1, "OPERS", false,
            [new ErpFundShare("1000", 100m)], [new ErpBenefit("Medical", CoverageTier.Family)]);

        SyncStep step = Plan([hire], open).Steps.Single();

        Assert.Equal(SyncAction.Fill, step.Action);
        Assert.Equal(open.Id, step.PositionId);
        Assert.Equal(("Jamie Ortiz", "E1047"), (step.Details.EmployeeName, step.Details.EmployeeId));
        Assert.Equal(1, step.Details.FirstMonth);                           // already on the payroll when the year starts
        Assert.Equal(_rules.Longevity[0].Id, step.Details.LongevityScheduleId); // the budget's choice is kept
        Assert.Single(step.Details.Coverages);
    }

    [Fact]
    public void A_hire_during_the_year_is_paid_from_the_month_they_start()
    {
        ErpEmployee hire = Clerk(id: "E2000") with { HireDate = new DateOnly(2027, 4, 12) };

        SyncStep step = Plan([hire]).Steps.Single();

        Assert.Equal(SyncAction.Add, step.Action);
        Assert.Equal((4, 12), (step.Details.FirstMonth, step.Details.LastMonth));
    }

    [Fact]
    public void A_new_position_copies_the_budgets_choices_from_a_colleague_with_the_same_title()
    {
        PositionDetails colleague = Budgeted(Clerk()) with { LongevityScheduleId = _rules.Longevity[0].Id };
        ExistingPosition existing = new(Guid.CreateVersion7(), Finance, colleague);
        ErpEmployee second = Clerk(id: "E2001") with { Name = "Robin Hale" };

        SyncStep added = Plan([Clerk(), second], existing).Steps.Single(s => s.Action == SyncAction.Add);

        Assert.Equal((3m, _rules.Longevity[0].Id), (added.Details.RaisePercent, added.Details.LongevityScheduleId));
        Assert.Equal("Robin Hale", added.Details.EmployeeName);
    }

    [Fact]
    public void Someone_no_longer_on_the_payroll_leaves_the_position_vacant()
    {
        ExistingPosition clerk = new(Guid.CreateVersion7(), Finance, Budgeted(Clerk()));

        SyncStep step = Plan([], clerk).Steps.Single();

        Assert.Equal(SyncAction.Vacate, step.Action);
        Assert.True(step.Details.IsVacant);
        Assert.Null(step.Details.EmployeeId);
        Assert.Equal(23.10m, step.Details.Rate);                           // the position stays budgeted at its rate
    }

    [Fact]
    public void A_transfer_leaves_the_old_position_vacant_and_places_the_employee_in_the_new_department()
    {
        ExistingPosition clerk = new(Guid.CreateVersion7(), Finance, Budgeted(Clerk()));

        SyncPlan plan = Plan([Clerk(department: "110")], clerk);

        Assert.Equal(1, plan.Count(SyncAction.Add));
        Assert.Equal(Police, plan.Steps.Single(s => s.Action == SyncAction.Add).DepartmentId);
        Assert.Contains("moved to another department", plan.Steps.Single(s => s.Action == SyncAction.Vacate).Changes.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Positions_entered_here_without_an_employee_number_are_left_alone()
    {
        ExistingPosition planned = new(Guid.CreateVersion7(), Finance, Budgeted(Clerk()) with { EmployeeId = null, EmployeeName = "Temp", Title = "Intern" });

        Assert.Empty(Plan([], planned).Steps);
    }

    [Fact]
    public void Anything_that_cannot_be_matched_is_an_error_naming_the_employee()
    {
        ErpEmployee bad = Clerk() with
        {
            DepartmentCode = "999",
            Retirement = "STRS",
            Funds = [new ErpFundShare("7000", 100m)],
            Benefits = [new ErpBenefit("Vision", CoverageTier.Family)],
        };
        ErpEmployee offScale = Clerk(id: "E3") with { Grade = "SGT", Step = 9 };

        SyncPlan plan = Plan([bad, offScale]);

        Assert.Empty(plan.Steps);
        string first = plan.Errors.Single(e => e.StartsWith("Casey Lin, Payroll clerk (E1033)", StringComparison.Ordinal));
        Assert.Contains("no active department has the code 999", first, StringComparison.Ordinal);
        Assert.Contains("no retirement system named \"STRS\"", first, StringComparison.Ordinal);
        Assert.Contains("no active fund has the code 7000", first, StringComparison.Ordinal);
        Assert.Contains("no insurance plan named \"Vision\"", first, StringComparison.Ordinal);
        Assert.Contains(plan.Errors, e => e.Contains("grade SGT step 9 is not on any pay scale", StringComparison.Ordinal));
    }

    [Fact]
    public void A_split_across_funds_comes_through_and_is_described_by_fund_code()
    {
        ExistingPosition clerk = new(Guid.CreateVersion7(), Finance, Budgeted(Clerk()));

        SyncStep step = Plan([Clerk() with { Funds = [new ErpFundShare("1000", 25m), new ErpFundShare("2011", 75m)] }], clerk).Steps.Single();

        Assert.Equal(["Funds: 1000 100% → 1000 25%, 2011 75%"], step.Changes);
    }
}
