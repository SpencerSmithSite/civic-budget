using CivicBudget.Application.Budgets;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Reports;

public sealed class ReportService(
    IBudgetEntryService entry,
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : IReportService
{
    public async Task<FundSummaryReportDto?> FundSummaryAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        (ReportHeaderDto header, BudgetWorkspaceDto workspace)? loaded = await LoadAsync(budgetVersionId, ct);
        return loaded is null ? null : ReportBuilder.FundSummary(loaded.Value.header, loaded.Value.workspace);
    }

    public async Task<DepartmentDetailReportDto?> DepartmentDetailAsync(Guid budgetVersionId, Guid? departmentId, CancellationToken ct = default)
    {
        (ReportHeaderDto header, BudgetWorkspaceDto workspace)? loaded = await LoadAsync(budgetVersionId, ct);
        return loaded is null ? null : ReportBuilder.DepartmentDetail(loaded.Value.header, loaded.Value.workspace, departmentId);
    }

    public async Task<CategoryReportDto?> RevenueVsExpenditureAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        (ReportHeaderDto header, BudgetWorkspaceDto workspace)? loaded = await LoadAsync(budgetVersionId, ct);
        return loaded is null ? null : ReportBuilder.RevenueVsExpenditure(loaded.Value.header, loaded.Value.workspace);
    }

    /// <summary>The workspace read carries the tenant filter and the department user visibility rule; the header needs only the government's name.</summary>
    private async Task<(ReportHeaderDto, BudgetWorkspaceDto)?> LoadAsync(Guid budgetVersionId, CancellationToken ct)
    {
        BudgetWorkspaceDto? workspace = await entry.GetWorkspaceAsync(budgetVersionId, ct);
        if (workspace is null)
        {
            return null;
        }

        // Governments is not tenant-filtered (it is the tenant), so look it up by the user's claim.
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == currentUser.GovernmentId, ct);

        var header = new ReportHeaderDto(
            budgetVersionId, government.Name, workspace.Version.Year, workspace.Version.Label, workspace.Version.Status,
            workspace.Version.ResolutionNumber, currentUser.DisplayName ?? "", clock.GetUtcNow());
        return (header, workspace);
    }
}
