using CivicBudget.Application.Common;
using CivicBudget.Domain.Budgets;

namespace CivicBudget.Application.Budgets;

/// <summary>What the workflow bar needs: where the version is, what this user may do next, and why not.</summary>
public sealed record WorkflowStateDto(
    BudgetStatus Status,
    bool CanPropose,
    bool CanReturnToDraft,
    bool CanAdopt,
    bool CanAmend,
    bool HasOpenSibling,
    IReadOnlyList<AppropriationLimitResult> LimitResults,
    bool IsSuperseded = false)
{
    /// <summary>Only the latest adopted version may go on the portal; a superseded one is history.</summary>
    public bool CanPublish => Status == BudgetStatus.Adopted && !IsSuperseded;

    /// <summary>Block mode and at least one fund over its limit: Propose and Adopt are refused.</summary>
    public bool BlocksTransition => LimitResults.Any(r => r.BlocksWorkflow);

    /// <summary>Warn mode and at least one fund over its limit: the user must acknowledge before proceeding.</summary>
    public bool RequiresAcknowledgement => !BlocksTransition && LimitResults.Any(r => r.Severity == AppropriationLimitSeverity.Warning);
}

/// <summary>
/// Draft → Proposed → Adopted, plus amendments. Every transition is Administrator or Fiscal Officer only, checks
/// the appropriation limit in the government's mode (SPEC section 5.1), and writes an audit event.
/// </summary>
public interface IBudgetWorkflowService
{
    Task<WorkflowStateDto?> GetStateAsync(Guid versionId, CancellationToken ct = default);
    Task<Result> ProposeAsync(Guid versionId, bool acknowledgeWarnings, CancellationToken ct = default);
    Task<Result> ReturnToDraftAsync(Guid versionId, CancellationToken ct = default);
    Task<Result> AdoptAsync(Guid versionId, string resolutionNumber, bool acknowledgeWarnings, CancellationToken ct = default);
    Task<Result<Guid>> CreateAmendmentAsync(Guid adoptedVersionId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Creates a fiscal year's Original budget: empty, or started from the prior year's latest adopted
    /// version (optionally changed by a percentage). The year must be open and have no budget yet.
    /// </summary>
    Task<Result<StartBudgetResultDto>> StartBudgetAsync(StartBudgetRequest request, CancellationToken ct = default);
}

/// <param name="StartFromPriorYear">False starts an empty budget; true copies the prior year's latest adopted version.</param>
public sealed record StartBudgetRequest(Guid FiscalYearId, bool StartFromPriorYear, decimal AdjustmentPercent = 0m, SeedAdjustmentScope Scope = SeedAdjustmentScope.AllLines, bool RoundToWholeDollars = false);

/// <param name="Skipped">Prior-year lines left out because their fund, department, or account has been retired.</param>
public sealed record StartBudgetResultDto(Guid VersionId, int LineCount, IReadOnlyList<string> Skipped);
