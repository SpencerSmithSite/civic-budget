using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Budget entry against the seeded Maple Ridge data: what each role sees and may change, and the
/// fund balance figures the screens rely on.
/// </summary>
[Collection(SqlServerTests.Name)]
public class BudgetEntryServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _adopted2025;
    private Guid _streetsDept;
    private Guid _policeDept;

    public async Task InitializeAsync()
    {
        // One database per test: tests here add lines or users and others assert exact counts, so a
        // shared database made the result depend on the order xUnit happened to run them in.
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_BudgetEntry");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _adopted2025 = (await scoped.BudgetVersions
            .Join(scoped.FiscalYears, v => v.FiscalYearId, fy => fy.Id, (v, fy) => new { v, fy })
            .Where(x => x.fy.Year == 2025).Select(x => x.v).SingleAsync()).Id;
        _streetsDept = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
        _policeDept = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
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

    [Fact]
    public async Task Finance_director_sees_every_line_and_the_over_limit_street_fund()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;

        Assert.Equal(95, workspace.Lines.Count);
        Assert.All(workspace.Lines, l => Assert.True(l.CanEdit));
        Assert.True(workspace.IsEditable);
        Assert.True(workspace.CanAddLines);
        Assert.True(workspace.AnyFundBlocksWorkflow);
        FundBalanceDto street = Assert.Single(workspace.FundBalances, f => f.Limit.BlocksWorkflow);
        Assert.Equal("2011", street.FundCode);
        Assert.True(street.CanEditBeginningBalance);
    }

    [Fact]
    public async Task Department_head_sees_only_own_department_lines_but_every_fund_balance()
    {
        await using AsyncServiceScope scope = As(Roles.DepartmentHead, _streetsDept);
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;

        Assert.NotEmpty(workspace.Lines);
        Assert.All(workspace.Lines, l => Assert.Equal(_streetsDept, l.DepartmentId));
        Assert.All(workspace.Lines, l => Assert.True(l.CanEdit));
        Assert.Equal(5, workspace.FundBalances.Count);                       // fund totals are whole-fund figures
        Assert.All(workspace.FundBalances, f => Assert.False(f.CanEditBeginningBalance));
        Assert.Single(workspace.Departments);                                 // add-line picker offers only their department
    }

    [Fact]
    public async Task Viewer_sees_everything_read_only()
    {
        await using AsyncServiceScope scope = As(Roles.Viewer);
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;

        Assert.Equal(95, workspace.Lines.Count);
        Assert.All(workspace.Lines, l => Assert.False(l.CanEdit));
        Assert.False(workspace.CanAddLines);
    }

    [Fact]
    public async Task Department_head_cannot_edit_another_departments_line_even_by_id()
    {
        Guid policeLine;
        await using (AsyncServiceScope fd = As(Roles.FinanceDirector))
        {
            BudgetWorkspaceDto all = (await fd.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;
            policeLine = all.Lines.First(l => l.DepartmentId == _policeDept).Id;
        }

        await using AsyncServiceScope scope = As(Roles.DepartmentHead, _streetsDept);
        Result result = await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().UpdateLineAmountAsync(_draft2027, policeLine, 1m);

        Assert.True(result.IsFailure);
        Assert.Contains("permission", result.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adopted_versions_are_read_only_for_everyone()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetEntryService service = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto adopted = (await service.GetWorkspaceAsync(_adopted2025))!;

        Assert.False(adopted.IsEditable);
        Assert.False(adopted.CanAddLines);
        Assert.All(adopted.Lines, l => Assert.False(l.CanEdit));
        Assert.True((await service.UpdateLineAmountAsync(_adopted2025, adopted.Lines[0].Id, 1m)).IsFailure);
        Assert.True((await service.SetBeginningBalanceAsync(_adopted2025, adopted.FundBalances[0].FundId, 1m)).IsFailure);
    }

    [Fact]
    public async Task Changing_an_amount_moves_the_fund_balance_by_the_same_amount()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetEntryService service = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto before = (await service.GetWorkspaceAsync(_draft2027))!;
        BudgetLineDto line = before.Lines.First(l => l.FundCode == "1000" && l.AccountType == Domain.Accounts.AccountType.Expenditure);
        decimal appropriationsBefore = before.FundBalances.Single(f => f.FundCode == "1000").Summary.Appropriations;

        Result result = await service.UpdateLineAmountAsync(_draft2027, line.Id, line.Amount + 1_000m);
        BudgetWorkspaceDto after = (await service.GetWorkspaceAsync(_draft2027))!;

        Assert.True(result.IsSuccess);
        Assert.Equal(appropriationsBefore + 1_000m, after.FundBalances.Single(f => f.FundCode == "1000").Summary.Appropriations);
        Assert.Equal(line.Amount + 1_000m, after.Lines.Single(l => l.Id == line.Id).Amount);
    }

    [Fact]
    public async Task Negative_amounts_are_rejected_before_touching_the_database()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetEntryService service = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto workspace = (await service.GetWorkspaceAsync(_draft2027))!;

        Result result = await service.UpdateLineAmountAsync(_draft2027, workspace.Lines[0].Id, -5m);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Adding_a_duplicate_line_or_an_expenditure_without_a_department_is_refused_by_the_domain()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetEntryService service = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto workspace = (await service.GetWorkspaceAsync(_draft2027))!;
        BudgetLineDto existing = workspace.Lines.First(l => l.DepartmentId == _policeDept);

        Result<Guid> duplicate = await service.AddLineAsync(new AddBudgetLineRequest(_draft2027, existing.FundId, existing.DepartmentId, existing.AccountId, 1m, 0m, 0m, null));
        Result<Guid> noDepartment = await service.AddLineAsync(new AddBudgetLineRequest(_draft2027, existing.FundId, null, existing.AccountId, 1m, 0m, 0m, null));

        Assert.True(duplicate.IsFailure);
        Assert.Contains("already exists", duplicate.Errors.Single().Message, StringComparison.Ordinal);
        Assert.True(noDepartment.IsFailure);
        Assert.Contains("require a department", noDepartment.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Department_head_can_add_and_remove_a_line_in_their_own_department_only()
    {
        await using AsyncServiceScope scope = As(Roles.DepartmentHead, _streetsDept);
        IBudgetEntryService service = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto workspace = (await service.GetWorkspaceAsync(_draft2027))!;
        LookupDto capitalProjects = workspace.Funds.Single(f => f.Code == "4901");
        AccountLookupDto fuel = workspace.Accounts.Single(a => a.Code == "5420");

        Result<Guid> own = await service.AddLineAsync(new AddBudgetLineRequest(_draft2027, capitalProjects.Id, _streetsDept, fuel.Id, 2_500m, 0m, 0m, "Loader fuel"));
        Result<Guid> other = await service.AddLineAsync(new AddBudgetLineRequest(_draft2027, capitalProjects.Id, _policeDept, fuel.Id, 2_500m, 0m, 0m, null));

        Assert.True(own.IsSuccess, string.Join("; ", own.Errors.Select(e => e.Message)));
        Assert.True(other.IsFailure);
        Assert.True((await service.RemoveLineAsync(_draft2027, own.Value)).IsSuccess);
    }
}
