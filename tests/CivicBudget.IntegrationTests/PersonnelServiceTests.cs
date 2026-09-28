using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Personnel;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Personnel;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Personnel budgeting against the seeded FY2027 positions: who may see and change them, how a change
/// reaches the department's lines, and how the year's settings reprice the open budget.
/// </summary>
[Collection(SqlServerTests.Name)]
public class PersonnelServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _police;
    private Guid _streets;
    private Guid _finance;
    private Guid _generalFund;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Personnel");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _police = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
        _streets = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
        _finance = (await scoped.Departments.SingleAsync(d => d.Code == "725")).Id;
        _generalFund = (await scoped.Funds.SingleAsync(f => f.Code == "1000")).Id;
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

    private static IPersonnelService Personnel(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IPersonnelService>();
    private static IPersonnelSettingsService Settings(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IPersonnelSettingsService>();

    private PositionDetails Clerk(PayrollRules rules) => new()
    {
        Title = "Evidence technician",
        EmployeeName = "Jesse Ward",
        Basis = PayBasis.Hourly,
        Rate = 22m,
        RetirementPlanId = rules.RetirementPlans.Single(p => p.Name == "OPERS").Id,
        Funds = [new FundShare(_generalFund, 100m)],
    };

    [Fact]
    public async Task The_police_lines_are_what_the_positions_cost()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelPageDto page = (await Personnel(scope).GetDepartmentAsync(_draft2027, _police))!;

        Assert.Equal(9, page.Positions.Count);
        Assert.Equal(1, page.Vacancies);
        Assert.NotNull(page.Rules);
        Assert.True(page.CanEdit);
        Assert.True(page.LinesMatchPositions);
        Assert.All(page.Positions, p => Assert.Null(p.Problem));
        Assert.Equal(["5110", "5120", "5210", "5220", "5230", "5240"], page.Lines.Select(l => l.AccountCode));
        Assert.Equal(page.Total, page.Lines.Sum(l => l.Amount));
        Assert.Equal(9, page.Lines.Single(l => l.AccountCode == "5110").PositionCount);
    }

    [Fact]
    public async Task Adding_a_position_raises_the_departments_lines_and_is_audited()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelPageDto before = (await Personnel(scope).GetDepartmentAsync(_draft2027, _police))!;
        PositionDetails clerk = Clerk(before.Rules!);
        PositionCost cost = PositionCostCalculator.Calculate(clerk, before.Rules!);

        Result<PersonnelSavedDto> saved = await Personnel(scope).AddPositionAsync(_draft2027, _police, clerk);

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        PersonnelPageDto after = (await Personnel(scope).GetDepartmentAsync(_draft2027, _police))!;
        Assert.Equal(10, after.Positions.Count);
        Assert.Equal(before.Total + cost.Total, after.Total);
        Assert.Equal(before.Lines.Single(l => l.AccountCode == "5110").Amount + 45_760m, after.Lines.Single(l => l.AccountCode == "5110").Amount);
        Assert.Equal(10, after.Lines.Single(l => l.AccountCode == "5110").PositionCount);

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Kind == AuditKind.Event && a.EntityId == _draft2027
            && a.Description!.StartsWith("Added position Jesse Ward, Evidence technician in Police")));
    }

    [Fact]
    public async Task A_position_that_does_not_price_comes_back_as_field_errors()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PayrollRules rules = (await Personnel(scope).GetDepartmentAsync(_draft2027, _police))!.Rules!;

        Result<PersonnelSavedDto> saved = await Personnel(scope).AddPositionAsync(_draft2027, _police, Clerk(rules) with { Title = "", Funds = [new FundShare(_generalFund, 60m)] });

        Assert.Contains(saved.Errors, e => e.PropertyName == nameof(PositionDetails.Title));
        Assert.Contains(saved.Errors, e => e.PropertyName == nameof(PositionDetails.Funds));
    }

    [Fact]
    public async Task Removing_a_position_lowers_the_lines()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelPageDto before = (await Personnel(scope).GetDepartmentAsync(_draft2027, _finance))!;
        PositionDto clerk = before.Positions.Single(p => p.Details.EmployeeName == "Casey Lin");

        Assert.True((await Personnel(scope).RemovePositionAsync(_draft2027, clerk.Id)).IsSuccess);

        PersonnelPageDto after = (await Personnel(scope).GetDepartmentAsync(_draft2027, _finance))!;
        Assert.Single(after.Positions);
        Assert.Equal(before.Total - clerk.Cost!.Total, after.Total);
        Assert.Equal(1, after.Lines.Single(l => l.AccountCode == "5110").PositionCount);
    }

    [Fact]
    public async Task A_calculated_amount_cannot_be_typed_over()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetLineDto salaries = (await entry.GetWorkspaceAsync(_draft2027))!.Lines.Single(l => l.AccountNumber == "1000-725-5110");

        Result typed = await entry.UpdateLineAmountAsync(_draft2027, salaries.Id, 1m);
        Result removed = await entry.RemoveLineAsync(_draft2027, salaries.Id);

        Assert.False(salaries.CanEditAmount);
        Assert.Equal("This line is calculated from 2 positions; change the positions instead.", typed.Errors.Single().Message);
        Assert.True(removed.IsFailure);
    }

    [Fact]
    public async Task Department_users_change_their_own_positions_until_they_submit()
    {
        await using AsyncServiceScope director = As(Roles.DepartmentHead, _streets);
        PersonnelPageDto streets = (await Personnel(director).GetDepartmentAsync(_draft2027, _streets))!;
        Assert.True(streets.CanEdit);
        Assert.Equal(["Streets & Service"], streets.Departments.Select(d => d.Name));
        Assert.Null(await Personnel(director).GetDepartmentAsync(_draft2027, _police));
        Assert.True((await Personnel(director).AddPositionAsync(_draft2027, _police, Clerk(streets.Rules!))).IsFailure);

        PositionDto seasonal = streets.Positions.Single(p => p.Details.IsVacant);
        Result<PersonnelSavedDto> changed = await Personnel(director).UpdatePositionAsync(_draft2027, seasonal.Id, seasonal.Details with { LastMonth = 9 });
        Assert.True(changed.IsSuccess, string.Join("; ", changed.Errors.Select(e => e.Message)));

        // Police has submitted: the chief can look, not change.
        await using AsyncServiceScope chief = As(Roles.DepartmentHead, _police);
        PersonnelPageDto police = (await Personnel(chief).GetDepartmentAsync(_draft2027, _police))!;
        Assert.False(police.CanEdit);
        Assert.True((await Personnel(chief).RemovePositionAsync(_draft2027, police.Positions[0].Id)).IsFailure);
    }

    [Fact]
    public async Task A_viewer_sees_positions_and_changes_none()
    {
        await using AsyncServiceScope viewer = As(Roles.Viewer);
        PersonnelPageDto police = (await Personnel(viewer).GetDepartmentAsync(_draft2027, _police))!;

        Assert.False(police.CanEdit);
        Assert.False(police.CanSetUpSettings);
        Assert.True((await Personnel(viewer).AddPositionAsync(_draft2027, _police, Clerk(police.Rules!))).IsFailure);
        Assert.False((await Settings(viewer).GetAsync(2027))!.CanEdit);
    }

    [Fact]
    public async Task Changing_the_years_settings_reprices_the_open_budget()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelSettingsForm form = (await Settings(scope).GetAsync(2027))!.Form!;
        decimal retirementBefore = (await Personnel(scope).GetDepartmentAsync(_draft2027, _finance))!.Lines.Single(l => l.AccountCode == "5210").Amount;
        form.RetirementPlans.Single(p => p.Name == "OPERS").EmployerRate = 15m;

        Result<PersonnelSettingsSavedDto> saved = await Settings(scope).SaveAsync(2027, form);

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        Assert.Equal(["FY2027 Original"], saved.Value.BudgetsRecalculated);
        decimal retirementAfter = (await Personnel(scope).GetDepartmentAsync(_draft2027, _finance))!.Lines.Single(l => l.AccountCode == "5210").Amount;
        decimal expected = retirementBefore / 14m * 15m;
        Assert.InRange(retirementAfter, expected - 0.02m, expected + 0.02m); // each position rounds to the cent

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == _draft2027 && a.Description!.StartsWith("FY2027 personnel settings changed")));
    }

    [Fact]
    public async Task Saving_settings_touches_only_the_lines_the_change_reaches()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelSettingsForm form = (await Settings(scope).GetAsync(2027))!.Form!;

        Result<PersonnelSettingsSavedDto> unchanged = await Settings(scope).SaveAsync(2027, form);
        form.WorkersCompRate = 2.5m;
        Result<PersonnelSettingsSavedDto> workersComp = await Settings(scope).SaveAsync(2027, form);

        Assert.Equal(0, unchanged.Value.LinesChanged);
        Assert.Equal(4, workersComp.Value.LinesChanged); // 5240 for Police, Finance, and Streets in two funds
    }

    [Fact]
    public async Task A_plan_someone_is_on_cannot_be_removed()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelSettingsPageDto page = (await Settings(scope).GetAsync(2027))!;
        InsurancePlanForm medical = page.Form!.InsurancePlans.Single(p => p.Name == "Medical (PPO)");
        page.Form.InsurancePlans.Remove(medical);

        Result<PersonnelSettingsSavedDto> saved = await Settings(scope).SaveAsync(2027, page.Form);

        Assert.True(page.PositionsUsing[medical.Id!.Value] > 0);
        Assert.Contains("cannot be priced", saved.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains("(Original): An insurance plan is not in this year's settings.", saved.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_year_starts_from_last_years_settings_or_the_defaults()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        Guid fy2028 = (await scope.ServiceProvider.GetRequiredService<Application.Setup.IFiscalYearService>().CreateAsync(new Application.Setup.CreateFiscalYearRequest(2028))).Value;
        Assert.NotEqual(Guid.Empty, fy2028);
        PersonnelSettingsPageDto blank = (await Settings(scope).GetAsync(2028))!;
        Assert.Null(blank.Form);
        Assert.True(blank.PriorYearIsSetUp);
        Assert.NotNull(blank.Suggested.Pay);
        Assert.NotNull(blank.Suggested.Medicare);

        SuggestedAccounts s = blank.Suggested;
        Assert.True((await Settings(scope).CreateAsync(2028, new CreatePersonnelSettingsRequest(true, s.Pay!.Value, s.Overtime, s.Retirement!.Value, s.Medicare!.Value, s.WorkersComp!.Value))).IsSuccess);
        Assert.True((await Settings(scope).CreateAsync(2028, new CreatePersonnelSettingsRequest(true, s.Pay.Value, s.Overtime, s.Retirement.Value, s.Medicare.Value, s.WorkersComp.Value))).IsFailure);

        PersonnelSettingsForm copied = (await Settings(scope).GetAsync(2028))!.Form!;
        Assert.Equal(2.1m, copied.WorkersCompRate);
        Assert.Contains(copied.Longevity, l => l.Name == "FOP Lodge 112 contract");

        await using AsyncServiceScope director = As(Roles.DepartmentHead, _streets);
        Assert.True((await Settings(director).SaveAsync(2028, copied)).IsFailure);
    }

    [Fact]
    public async Task An_amendment_carries_the_positions()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();
        Guid street = (await entry.GetWorkspaceAsync(_draft2027))!.FundBalances.Single(f => f.FundCode == "2011").FundId;
        Assert.True((await entry.SetBeginningBalanceAsync(_draft2027, street, 100_000m)).IsSuccess);
        Assert.True((await workflow.ProposeAsync(_draft2027, false)).IsSuccess);
        Assert.True((await workflow.AdoptAsync(_draft2027, "2026-50", false)).IsSuccess);

        Result<Guid> amendment = await workflow.CreateAmendmentAsync(_draft2027, "Mid-year contract settlement");

        Assert.True(amendment.IsSuccess);
        PersonnelPageDto police = (await Personnel(scope).GetDepartmentAsync(amendment.Value, _police))!;
        Assert.Equal(9, police.Positions.Count);
        Assert.True(police.CanEdit);
        Assert.True(police.LinesMatchPositions);
        Assert.False((await Personnel(scope).GetDepartmentAsync(_draft2027, _police))!.CanEdit); // the adopted original is fixed
    }
}
