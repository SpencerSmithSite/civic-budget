using CivicBudget.Application.Common;

namespace CivicBudget.Application.Assistant;

/// <summary>One line of a proposal's preview: what it is, what it is now (null for something new), and what it would be.</summary>
public sealed record ProposalRow(string Label, string? Before, string After);

/// <summary>
/// A change the assistant has worked out and the user has not yet confirmed: a title, the preview
/// rows (the first few, with a count of the rest), any text it would save, and the button's words.
/// </summary>
public sealed record AssistantProposalDto(Guid Id, string Title, IReadOnlyList<ProposalRow> Rows, int MoreRows, string? Text, string ConfirmLabel, string? Note);

/// <summary>What happened when the user confirmed: a sentence, and the page to see the result on.</summary>
public sealed record ProposalOutcome(string Message, string? Link);

/// <summary>
/// The proposals waiting for the user's click in one browser tab. A tool adds one with the exact
/// change it previewed; only <see cref="IAssistantService.ConfirmAsync"/> takes it out and runs it,
/// once, and only a person's click calls that. The model is never given a way to confirm. A proposal
/// expires after half an hour, because the budget it was worked out from will have moved on.
/// </summary>
public sealed class AssistantProposals(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public const int PreviewRows = 25;

    private readonly Dictionary<Guid, Pending> _pending = [];

    /// <param name="Commit">Runs the change through the Application service a page would call, as the user.</param>
    /// <param name="AuditText">For the audit trail: "Set the FY2027 plan to 5 years at 4% a year".</param>
    public sealed record Pending(AssistantProposalDto Proposal, Func<CancellationToken, Task<Result<ProposalOutcome>>> Commit, string AuditText, string EntityType, Guid EntityId, DateTimeOffset ExpiresAtUtc);

    public AssistantProposalDto Add(string title, IReadOnlyList<ProposalRow> rows, string? text, string confirmLabel, string? note,
        Func<CancellationToken, Task<Result<ProposalOutcome>>> commit, string auditText, string entityType, Guid entityId)
    {
        var proposal = new AssistantProposalDto(Guid.NewGuid(), title, [.. rows.Take(PreviewRows)], Math.Max(0, rows.Count - PreviewRows), text, confirmLabel, note);
        lock (_pending)
        {
            _pending[proposal.Id] = new Pending(proposal, commit, auditText, entityType, entityId, clock.GetUtcNow() + Lifetime);
        }

        return proposal;
    }

    /// <summary>The proposal, removed so it can run only once; null when unknown, used, or expired.</summary>
    public Pending? Take(Guid id)
    {
        lock (_pending)
        {
            if (!_pending.Remove(id, out Pending? pending))
            {
                return null;
            }

            return pending.ExpiresAtUtc > clock.GetUtcNow() ? pending : null;
        }
    }
}
