using System.Security.Claims;
using System.Text;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Personnel;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Personnel;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Bringing the ERP's employees into a budget, from the simulated ERP's payroll and from a file, and
/// the personnel reports built on the result.
/// </summary>
[Collection(SqlServerTests.Name)]
public class PersonnelSyncServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;
    private Guid _draft2027;
    private Guid _adopted2026;
    private Guid _pineDraft;
    private Guid _police;
    private Guid _streets;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_PersonnelSync");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
        _pineDraft = (await db.BudgetVersions.IgnoreQueryFilters().SingleAsync(v => v.GovernmentId == _pineHollow && v.Status == BudgetStatus.Draft)).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _adopted2026 = (await scoped.BudgetVersions.Where(v => v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null)
            .OrderByDescending(v => v.VersionNumber).FirstAsync(v => scoped.FiscalYears.Any(f => f.Id == v.FiscalYearId && f.Year == 2026))).Id;
        _police = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
        _streets = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope As(string role, Guid? government = null, params Guid[] departments)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, (government ?? _mapleRidge).ToString()));
        foreach (Guid department in departments)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, department.ToString()));
        }

        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    private static IPersonnelSyncService Sync(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IPersonnelSyncService>();

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\n", lines)));

    [Fact]
    public async Task The_preview_shows_the_hire_the_raise_and_the_retirement_before_anything_is_written()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);

        Result<PersonnelSyncPreviewDto> result = await Sync(scope).PreviewFromErpAsync(_draft2027);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        PersonnelSyncPreviewDto preview = result.Value;
        Assert.Empty(preview.Errors);
        Assert.Equal((14, 0, 1, 1, 1, 12), (preview.Employees, preview.Count(SyncAction.Add), preview.Count(SyncAction.Fill),
            preview.Count(SyncAction.Update), preview.Count(SyncAction.Vacate), preview.Count(SyncAction.Unchanged)));
        Assert.Equal("Jamie Ortiz, Patrol officer (E1047)", preview.Steps.Single(s => s.Action == SyncAction.Fill).Who);
        Assert.Equal(["Pay: $23.10 an hour × 1,560 hours → $23.60 an hour × 1,560 hours"], preview.Steps.Single(s => s.Action == SyncAction.Update).Changes);
        Assert.StartsWith("Rowan Ruiz", preview.Steps.Single(s => s.Action == SyncAction.Vacate).Who, StringComparison.Ordinal);
        SyncLineChangeDto policePay = preview.Lines.Single(l => l.AccountNumber == "1000-110-5110");
        Assert.True(policePay.After > policePay.Before);                       // the vacancy is now paid from January, not April

        // Nothing was written: the budget still has the vacancy.
        await using AsyncServiceScope check = As(Roles.FinanceDirector);
        PersonnelPageDto police = (await check.ServiceProvider.GetRequiredService<IPersonnelService>().GetDepartmentAsync(_draft2027, _police))!;
        Assert.Equal(1, police.Vacancies);
    }

    [Fact]
    public async Task Applying_updates_the_positions_logs_the_sync_and_a_second_run_has_nothing_to_do()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        PersonnelSyncPreviewDto preview = (await Sync(scope).PreviewFromErpAsync(_draft2027)).Value;

        Result<PersonnelSyncDto> committed = await Sync(scope).CommitFromErpAsync(_draft2027);

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal((0, 1, 1, 1, preview.Lines.Count), (committed.Value.Added, committed.Value.Filled, committed.Value.Updated, committed.Value.Vacated, committed.Value.LinesChanged));

        IPersonnelService personnel = scope.ServiceProvider.GetRequiredService<IPersonnelService>();
        PersonnelPageDto police = (await personnel.GetDepartmentAsync(_draft2027, _police))!;
        Assert.Equal(0, police.Vacancies);
        Assert.Contains(police.Positions, p => p.Details.EmployeeId == "E1047");
        PersonnelPageDto streets = (await personnel.GetDepartmentAsync(_draft2027, _streets))!;
        Assert.Equal(2, streets.Vacancies);                                     // the seasonal opening, and Rowan Ruiz's position
        Assert.Equal(preview.Lines.Single(l => l.AccountNumber == "1000-110-5110").After, police.Lines.Single(l => l.AccountCode == "5110").Amount);

        Assert.Single((await Sync(scope).StatusAsync()).History);
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Kind == AuditKind.Event && a.EntityId == _draft2027
            && a.Description!.StartsWith("Positions brought in from ERP (simulated): 0 added, 1 filled, 1 updated, 1 left vacant")));

        Result<PersonnelSyncDto> again = await Sync(scope).CommitFromErpAsync(_draft2027);
        Assert.Equal("Every employee already matches the budget; there is nothing to bring in.", again.Errors.Single().Message);
    }

    [Fact]
    public async Task An_employee_who_cannot_be_matched_refuses_the_whole_sync()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        MemoryStream File() => Csv(
            "Employee ID,Name,Title,Department,Pay Type,Rate,Retirement,Funds,Benefits",
            "E1033,Casey Lin,Payroll and accounts payable clerk,725,Hourly,23.60,OPERS,1000,",
            "E9001,Morgan Fields,Clerk,725,Hourly,19,OPERS,1000,Vision: Family");

        PersonnelSyncPreviewDto preview = (await Sync(scope).PreviewFileAsync(_draft2027, "payroll.csv", File())).Value;
        Result<PersonnelSyncDto> committed = await Sync(scope).CommitFileAsync(_draft2027, "payroll.csv", File());

        Assert.False(preview.CanCommit);
        Assert.Contains("no insurance plan named \"Vision\"", preview.Errors.Single(), StringComparison.Ordinal);
        Assert.True(committed.IsFailure);
    }

    [Fact]
    public async Task Only_the_fiscal_authority_brings_employees_in_and_only_into_a_budget_being_prepared()
    {
        await using AsyncServiceScope chief = As(Roles.DepartmentHead, departments: _police);
        await using AsyncServiceScope viewer = As(Roles.Viewer);
        await using AsyncServiceScope officer = As(Roles.FinanceDirector);

        Assert.True((await Sync(chief).PreviewFromErpAsync(_draft2027)).IsFailure);
        Assert.True((await Sync(viewer).CommitFromErpAsync(_draft2027)).IsFailure);
        Assert.Contains("adopted", (await Sync(officer).PreviewFromErpAsync(_adopted2026)).Errors.Single().Message, StringComparison.Ordinal);
        Assert.DoesNotContain((await Sync(officer).StatusAsync()).Versions, v => v.Id == _adopted2026);
    }

    [Fact]
    public async Task A_government_without_personnel_sets_up_the_year_then_brings_its_payroll_in_from_a_file()
    {
        await using AsyncServiceScope admin = As(Roles.Admin, _pineHollow);
        IPersonnelSettingsService settings = admin.ServiceProvider.GetRequiredService<IPersonnelSettingsService>();
        Assert.Contains("no personnel settings", (await Sync(admin).PreviewFromErpAsync(_pineDraft)).Errors.Single().Message, StringComparison.Ordinal);

        SuggestedAccounts s = (await settings.GetAsync(2027))!.Suggested;
        Assert.True((await settings.CreateAsync(2027, new CreatePersonnelSettingsRequest(false, s.Pay!.Value, s.Overtime, s.Retirement!.Value, s.Medicare!.Value, s.WorkersComp!.Value))).IsSuccess);

        Result<PersonnelSyncDto> committed = await Sync(admin).CommitFileAsync(_pineDraft, "payroll.csv", Csv(
            "Employee ID,Name,Title,Department,Pay Type,Rate,Annual Hours,Hire Date,Retirement,Funds",
            "T204,Sidney Albright,Fiscal Officer,710,Salary,\"16,500.00\",,2017-04-01,OPERS,1000",
            "T210,Kelly Brandt,Road superintendent (part-time),610,Hourly,24.50,1040,2012-05-07,OPERS,2031"));

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal(2, committed.Value.Added);
        BudgetWorkspaceDto workspace = (await admin.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_pineDraft))!;
        BudgetLineDto roadPay = workspace.Lines.Single(l => l.FundCode == "2031" && l.AccountCode == "5110");
        Assert.Equal((25_480m, 1), (roadPay.Amount, roadPay.PositionCount!.Value));           // 24.50 × 1,040

        // The simulated ERP's API knows the whole township payroll; the two already here are unchanged.
        PersonnelSyncPreviewDto fromApi = (await Sync(admin).PreviewFromErpAsync(_pineDraft)).Value;
        Assert.Equal((4, 2), (fromApi.Count(SyncAction.Add), fromApi.Count(SyncAction.Unchanged)));
    }

    [Fact]
    public async Task The_personnel_reports_add_up_to_the_personnel_lines()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IPersonnelReportService reports = scope.ServiceProvider.GetRequiredService<IPersonnelReportService>();
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;
        List<BudgetLineDto> personnelLines = workspace.Lines.Where(l => l.PositionCount is not null).ToList();

        PositionRosterDto roster = (await reports.RosterAsync(_draft2027))!;
        PersonnelCostDto cost = (await reports.CostByFundAsync(_draft2027))!;
        BenefitsSummaryDto benefits = (await reports.BenefitsAsync(_draft2027))!;

        Assert.Equal((16, 2), (roster.Positions, roster.Vacant));
        Assert.Equal(personnelLines.Sum(l => l.Amount), roster.Total);
        Assert.Equal(personnelLines.Sum(l => l.Amount), cost.Total.Total);
        Assert.Equal(personnelLines.Where(l => l.FundCode == "2011").Sum(l => l.Amount), cost.Funds.Single(f => f.FundCode == "2011").Subtotal.Total);
        Assert.Equal(roster.Benefits, benefits.Total);
        Assert.Contains(benefits.Retirement, r => r.System == "OP&F police" && r.Members == 8 && r.PickedUp > 0m);
    }

    [Fact]
    public async Task A_department_user_gets_their_roster_and_none_of_the_whole_payroll_reports()
    {
        await using AsyncServiceScope director = As(Roles.DepartmentHead, departments: _streets);
        IPersonnelReportService reports = director.ServiceProvider.GetRequiredService<IPersonnelReportService>();

        PositionRosterDto roster = (await reports.RosterAsync(_draft2027))!;

        Assert.Equal(["620"], roster.Departments.Select(d => d.DepartmentCode));
        Assert.Null(await reports.CostByFundAsync(_draft2027));
        Assert.Null(await reports.BenefitsAsync(_draft2027));
    }
}
