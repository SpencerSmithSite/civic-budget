using CivicBudget.Application.Assistant;

namespace CivicBudget.Web.Components.Assistant;

/// <summary>
/// The assistant's state for one browser tab: open or closed, and the conversation. It lives for the
/// tab's connection (scoped to the circuit), so the conversation survives moving between pages,
/// including the pages the assistant opens, and is gone when the tab closes. Nothing is stored.
/// </summary>
public sealed class AssistantPanelState
{
    public bool IsOpen { get; private set; }

    public AssistantStatus? Status { get; set; }

    public List<AssistantEntry> Conversation { get; } = [];

    /// <summary>Raised when the panel opens or closes, so the button and the panel re-render together.</summary>
    public event Action? Changed;

    /// <summary>Set when the panel was closed from inside it, so the button takes focus back (WCAG 2.4.3).</summary>
    public bool ReturnFocusToButton { get; set; }

    public void Toggle() => SetOpen(!IsOpen);

    public void CloseFromPanel()
    {
        ReturnFocusToButton = true;
        SetOpen(false);
    }

    public void SetOpen(bool open)
    {
        IsOpen = open;
        Changed?.Invoke();
    }

    /// <summary>
    /// Counts the changes confirmed in this tab. The admin layout keys the page on it, so the page
    /// under the panel is rebuilt and loads the change instead of showing what it had before.
    /// </summary>
    public int PageGeneration { get; private set; }

    public void ReloadPage()
    {
        PageGeneration++;
        Changed?.Invoke();
    }

    /// <summary>After the Administrator turns the assistant on or off, so the button and panel agree at once.</summary>
    public void Refresh(AssistantStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}

/// <summary>A message as the panel shows it: the text, the steps behind an answer, any changes proposed with it, and whether it is an error.</summary>
public sealed record AssistantEntry(bool FromUser, string Text, IReadOnlyList<AssistantStep> Steps, bool IsError = false)
{
    public IReadOnlyList<ProposalView> Proposals { get; init; } = [];

    /// <summary>
    /// The answer as it goes back to the model with the next question, with what the user did with
    /// each proposal, so "now do the same for Police" follows from what really happened.
    /// </summary>
    public string HistoryText => Proposals.Count == 0 ? Text : Text + "\n\n" + string.Join("\n", Proposals.Select(p => p.HistoryNote));
}

public enum ProposalStage
{
    Waiting,
    Working,
    Done,
    Failed,
    Cancelled,
}

/// <summary>A proposal on the panel and what has happened to it; it lives with the conversation.</summary>
public sealed class ProposalView(AssistantProposalDto proposal)
{
    public AssistantProposalDto Proposal { get; } = proposal;

    public ProposalStage Stage { get; set; } = ProposalStage.Waiting;

    /// <summary>The outcome's sentence, or why it failed.</summary>
    public string? Message { get; set; }

    public string? Link { get; set; }

    public string HistoryNote => Stage switch
    {
        ProposalStage.Done => $"[The user confirmed \"{Proposal.Title}\": {Message}]",
        ProposalStage.Failed => $"[The user confirmed \"{Proposal.Title}\" but it failed: {Message}]",
        ProposalStage.Cancelled => $"[The user cancelled \"{Proposal.Title}\". Nothing changed.]",
        _ => $"[\"{Proposal.Title}\" is waiting for the user to confirm. Nothing has changed yet.]",
    };
}
