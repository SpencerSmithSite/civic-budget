using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Reports;

/// <summary>
/// Shapes a <see cref="BudgetWorkspaceDto"/> into each report. Pure functions: the service adds the
/// header and the workspace; the tests feed a hand-built workspace and check the arithmetic.
/// </summary>
public static class ReportBuilder
{
    public static FundSummaryReportDto FundSummary(ReportHeaderDto header, BudgetWorkspaceDto workspace)
    {
        List<FundSummaryRowDto> funds = workspace.FundBalances
            .OrderBy(f => f.FundCode, StringComparer.Ordinal)
            .Select(f => new FundSummaryRowDto(
                f.FundCode, f.FundName, f.Category,
                f.Summary.BeginningBalance, f.Summary.Revenues, f.Summary.TransfersIn, f.Summary.Expenditures, f.Summary.TransfersOut,
                f.Summary.IsWithinAppropriationLimit))
            .ToList();

        var total = new FundSummaryRowDto(
            "", "All funds", null,
            funds.Sum(f => f.BeginningBalance), funds.Sum(f => f.Revenues), funds.Sum(f => f.TransfersIn),
            funds.Sum(f => f.Expenditures), funds.Sum(f => f.TransfersOut),
            funds.All(f => f.IsWithinAppropriationLimit));
        return new FundSummaryReportDto(header, funds, total);
    }

    public static DepartmentDetailReportDto DepartmentDetail(ReportHeaderDto header, BudgetWorkspaceDto workspace, Guid? departmentId)
    {
        // The workspace already hides other departments from a Department Head, so grouping its lines is the filter.
        List<DepartmentDetailDto> departments = workspace.Lines
            .Where(l => l.DepartmentId is not null && (departmentId is null || l.DepartmentId == departmentId))
            .GroupBy(l => (l.DepartmentId!.Value, l.DepartmentCode!, l.DepartmentName!))
            .OrderBy(g => g.Key.Item2, StringComparer.Ordinal)
            .Select(g => new DepartmentDetailDto(
                g.Key.Item1, g.Key.Item2, g.Key.Item3,
                g.OrderBy(l => l.FundCode, StringComparer.Ordinal).ThenBy(l => l.AccountType).ThenBy(l => l.AccountCode, StringComparer.Ordinal)
                    .Select(l => new DetailLineDto(
                        l.FundCode, l.FundName, l.AccountCode, l.AccountName, l.AccountType, l.Category,
                        l.PriorYearActual, l.CurrentYearBudget, l.Amount, l.Justification))
                    .ToList()))
            .ToList();

        List<(Guid, string, string)> available = workspace.Lines
            .Where(l => l.DepartmentId is not null)
            .Select(l => (l.DepartmentId!.Value, l.DepartmentCode!, l.DepartmentName!))
            .Distinct()
            .OrderBy(d => d.Item2, StringComparer.Ordinal)
            .ToList();

        return new DepartmentDetailReportDto(header, departments, available, departmentId);
    }

    public static CategoryReportDto RevenueVsExpenditure(ReportHeaderDto header, BudgetWorkspaceDto workspace)
    {
        List<CategoryRowDto> revenues = ByCategory(workspace, AccountType.Revenue);
        List<CategoryRowDto> expenditures = ByCategory(workspace, AccountType.Expenditure);
        return new CategoryReportDto(header, revenues, Total("Total revenues", revenues), expenditures, Total("Total expenditures", expenditures));
    }

    private static List<CategoryRowDto> ByCategory(BudgetWorkspaceDto workspace, AccountType type) =>
        workspace.Lines
            .Where(l => l.AccountType == type)
            .GroupBy(l => l.Category)
            .OrderBy(g => g.Key) // enum order is the order Ohio reports list them (personal services first, taxes first)
            .Select(g => new CategoryRowDto(Labels.Category(g.Key), g.Sum(l => l.PriorYearActual), g.Sum(l => l.CurrentYearBudget), g.Sum(l => l.Amount)))
            .ToList();

    private static CategoryRowDto Total(string label, List<CategoryRowDto> rows) =>
        new(label, rows.Sum(r => r.PriorYearActual), rows.Sum(r => r.CurrentYearBudget), rows.Sum(r => r.Amount));
}
