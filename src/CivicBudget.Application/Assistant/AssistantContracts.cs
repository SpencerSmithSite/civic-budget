using CivicBudget.Application.Common;
using Microsoft.Extensions.AI;

namespace CivicBudget.Application.Assistant;

/// <summary>One message of the conversation so far, as the panel shows it.</summary>
public sealed record AssistantMessage(bool FromUser, string Text);

/// <summary>What the assistant looked at to answer, in words ("Budget against actual, FY2026 Amendment 1").</summary>
public sealed record AssistantStep(string Summary);

/// <summary>The answer, the steps behind it, a page to open when the user asked to go somewhere, and any changes waiting for their confirmation.</summary>
public sealed record AssistantReply(string Text, IReadOnlyList<AssistantStep> Steps, string? NavigateTo, IReadOnlyList<AssistantProposalDto> Proposals);

/// <param name="CurrentPath">The page the user is on ("/admin/budgets/{id}/plan"), so "this budget" means something.</param>
public sealed record AssistantRequest(IReadOnlyList<AssistantMessage> History, string Question, string? CurrentPath);

/// <summary>
/// Whether the assistant can answer for this user. Without a model the button is not shown at all;
/// with a model but switched off for the government, the panel says who can turn it on.
/// </summary>
public sealed record AssistantStatus(bool Configured, bool Enabled)
{
    public bool Available => Configured && Enabled;

    public string? Reason => !Configured
        ? "The assistant is not connected to a model. The operator connects it (Assistant:ApiKey)."
        : !Enabled ? "The assistant is off for your government. An Administrator can turn it on under Government settings." : null;
}

/// <summary>The Government settings card: whether the operator has connected a model, and whether this government has it on.</summary>
/// <summary>The Administrator's two switches: the assistant for staff, and questions from the public on the portal.</summary>
public sealed record AssistantSettingsDto(bool Configured, bool Enabled, bool PortalQuestionsEnabled = false);

/// <summary>
/// The admin assistant. It answers from CivicBudget's own services run as the signed-in user, so it
/// can see exactly what the user can see and no more; it cannot change anything (ADR-0047).
/// </summary>
public interface IAssistantService
{
    Task<AssistantStatus> StatusAsync(CancellationToken ct = default);

    Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken ct = default);

    Task<AssistantSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Administrator only: lets this government's users use the assistant, or stops them.</summary>
    Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct = default);

    /// <summary>Turns the portal's question box on or off (Administrator only, audited); the portal's cached pages are dropped so its link appears or goes at once.</summary>
    Task<Result> SetPortalQuestionsEnabledAsync(bool enabled, CancellationToken ct = default);

    /// <summary>Runs a proposal the user clicked Confirm on: once, as the user, through the service a page would call, and into the audit trail.</summary>
    Task<Result<ProposalOutcome>> ConfirmAsync(Guid proposalId, CancellationToken ct = default);

    /// <summary>Drops a proposal the user turned down.</summary>
    void Discard(Guid proposalId);
}

/// <summary>
/// One question's working state, shared by the tools: the page the user is on (and the budget it
/// shows), the steps taken, and a page the user asked to open. A new one per question, so nothing
/// from one question leaks into the next.
/// </summary>
public sealed class AssistantTurn(string? currentPath)
{
    public string? CurrentPath { get; } = currentPath;

    /// <summary>The budget version the current page shows, from its address; null on pages about no one version.</summary>
    public Guid? PageVersionId { get; } = VersionFrom(currentPath);

    public List<AssistantStep> Steps { get; } = [];

    public List<AssistantProposalDto> Proposals { get; } = [];

    public string? NavigateTo { get; set; }

    private static Guid? VersionFrom(string? path)
    {
        if (path is null)
        {
            return null;
        }

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 2 < parts.Length; i++)
        {
            if (parts[i] == "admin" && parts[i + 1] is "budgets" or "reports" && Guid.TryParse(parts[i + 2], out Guid id))
            {
                return id;
            }
        }

        return null;
    }
}

/// <summary>
/// Contributes tools to a question. Application provides the budget and report tools; the Web
/// project adds the page tools, because pages and their permissions live there.
/// </summary>
public interface IAssistantToolProvider
{
    IEnumerable<AIFunction> Tools(AssistantTurn turn);
}
