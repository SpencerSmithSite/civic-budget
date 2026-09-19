namespace CivicBudget.Application.Reports;

/// <summary>
/// The three reports from the spec, built from the same workspace read the entry screens use, so
/// a report never disagrees with the screen. Every role may run them (<c>Policies.CanViewBudget</c>);
/// a department user's Department Detail is limited to their departments, the same rule as the
/// workspace. Null means the version does not exist for this tenant.
/// </summary>
public interface IReportService
{
    Task<FundSummaryReportDto?> FundSummaryAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<DepartmentDetailReportDto?> DepartmentDetailAsync(Guid budgetVersionId, Guid? departmentId, CancellationToken ct = default);

    Task<CategoryReportDto?> RevenueVsExpenditureAsync(Guid budgetVersionId, CancellationToken ct = default);
}
