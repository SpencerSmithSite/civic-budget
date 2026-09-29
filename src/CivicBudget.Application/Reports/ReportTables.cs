using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Application.Import;

namespace CivicBudget.Application.Reports;

/// <summary>
/// Each report and grid as an <see cref="ExportTable"/> for the XLSX download. Kept beside the
/// DTOs so a column added to a report is added to its spreadsheet in the same change.
/// </summary>
public static class ReportTables
{
    /// <summary>The workspace lines in the import's column layout (full number first, then the codes), so an export can be edited and imported back.</summary>
    public static ExportTable Lines(BudgetWorkspaceDto workspace) => new(
        $"FY{workspace.Version.Year} {workspace.Version.Label}",
        ImportFileParser.Headers,
        workspace.Lines.Select(l => new object?[]
        {
            l.AccountNumber, l.FundCode, l.DepartmentCode, l.AccountCode, l.AccountName, l.Amount, l.PriorYearActual, l.CurrentYearBudget, l.Justification,
        }).ToList());

    /// <summary>
    /// The multi-year plan: every line in every year, then each fund's projected ending balance by year
    /// (none for a department user, who sees no fund totals). "Notes" lists the years typed by hand, or the years a fund overspends.
    /// </summary>
    public static ExportTable Plan(BudgetPlanDto plan) => new(
        $"FY{plan.BudgetYear} {plan.VersionLabel} plan",
        ["Row", "Account number", "Fund", "Department", "Account name", "Type", .. plan.FiscalYears.Select(y => $"FY{y}"), "Notes"],
        plan.Lines.Select(l => new object?[] { "Line", l.AccountNumber, $"{l.FundCode} {l.FundName}", l.DepartmentCode is null ? null : $"{l.DepartmentCode} {l.DepartmentName}", l.AccountName, Labels.AccountType(l.AccountType) }
                .Concat(l.Amounts.Cast<object?>())
                .Append(plan.FiscalYears.Any(y => l.Typed[y - plan.BudgetYear]) ? "Typed: " + string.Join(", ", plan.FiscalYears.Where(y => l.Typed[y - plan.BudgetYear]).Select(y => $"FY{y}")) : null)
                .ToArray())
            .Concat(plan.Funds.Select(f => new object?[] { "Ending balance", null, $"{f.FundCode} {f.FundName}", null, "Projected ending balance", null }
                .Concat(f.Years.Select(y => (object?)y.EndingBalance))
                .Append(f.Years.Any(y => y.OverLimit) ? "Over limit: " + string.Join(", ", f.Years.Where(y => y.OverLimit).Select(y => $"FY{y.FiscalYear}")) : null)
                .ToArray()))
            .ToList());

    public static ExportTable FundSummary(FundSummaryReportDto report) => new(
        "Budget summary by fund",
        ["Fund", "Name", "Category", "Beginning balance", "Revenues", "Transfers in", "Estimated resources", "Expenditures", "Transfers out", "Appropriations", "Projected ending balance", "Within limit"],
        report.Funds.Append(report.Total).Select(f => new object?[]
        {
            f.FundCode, f.FundName, f.Category?.ToString(), f.BeginningBalance, f.Revenues, f.TransfersIn, f.EstimatedResources,
            f.Expenditures, f.TransfersOut, f.Appropriations, f.ProjectedEndingBalance, f.IsWithinAppropriationLimit ? "Yes" : "No",
        }).ToList());

    public static ExportTable DepartmentDetail(DepartmentDetailReportDto report) => new(
        "Department budget detail",
        ["Department", "Account number", "Fund", "Account name", "Type", "Category", "Prior year actual", "Current year budget", "Amount", "Change", "Change %", "Justification"],
        report.Departments.SelectMany(d => d.Lines.Select(l => new object?[]
        {
            $"{d.DepartmentCode} {d.DepartmentName}", l.AccountNumber, $"{l.FundCode} {l.FundName}", l.AccountName,
            Labels.AccountType(l.AccountType), Labels.Category(l.Category),
            l.PriorYearActual, l.CurrentYearBudget, l.Amount, l.DollarChange, l.PercentChange, l.Justification,
        })).ToList());

    public static ExportTable RevenueVsExpenditure(CategoryReportDto report) => new(
        "Revenue vs expenditure",
        ["Section", "Category", "Prior year actual", "Current year budget", "Amount", "Change", "Change %"],
        report.Revenues.Select(r => Row("Revenues", r))
            .Append(Row("Revenues", report.RevenueTotal))
            .Concat(report.Expenditures.Select(r => Row("Expenditures", r)))
            .Append(Row("Expenditures", report.ExpenditureTotal))
            .Append(Row("Net", report.Net))
            .ToList());

    /// <summary>The detailed schedule and the issued columns side by side, one row per fund, subtotal, and total.</summary>
    public static ExportTable Certificate(CertificateReportDto report) => new(
        report.Header.Title,
        new[] { "Fund type", "Fund", "Name", "Cash 12/31", "Encumbrances", "Nonspendable", "Reserves", "Unpaid advances", report.BalanceLabel }
            .Concat(report.RevenueColumnLabels).Concat([report.OtherSourcesLabel, "Estimated revenue", "Total available", "Appropriations", "Within limit"]).ToList(),
        report.Sections.SelectMany(s => s.Funds.Select(f => CertificateRow(s.Label, f)).Append(CertificateRow(s.Label, s.Subtotal)))
            .Append(CertificateRow("", report.Total)).ToList());

    public static ExportTable BudgetVsActual(BudgetActualReportDto report) => new(
        "Budget vs actual",
        ["Fund", "Department", "Account number", "Account", "Budget", "Spent", "Encumbered", "Remaining", "Used %", "Last year by now"],
        report.Funds.SelectMany(f => f.Departments.SelectMany(d => d.Lines.Select(r => ActualRow(f.FundCode, d.Label, r)).Append(ActualRow(f.FundCode, d.Label, d.Subtotal)))
                .Append(ActualRow(f.FundCode, "", f.Subtotal)))
            .Append(ActualRow("", "", report.Total)).ToList());

    public static ExportTable RevenueVsReceipts(RevenueReceiptReportDto report) => new(
        "Revenue vs receipts",
        ["Fund", "Account number", "Account", "Estimate", "Received", "Still to collect", "Collected %", "Normally by now %"],
        report.Funds.SelectMany(f => f.Lines.Select(r => ReceiptRow(f.FundCode, r)).Append(ReceiptRow(f.FundCode, f.Subtotal)))
            .Append(ReceiptRow("", report.Total)).ToList());

    public static ExportTable FundProjection(FundProjectionReportDto report) => new(
        "Projected fund balances",
        ["Fund", "Name", "Beginning balance", "Budgeted receipts", "Received to date", "Projected receipts", "Appropriations", "Spent to date", "Encumbered", "Projected spending", "Budgeted ending", "Projected ending", "Difference"],
        report.Funds.Append(report.Total).Select(f => new object?[]
        {
            f.FundCode, f.FundName, f.BeginningBalance, f.BudgetedReceipts, f.ReceivedToDate, f.ProjectedReceipts, f.Appropriations, f.SpentToDate, f.Encumbered,
            f.ProjectedSpending, f.BudgetedEnding, f.ProjectedEnding, f.Difference,
        }).ToList());

    public static ExportTable Trends(TrendReportDto report) => new(
        "Multi-year trends",
        ["Fund", "Name", "Fiscal year", "Budgeted receipts", "Actual receipts", "Appropriations", "Actual spending"],
        report.Funds.SelectMany(f => f.Years.Select(c => new object?[] { f.FundCode, f.FundName, c.FiscalYear, c.BudgetedReceipts, c.ActualReceipts, c.Appropriations, c.ActualSpending }))
            .Concat(report.Totals.Select(c => new object?[] { "", "All funds", c.FiscalYear, c.BudgetedReceipts, c.ActualReceipts, c.Appropriations, c.ActualSpending })).ToList());

    private static readonly string[] MeasureLead = ["Fund", "Name", "Department"];

    public static ExportTable AppropriationMeasure(AppropriationMeasureDto report) => new(
        "Appropriation measure",
        MeasureLead.Concat(report.ColumnLabels).Concat(["Other", "Total"]).ToList(),
        report.Funds.SelectMany(f => f.Departments.Select(d => MeasureRow(f.FundCode, f.FundName, d))
                .Append(MeasureRow(f.FundCode, f.FundName, f.Subtotal))
                .Append(new object?[] { f.FundCode, f.FundName, "Transfers out" }.Concat(report.ColumnLabels.Select(_ => (object?)null)).Concat([f.TransfersOut, f.TransfersOut]).ToArray()))
            .Append(MeasureRow("", "", report.DepartmentsTotal)).ToList());

    public static ExportTable PositionRoster(PositionRosterDto report) => new(
        "Position roster",
        ["Department", "Title", "Employee", "ERP employee number", "Hire date", "Years of service", "Pay rate", "Months", "Funds", "Pay", "Benefits", "Total"],
        report.Departments.SelectMany(d => d.Positions.Select(p => new object?[]
            {
                $"{d.DepartmentCode} {d.DepartmentName}", p.Title, p.EmployeeName ?? "Vacant", p.EmployeeId,
                p.HireDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), p.YearsOfService, p.PayText, p.Months, p.Funds, p.Pay, p.Benefits, p.Total,
            })
            .Append([$"{d.DepartmentCode} {d.DepartmentName}", $"Total, {d.Positions.Count} positions", null, null, null, null, null, null, null, d.Pay, d.Benefits, d.Total]))
            .Append(["All departments", $"{report.Positions} positions, {report.Vacant} vacant", null, null, null, null, null, null, null, report.Pay, report.Benefits, report.Total])
            .ToList());

    public static ExportTable PersonnelCost(PersonnelCostDto report) => new(
        "Personnel cost by fund",
        ["Fund", "Name", "Department", "Pay", "Retirement", "Medicare", "Workers' compensation", "Insurance", "Total"],
        report.Funds.SelectMany(f => f.Departments.Select(d => CostRow(f.FundCode, f.FundName, d)).Append(CostRow(f.FundCode, f.FundName, f.Subtotal)))
            .Append(CostRow("", "", report.Total)).ToList());

    public static ExportTable BenefitsSummary(BenefitsSummaryDto report) => new(
        "Benefits summary",
        ["Benefit", "Coverage or rate", "Positions", "Monthly premium", "Pay it is charged on", "Employer cost", "Employee share or pick-up"],
        report.Retirement.Select(r => new object?[] { r.System, $"{r.EmployerRate}%", r.Members, null, r.PensionablePay, r.EmployerShare, r.PickedUp })
            .Concat(report.Insurance.SelectMany(i => i.Tiers.Where(t => t.Positions > 0)
                .Select(t => new object?[] { i.Plan, Domain.Personnel.PositionCostCalculator.TierName(t.Tier), t.Positions, t.MonthlyPremium, null, t.EmployerCost, t.EmployeeShare })))
            .Append(["Medicare", $"{report.MedicareRate}%", report.Positions, null, report.TaxablePay, report.Medicare, null])
            .Append(["Workers' compensation", $"{report.WorkersCompRate}%", report.Positions, null, report.TaxablePay, report.WorkersComp, null])
            .Append(["Total", null, null, null, null, report.Total, null])
            .ToList());

    private static object?[] CostRow(string fundCode, string fundName, PersonnelCostRowDto r) =>
        [fundCode, fundName, r.Label, r.Pay, r.Retirement, r.Medicare, r.WorkersComp, r.Insurance, r.Total];

    private static object?[] ActualRow(string fund, string department, BudgetActualRowDto r) =>
        [fund, department, r.AccountNumber, r.Label, r.Budget, r.Actual, r.Encumbered, r.Remaining, r.Used * 100m, r.LastYearAtThisPoint];

    private static object?[] ReceiptRow(string fund, RevenueReceiptRowDto r) =>
        [fund, r.AccountNumber, r.Label, r.Estimate, r.Received, r.StillToCollect, r.Collected * 100m, r.NormallyByNow * 100m];

    private static object?[] MeasureRow(string fundCode, string fundName, MeasureRowDto r) =>
        new object?[] { fundCode, fundName, r.Label }.Concat(r.Columns.Cast<object?>()).Concat([r.Other, r.Total]).ToArray();

    private static object?[] CertificateRow(string section, CertificateRowDto r) =>
        new object?[] { section, r.FundCode, r.FundName, r.Cash, r.Encumbrances, r.Nonspendable, r.Reserves, r.UnpaidAdvances, r.Carryover }
            .Concat(r.RevenueColumns.Cast<object?>())
            .Concat([r.OtherSources, r.EstimatedRevenue, r.TotalAvailable, r.Appropriations, r.IsWithinLimit ? "Yes" : "No"]).ToArray();

    private static object?[] Row(string section, CategoryRowDto r) =>
        [section, r.Label, r.PriorYearActual, r.CurrentYearBudget, r.Amount, r.DollarChange, r.PercentChange];
}
