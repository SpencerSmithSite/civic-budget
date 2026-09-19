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

    private static object?[] Row(string section, CategoryRowDto r) =>
        [section, r.Label, r.PriorYearActual, r.CurrentYearBudget, r.Amount, r.DollarChange, r.PercentChange];
}
