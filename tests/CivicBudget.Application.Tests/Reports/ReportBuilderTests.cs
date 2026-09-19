using CivicBudget.Application.Budgets;
using CivicBudget.Application.Export;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Tests.Reports;

/// <summary>The three reports over a small hand-built workspace: grouping, ordering, subtotals, and the export tables.</summary>
public class ReportBuilderTests
{
    private static readonly Guid General = Guid.NewGuid();
    private static readonly Guid Street = Guid.NewGuid();
    private static readonly Guid Police = Guid.NewGuid();
    private static readonly Guid Streets = Guid.NewGuid();

    private static readonly ReportHeaderDto Header = new(Guid.NewGuid(), "Village of Maple Ridge", 2027, "Original", BudgetStatus.Draft, null, "Test", DateTimeOffset.UnixEpoch);

    private static BudgetWorkspaceDto Workspace() => new(
        new BudgetVersionSummaryDto(Header.BudgetVersionId, 2027, 1, "Original", BudgetStatus.Draft, null, null, 5),
        AccountNumberFormat.UanVillage,
        true, true,
        [
            Line(Street, "2011", "Street", Streets, "620", "Streets", "5100", "Salaries", AccountType.Expenditure, ReportingCategory.PersonalServices, 100m, 90m, 80m),
            Line(General, "1000", "General", Police, "110", "Police", "5200", "Overtime", AccountType.Expenditure, ReportingCategory.PersonalServices, 50m, 40m, 30m),
            Line(General, "1000", "General", Police, "110", "Police", "5300", "Fuel", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials, 20m, 20m, 20m),
            Line(General, "1000", "General", null, null, null, "4100", "Property tax", AccountType.Revenue, ReportingCategory.Taxes, 300m, 250m, 0m),
            Line(General, "1000", "General", Police, "110", "Police", "4500", "Police fines", AccountType.Revenue, ReportingCategory.FinesAndForfeitures, 10m, 0m, 0m),
        ],
        [
            Balance(Street, "2011", "Street", FundCategory.SpecialRevenue, beginning: 5m, revenues: 0m, transfersIn: 50m, expenditures: 100m, transfersOut: 0m),
            Balance(General, "1000", "General", FundCategory.General, beginning: 500m, revenues: 310m, transfersIn: 0m, expenditures: 70m, transfersOut: 50m),
        ],
        [], [], [],
        [
            new DepartmentRequestDto(Police, "110", "Police", DepartmentRequestStatus.Submitted, "Overtime covers two officers on leave.", DateTimeOffset.UnixEpoch, "Chief Hale", null, null, 3, 50m, 60m, 70m, false, false, true),
        ]);

    [Fact]
    public void Fund_summary_orders_by_code_carries_the_certificate_arithmetic_and_totals()
    {
        FundSummaryReportDto report = ReportBuilder.FundSummary(Header, Workspace());

        Assert.Equal(["1000", "2011"], report.Funds.Select(f => f.FundCode));
        FundSummaryRowDto street = report.Funds[1];
        Assert.Equal(55m, street.EstimatedResources);      // 5 + 0 + 50
        Assert.Equal(100m, street.Appropriations);
        Assert.Equal(-45m, street.ProjectedEndingBalance);
        Assert.False(street.IsWithinAppropriationLimit);
        Assert.Equal(1, report.FundsOverLimit);
        Assert.Equal(505m, report.Total.BeginningBalance);
        Assert.Equal(220m, report.Total.Appropriations);    // 100 + 70 expenditures + 50 transfers out
        Assert.Equal("All funds", report.Total.FundName);
        Assert.False(report.Total.IsWithinAppropriationLimit);
    }

    [Fact]
    public void Department_detail_groups_visible_lines_by_department_with_expenditure_subtotals()
    {
        DepartmentDetailReportDto report = ReportBuilder.DepartmentDetail(Header, Workspace(), departmentId: null);

        Assert.Equal(["110", "620"], report.Departments.Select(d => d.DepartmentCode));
        DepartmentDetailDto police = report.Departments[0];
        Assert.Equal(["4500", "5200", "5300"], police.Lines.Select(l => l.AccountCode)); // revenue first, then expenditures by code
        Assert.Equal(70m, police.Amount);                    // fines are revenue and stay out of the expenditure subtotal
        Assert.Equal(60m, police.CurrentYearBudget);
        Assert.Equal(10m, police.DollarChange);
        Assert.Equal(16.7m, Math.Round(police.PercentChange!.Value, 1));
        Assert.Equal(170m, report.TotalAmount);
        Assert.Equal(["110", "620"], report.AvailableDepartments.Select(d => d.Code));
        Assert.Null(report.SelectedDepartmentId);
        Assert.Equal("Overtime covers two officers on leave.", police.Narrative); // the department's message prints under its heading
        Assert.Null(report.Departments[1].Narrative);                             // Streets wrote none
    }

    [Fact]
    public void Department_detail_can_be_limited_to_one_department_while_still_listing_the_others_to_pick()
    {
        DepartmentDetailReportDto report = ReportBuilder.DepartmentDetail(Header, Workspace(), Streets);

        Assert.Equal(["620"], report.Departments.Select(d => d.DepartmentCode));
        Assert.Equal(Streets, report.SelectedDepartmentId);
        Assert.Equal(2, report.AvailableDepartments.Count);
    }

    [Fact]
    public void Revenue_vs_expenditure_groups_by_category_in_enum_order_and_nets_the_totals()
    {
        CategoryReportDto report = ReportBuilder.RevenueVsExpenditure(Header, Workspace());

        Assert.Equal(["Taxes", "Fines and forfeitures"], report.Revenues.Select(r => r.Label));
        Assert.Equal(["Personal services", "Supplies and materials"], report.Expenditures.Select(r => r.Label));
        Assert.Equal(150m, report.Expenditures[0].Amount);
        Assert.Equal(310m, report.RevenueTotal.Amount);
        Assert.Equal(170m, report.ExpenditureTotal.Amount);
        Assert.Equal(140m, report.Net.Amount);
        Assert.Equal(250m - 150m, report.Net.CurrentYearBudget);
    }

    [Fact]
    public void Export_tables_have_one_row_per_line_and_the_lines_table_uses_the_import_headers()
    {
        BudgetWorkspaceDto workspace = Workspace();

        ExportTable lines = ReportTables.Lines(workspace);
        ExportTable detail = ReportTables.DepartmentDetail(ReportBuilder.DepartmentDetail(Header, workspace, null));
        ExportTable summary = ReportTables.FundSummary(ReportBuilder.FundSummary(Header, workspace));
        ExportTable category = ReportTables.RevenueVsExpenditure(ReportBuilder.RevenueVsExpenditure(Header, workspace));

        Assert.Equal(["Account Number", "Fund", "Department", "Account", "Account Name", "Amount", "Prior Year Actual", "Current Year Budget", "Justification"], lines.Headers);
        Assert.Equal("2011-620-5100", lines.Rows[0][0]);
        Assert.Equal(5, lines.Rows.Count);
        Assert.Equal(4, detail.Rows.Count);        // the fund-only revenue line has no department
        Assert.Equal(3, summary.Rows.Count);       // two funds plus the total
        Assert.Equal(2 + 1 + 2 + 1 + 1, category.Rows.Count);
        Assert.All(lines.Rows.Concat(detail.Rows).Concat(summary.Rows).Concat(category.Rows), row => Assert.Equal(row.Length, HeadersFor(row, lines, detail, summary, category)));
    }

    private static int HeadersFor(object?[] row, params ExportTable[] tables) =>
        tables.First(t => t.Rows.Contains(row)).Headers.Count;

    private static BudgetLineDto Line(Guid fundId, string fundCode, string fundName, Guid? deptId, string? deptCode, string? deptName,
        string accountCode, string accountName, AccountType type, ReportingCategory category, decimal amount, decimal current, decimal prior) =>
        new(Guid.NewGuid(), fundId, fundCode, fundName, deptId, deptCode, deptName, Guid.NewGuid(), accountCode, accountName, deptCode is null ? $"{fundCode}-{accountCode}" : $"{fundCode}-{deptCode}-{accountCode}", type, category,
            amount, prior, current, null, true);

    private static FundBalanceDto Balance(Guid fundId, string code, string name, FundCategory category,
        decimal beginning, decimal revenues, decimal transfersIn, decimal expenditures, decimal transfersOut)
    {
        var summary = new FundBalanceSummary(fundId, beginning, revenues, transfersIn, expenditures, transfersOut);
        return new FundBalanceDto(fundId, code, name, category, summary, AppropriationLimitCheck.Evaluate(summary, Domain.Governments.AppropriationLimitMode.Warn), true);
    }
}
