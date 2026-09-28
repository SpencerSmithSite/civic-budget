using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Domain.Tests.Personnel;

public class BudgetVersionPersonnelTests
{
    private readonly PersonnelTestData data = new(workersCompRate: 0m);
    private readonly Department police = TestData.Police();
    private readonly Fund general = TestData.GeneralFund();
    private readonly Account salaries = Expense("5110", "Salaries");
    private readonly Account retirement = Expense("5210", "Retirement");
    private readonly Account medicare = Expense("5220", "Medicare");
    private readonly PersonnelSettings settings;

    public BudgetVersionPersonnelTests()
    {
        settings = PersonnelSettings.CreateDefault(TestData.GovernmentId, 2027, salaries.Id, null, retirement.Id, medicare.Id, medicare.Id);
    }

    private PayrollRules Rules => settings.ToRules(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

    private PersonnelChart Chart => new(police, new Dictionary<Guid, Fund> { [general.Id] = general },
        new[] { salaries, retirement, medicare }.ToDictionary(a => a.Id));

    private PositionDetails Officer(decimal salary = 60_000m) => new()
    {
        Title = "Patrol officer",
        EmployeeName = "Casey Lin",
        Rate = salary,
        RetirementPlanId = Rules.RetirementPlans.Single(p => p.Name == "OPERS").Id,
        Funds = [new FundShare(general.Id, 100m)],
    };

    private static Account Expense(string code, string name) =>
        new(TestData.GovernmentId, code, name, AccountType.Expenditure, ReportingCategory.PersonalServices);

    private static BudgetLine Line(BudgetVersion version, Account account) => version.Lines.Single(l => l.AccountId == account.Id);

    [Fact]
    public void Adding_a_position_creates_the_departments_personnel_lines()
    {
        BudgetVersion version = TestData.DraftVersion();

        (_, IReadOnlyList<PersonnelLineChange> changes) = version.AddPosition(Chart, Officer(), Rules);

        Assert.Equal(60_000m, Line(version, salaries).Amount);
        Assert.Equal(8_400m, Line(version, retirement).Amount);
        Assert.Equal(870m, Line(version, medicare).Amount);
        Assert.All(version.Lines, l => Assert.Equal(1, l.PositionCount));
        Assert.All(changes, c => Assert.Null(c.Before));
    }

    [Fact]
    public void A_typed_line_becomes_calculated_when_a_position_costs_into_it()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.AddLine(general, police, salaries, 55_000m);

        IReadOnlyList<PersonnelLineChange> changes = version.AddPosition(Chart, Officer(), Rules).Changes;

        Assert.True(Line(version, salaries).IsFromPersonnel);
        Assert.Contains(changes, c => c.AccountId == salaries.Id && c.Before == 55_000m && c.After == 60_000m);
    }

    [Fact]
    public void Two_positions_add_up_on_one_line()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.AddPosition(Chart, Officer(), Rules);
        version.AddPosition(Chart, Officer(50_000m) with { EmployeeName = null }, Rules);

        Assert.Equal(110_000m, Line(version, salaries).Amount);
        Assert.Equal(2, Line(version, salaries).PositionCount);
    }

    [Fact]
    public void A_calculated_amount_cannot_be_typed_over_or_removed()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.AddPosition(Chart, Officer(), Rules);
        Guid lineId = Line(version, salaries).Id;

        DomainException typed = Assert.Throws<DomainException>(() => version.UpdateLineAmount(lineId, 1m));
        Assert.Equal("This line is calculated from 1 position; change the positions instead.", typed.Message);
        Assert.Throws<DomainException>(() => version.RemoveLine(lineId));
    }

    [Fact]
    public void Updating_a_position_recalculates_its_lines()
    {
        BudgetVersion version = TestData.DraftVersion();
        Position position = version.AddPosition(Chart, Officer(), Rules).Position;

        version.UpdatePosition(position.Id, Chart, Officer(70_000m), Rules);

        Assert.Equal(70_000m, Line(version, salaries).Amount);
        Assert.Equal(9_800m, Line(version, retirement).Amount);
    }

    [Fact]
    public void Removing_the_last_position_returns_its_lines_to_typed_zeros()
    {
        BudgetVersion version = TestData.DraftVersion();
        Position position = version.AddPosition(Chart, Officer(), Rules).Position;

        version.RemovePosition(position.Id, Chart, Rules);

        Assert.Empty(version.Positions);
        Assert.All(version.Lines, l =>
        {
            Assert.False(l.IsFromPersonnel);
            Assert.Equal(0m, l.Amount);
        });
        version.UpdateLineAmount(Line(version, salaries).Id, 1_000m);
    }

    [Fact]
    public void Changed_settings_reach_the_lines_when_applied()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.AddPosition(Chart, Officer(), Rules);
        Guid opers = Rules.RetirementPlans.Single(p => p.Name == "OPERS").Id;
        settings.SaveRetirementPlan(opers, "OPERS", 15m, 10m, retirement.Id);

        version.ApplyPersonnel(Chart, Rules);

        Assert.Equal(9_000m, Line(version, retirement).Amount);
    }

    [Fact]
    public void An_adopted_budget_takes_no_positions()
    {
        BudgetVersion version = TestData.AdoptedVersion();

        Assert.Throws<DomainException>(() => version.AddPosition(Chart, Officer(), Rules));
    }

    [Fact]
    public void A_position_that_does_not_price_is_refused()
    {
        BudgetVersion version = TestData.DraftVersion();

        DomainException ex = Assert.Throws<DomainException>(() => version.AddPosition(Chart, Officer() with { Funds = [] }, Rules));
        Assert.Equal("Choose the fund that pays for the position.", ex.Message);
        Assert.Empty(version.Positions);
    }

    [Fact]
    public void Costs_cannot_land_on_a_revenue_account()
    {
        Account fines = new(TestData.GovernmentId, "4310", "Fines", AccountType.Revenue, ReportingCategory.FinesAndForfeitures);
        settings.SetBasics(2080m, fines.Id, 1.45m, medicare.Id, 0m, medicare.Id);
        PersonnelChart chart = Chart with { Accounts = new[] { fines, retirement, medicare }.ToDictionary(a => a.Id) };

        Assert.Throws<DomainException>(() => TestData.DraftVersion().AddPosition(chart, Officer(), Rules));
    }

    [Fact]
    public void An_amendment_copies_positions_and_calculated_lines()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.AddPosition(Chart, Officer(), Rules);
        version.Propose();
        version.Adopt("2026-40", "user-fd", DateTimeOffset.UtcNow);

        BudgetVersion amendment = version.CreateAmendment("Mid-year raise");

        Position copy = Assert.Single(amendment.Positions);
        Assert.NotEqual(version.Positions.Single().Id, copy.Id);
        Assert.Equal(amendment.Id, copy.BudgetVersionId);
        Assert.Equal(1, Line(amendment, salaries).PositionCount);
        Assert.Equal(Officer().Funds, copy.ToDetails().Funds);
    }

    [Fact]
    public void Next_year_starts_each_position_at_the_rate_it_ended_the_year()
    {
        BudgetVersion prior = TestData.DraftVersion();
        prior.AddPosition(Chart, Officer() with { RaisePercent = 3m, RaiseMonth = 7, FirstMonth = 4 }, Rules);
        (PersonnelSettings nextSettings, IReadOnlyDictionary<Guid, Guid> ids) = settings.CopyTo(2028);
        BudgetVersion next = TestData.DraftVersion();

        int carried = next.CarryForwardPositions(prior, Rules, ids, new HashSet<Guid> { police.Id });

        PositionDetails details = next.Positions.Single().ToDetails();
        Assert.Equal(1, carried);
        Assert.Equal(61_800m, details.Rate);
        Assert.Equal(0m, details.RaisePercent);
        Assert.Equal(1, details.FirstMonth);
        Assert.Equal(ids[Rules.RetirementPlans.Single(p => p.Name == "OPERS").Id], details.RetirementPlanId);

        next.ApplyPersonnel(Chart, nextSettings.ToRules(new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31)));
        Assert.Equal(61_800m, Line(next, salaries).Amount);
    }

    [Fact]
    public void Positions_in_retired_departments_stay_behind()
    {
        BudgetVersion prior = TestData.DraftVersion();
        prior.AddPosition(Chart, Officer(), Rules);

        int carried = TestData.DraftVersion().CarryForwardPositions(prior, Rules, settings.CopyTo(2028).NewIds, new HashSet<Guid>());

        Assert.Equal(0, carried);
    }
}
