using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>Reports agree with the workspace they are built from, and a Department Head's detail report is limited to their departments.</summary>
[Collection(SqlServerTests.Name)]
public class ReportServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _policeDept;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Reports");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == Domain.Budgets.BudgetStatus.Draft)).Id;
        _policeDept = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Fund_summary_matches_the_workspace_balances_and_names_the_government()
    {
        await using AsyncServiceScope scope = As(Roles.Viewer);
        IReportService reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;

        FundSummaryReportDto report = (await reports.FundSummaryAsync(_draft2027))!;

        Assert.Equal("Village of Maple Ridge", report.Header.GovernmentName);
        Assert.Equal((2027, "Original"), (report.Header.FiscalYear, report.Header.VersionLabel));
        Assert.Equal("Test Viewer", report.Header.GeneratedBy);
        Assert.Equal(workspace.FundBalances.Count, report.Funds.Count);
        Assert.Equal(workspace.FundBalances.Sum(f => f.Summary.Appropriations), report.Total.Appropriations);
        Assert.Equal(workspace.FundBalances.Sum(f => f.Summary.ProjectedEndingBalance), report.Total.ProjectedEndingBalance);
        Assert.Equal(workspace.FundBalances.Count(f => !f.Summary.IsWithinAppropriationLimit), report.FundsOverLimit);
        Assert.Null(await reports.FundSummaryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Category_report_totals_equal_the_workspace_revenue_and_expenditure_lines()
    {
        await using AsyncServiceScope scope = As(Roles.Viewer);
        IReportService reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;

        CategoryReportDto report = (await reports.RevenueVsExpenditureAsync(_draft2027))!;

        Assert.Equal(workspace.Lines.Where(l => l.AccountType == Domain.Accounts.AccountType.Revenue).Sum(l => l.Amount), report.RevenueTotal.Amount);
        Assert.Equal(workspace.Lines.Where(l => l.AccountType == Domain.Accounts.AccountType.Expenditure).Sum(l => l.Amount), report.ExpenditureTotal.Amount);
        Assert.Equal("Personal services", report.Expenditures[0].Label);
        Assert.Equal("Taxes", report.Revenues[0].Label);
    }

    [Fact]
    public async Task A_department_head_sees_only_their_departments_in_the_detail_report()
    {
        await using AsyncServiceScope director = As(Roles.FinanceDirector);
        await using AsyncServiceScope head = As(Roles.DepartmentHead, _policeDept);

        DepartmentDetailReportDto all = (await director.ServiceProvider.GetRequiredService<IReportService>().DepartmentDetailAsync(_draft2027, null))!;
        DepartmentDetailReportDto mine = (await head.ServiceProvider.GetRequiredService<IReportService>().DepartmentDetailAsync(_draft2027, null))!;
        DepartmentDetailReportDto filtered = (await director.ServiceProvider.GetRequiredService<IReportService>().DepartmentDetailAsync(_draft2027, _policeDept))!;

        Assert.True(all.Departments.Count > 1);
        Assert.Equal("110", Assert.Single(mine.Departments).DepartmentCode);
        Assert.Equal("110", Assert.Single(mine.AvailableDepartments).Code);
        Assert.Equal(mine.TotalAmount, filtered.TotalAmount);
        Assert.Equal(all.AvailableDepartments.Count, filtered.AvailableDepartments.Count); // the picker still lists everyone for the director
    }

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
}
