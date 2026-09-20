using CivicBudget.Application.Common;

namespace CivicBudget.Application.Budgets;

/// <summary>
/// The department side of budgeting: a department writes its narrative and hands its request to
/// the fiscal officer, who can send it back. Reading happens through
/// <see cref="IBudgetEntryService.GetWorkspaceAsync"/>, whose <see cref="BudgetWorkspaceDto.DepartmentRequests"/>
/// already says where each department stands and what the current user may do about it.
/// </summary>
public interface IDepartmentRequestService
{
    Task<Result> SaveNarrativeAsync(Guid versionId, Guid departmentId, string? narrative, CancellationToken ct = default);

    /// <summary>Marks the department's request submitted; its lines and narrative lock for department users until returned.</summary>
    Task<Result> SubmitAsync(Guid versionId, Guid departmentId, CancellationToken ct = default);

    /// <summary>Fiscal authority only: sends a submitted request back with a note the department will see.</summary>
    Task<Result> ReturnAsync(Guid versionId, Guid departmentId, string note, CancellationToken ct = default);
}
