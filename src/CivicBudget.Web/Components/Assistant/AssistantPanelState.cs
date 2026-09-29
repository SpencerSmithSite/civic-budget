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

    /// <summary>After the Administrator turns the assistant on or off, so the button and panel agree at once.</summary>
    public void Refresh(AssistantStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}

/// <summary>A message as the panel shows it: the text, the steps behind an answer, and whether it is an error.</summary>
public sealed record AssistantEntry(bool FromUser, string Text, IReadOnlyList<AssistantStep> Steps, bool IsError = false);
