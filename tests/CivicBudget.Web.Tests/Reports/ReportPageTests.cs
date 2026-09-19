using CivicBudget.Application.Reports;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Web.Components.Admin.Reports;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Reports;

/// <summary>Report pages render the header block, the rows, and the totals from the DTOs, and link to their XLSX twin.</summary>
public class ReportPageTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly ReportHeaderDto Header = new(VersionId, "Village of Maple Ridge", 2027, "Original", BudgetStatus.Draft, null, "Dana Whitfield", new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

    private readonly FakeReportService _reports = new();

    public ReportPageTests()
    {
        Services.AddSingleton<IReportService>(_reports);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Fund_summary_shows_the_certificate_columns_the_total_row_and_flags_a_fund_over_limit()
    {
        _reports.FundSummary = new FundSummaryReportDto(Header,
        [
            new("1000", "General Fund", FundCategory.General, 500m, 900m, 0m, 800m, 100m, true),
            new("2011", "Street", FundCategory.SpecialRevenue, 10m, 100m, 0m, 200m, 0m, false),
        ],
        new("", "All funds", null, 510m, 1000m, 0m, 1000m, 100m, false));

        IRenderedComponent<FundSummaryReport> page = Render<FundSummaryReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("Village of Maple Ridge", page.Find(".cb-report-gov").TextContent));
        Assert.Contains("1 fund appropriates more than estimated resources", page.Find(".alert-warning").TextContent);
        Assert.Equal(2, page.FindAll("tbody tr").Count);
        Assert.Contains("$1,400.00", page.Markup);   // General estimated resources 500 + 900
        Assert.Contains("-$90.00", page.Markup);     // Street projected ending 110 - 200
        Assert.Contains("$1,510.00", page.Find("tfoot").TextContent); // total estimated resources
        Assert.Single(page.FindAll(".cb-pill-danger"));
        Assert.Contains($"admin/export/reports/{VersionId}/fund-summary.xlsx", page.Find("a.btn").GetAttribute("href"));
        Assert.Contains("by Dana Whitfield", page.Find(".cb-report-head").TextContent);
    }

    [Fact]
    public void Category_report_lists_both_sections_with_subtotals_and_the_net()
    {
        _reports.Category = new CategoryReportDto(Header,
            [new("Taxes", 90m, 95m, 100m)], new("Total revenues", 90m, 95m, 100m),
            [new("Personal services", 50m, 60m, 70m), new("Supplies and materials", 5m, 5m, 5m)], new("Total expenditures", 55m, 65m, 75m));

        IRenderedComponent<CategoryReport> page = Render<CategoryReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tr.cb-group").Count));
        Assert.Equal(2, page.FindAll("tbody tr.cb-subtotal").Count);
        Assert.Contains("Revenues less expenditures", page.Find("tfoot").TextContent);
        Assert.Contains("$25.00", page.Find("tfoot").TextContent);
        Assert.Contains("+16.7%", page.Markup); // personal services 60 to 70
    }

    [Fact]
    public void Department_detail_groups_lines_under_each_department_and_offers_the_filter()
    {
        Guid police = Guid.CreateVersion7();
        _reports.DepartmentDetail = new DepartmentDetailReportDto(Header,
            [new(police, "110", "Police", [new("1000", "General Fund", "5120", "Overtime", "1000-110-5120", Domain.Accounts.AccountType.Expenditure, Domain.Accounts.ReportingCategory.PersonalServices, 30m, 40m, 58m, "Contract")])],
            [(police, "110", "Police"), (Guid.CreateVersion7(), "620", "Streets")], null);

        IRenderedComponent<DepartmentDetailReport> page = Render<DepartmentDetailReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("110 Police", page.Find("tr.cb-group").TextContent));
        Assert.Equal(3, page.FindAll("select option").Count); // All + two departments
        Assert.Contains("Contract", page.Markup);
        Assert.Contains("Police expenditures", page.Find("tr.cb-subtotal").TextContent);
        Assert.Contains("+45.0%", page.Markup); // 40 to 58
        Assert.Empty(page.FindAll("tfoot")); // one department: no grand total row
    }

    [Fact]
    public void An_unknown_version_shows_the_not_found_state()
    {
        IRenderedComponent<FundSummaryReport> page = Render<FundSummaryReport>(p => p.Add(x => x.VersionId, Guid.CreateVersion7()));

        page.WaitForAssertion(() => Assert.Contains("Budget version not found", page.Markup));
    }

    private sealed class FakeReportService : IReportService
    {
        public FundSummaryReportDto? FundSummary { get; set; }
        public DepartmentDetailReportDto? DepartmentDetail { get; set; }
        public CategoryReportDto? Category { get; set; }

        public Task<FundSummaryReportDto?> FundSummaryAsync(Guid budgetVersionId, CancellationToken ct = default) =>
            Task.FromResult(budgetVersionId == VersionId ? FundSummary : null);

        public Task<DepartmentDetailReportDto?> DepartmentDetailAsync(Guid budgetVersionId, Guid? departmentId, CancellationToken ct = default) =>
            Task.FromResult(budgetVersionId == VersionId ? DepartmentDetail : null);

        public Task<CategoryReportDto?> RevenueVsExpenditureAsync(Guid budgetVersionId, CancellationToken ct = default) =>
            Task.FromResult(budgetVersionId == VersionId ? Category : null);
    }
}
