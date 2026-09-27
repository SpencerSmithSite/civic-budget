using System.Security.Claims;
using CivicBudget.Application.Common;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The reports on the seeded books: FY2026 against the ERP's figures through its last closed month,
/// paced against FY2025; the trends across FY2025 to FY2027; the appropriation measure and its
/// columns; and what a department user may and may not run.
/// </summary>
[Collection(SqlServerTests.Name)]
public class ActualsReportServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge, _amendment2026, _draft2027, _police;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_ActualsReports");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        var versions = await db.BudgetVersions.IgnoreQueryFilters()
            .Join(db.FiscalYears.IgnoreQueryFilters(), v => v.FiscalYearId, f => f.Id, (v, f) => new { v.Id, v.GovernmentId, f.Year, v.VersionNumber })
            .Where(v => v.GovernmentId == _mapleRidge).ToListAsync();
        _amendment2026 = versions.Single(v => v.Year == 2026 && v.VersionNumber == 2).Id;
        _draft2027 = versions.Single(v => v.Year == 2027).Id;
        _police = (await db.Departments.IgnoreQueryFilters().SingleAsync(d => d.GovernmentId == _mapleRidge && d.Code == "110")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Fy2026_is_measured_against_the_erps_figures_and_paced_against_fy2025()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IActualsReportService reports = scope.ServiceProvider.GetRequiredService<IActualsReportService>();

        BudgetActualReportDto spending = (await reports.BudgetVsActualAsync(_amendment2026))!;
        RevenueReceiptReportDto revenue = (await reports.RevenueVsReceiptsAsync(_amendment2026))!;
        FundProjectionReportDto projection = (await reports.FundProjectionAsync(_amendment2026))!;

        Assert.NotNull(spending.Period);
        Assert.True(spending.Period!.ThroughPeriod is > 0 and < 12);
        Assert.True(spending.Total.Actual > 0m && spending.Total.Actual < spending.Total.Budget);
        Assert.All(spending.Funds.SelectMany(f => f.Departments).SelectMany(d => d.Lines), l => Assert.NotNull(l.LastYearAtThisPoint));
        Assert.All(revenue.Funds.SelectMany(f => f.Lines), l => Assert.NotNull(l.NormallyByNow));
        Assert.Equal(620_000m, projection.Funds.Single(f => f.FundCode == "1000").BeginningBalance);
        Assert.Equal(0, projection.LinesProjectedFromBudget);                        // every line has FY2025 history
    }

    [Fact]
    public async Task A_budget_for_a_year_not_begun_has_no_actuals_to_compare()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);

        BudgetActualReportDto report = (await scope.ServiceProvider.GetRequiredService<IActualsReportService>().BudgetVsActualAsync(_draft2027))!;

        Assert.Null(report.Period);
        Assert.Equal(0m, report.Total.Actual);
    }

    [Fact]
    public async Task Trends_run_from_the_adopted_budgets_and_the_erps_years_to_this_draft()
    {
        await using AsyncServiceScope scope = As(Roles.Viewer);

        TrendReportDto trends = (await scope.ServiceProvider.GetRequiredService<IActualsReportService>().TrendsAsync(_draft2027))!;

        Assert.Equal([2025, 2026, 2027], trends.Years.Select(y => y.FiscalYear));
        Assert.Equal(("full year", (string?)null), (trends.Years[0].Actuals, trends.Years[2].Actuals));
        Assert.StartsWith("to ", trends.Years[1].Actuals, StringComparison.Ordinal);
        TrendCellDto general2025 = trends.Funds.Single(f => f.FundCode == "1000").Years[0];
        Assert.True(general2025.ActualReceipts > 0m && general2025.BudgetedReceipts > 0m);
        Assert.Null(trends.Funds.Single(f => f.FundCode == "1000").Years[2].ActualSpending);
    }

    [Fact]
    public async Task The_appropriation_measure_uses_the_saved_columns_once_there_are_any()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IActualsReportService reports = scope.ServiceProvider.GetRequiredService<IActualsReportService>();
        IMeasureColumnService columns = scope.ServiceProvider.GetRequiredService<IMeasureColumnService>();

        AppropriationMeasureDto before = (await reports.AppropriationMeasureAsync(_draft2027))!;
        Assert.True(before.UsingDefaultColumns);
        Assert.Equal(["Personal services"], before.ColumnLabels);
        Assert.Equal(before.Total, before.Funds.Sum(f => f.Total));

        MeasureColumnsDto available = await columns.GetAsync();
        Guid Account(string code) => available.ExpenditureAccounts.Single(a => a.Code == code).Id;
        Result saved = await columns.SaveAsync([new ReportColumnDto("Salaries", [Account("5110"), Account("5120")]), new ReportColumnDto("Benefits", [Account("5210"), Account("5220"), Account("5230"), Account("5240")])]);

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        AppropriationMeasureDto after = (await reports.AppropriationMeasureAsync(_draft2027))!;
        Assert.Equal(["Salaries", "Benefits"], after.ColumnLabels);
        Assert.Equal(before.Total, after.Total);                                         // the same money, split differently
        Assert.Equal(before.DepartmentsTotal.Columns[0], after.DepartmentsTotal.Columns.Sum());
    }

    [Fact]
    public async Task A_department_user_gets_their_own_lines_and_none_of_the_whole_fund_reports()
    {
        await using AsyncServiceScope scope = As(Roles.DepartmentHead, _police);
        IActualsReportService reports = scope.ServiceProvider.GetRequiredService<IActualsReportService>();

        BudgetActualReportDto spending = (await reports.BudgetVsActualAsync(_amendment2026))!;

        Assert.Equal(["110 Police"], spending.Funds.SelectMany(f => f.Departments).Select(d => d.Label).Distinct());
        Assert.Null(await reports.FundProjectionAsync(_amendment2026));
        Assert.Null(await reports.TrendsAsync(_amendment2026));
        Assert.Null(await reports.AppropriationMeasureAsync(_amendment2026));
        Assert.True((await scope.ServiceProvider.GetRequiredService<IMeasureColumnService>().SaveAsync([])).IsFailure);
    }

    private AsyncServiceScope As(string role, Guid? department = null)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        if (department is { } d)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, d.ToString()));
        }

        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
