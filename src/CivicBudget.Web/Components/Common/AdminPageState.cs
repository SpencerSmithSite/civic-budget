namespace CivicBudget.Web.Components.Common;

public sealed record Crumb(string Text, string? Href = null);

/// <summary>
/// Lets a page tell the admin shell what to show in the top bar (breadcrumbs). Scoped, so it lives
/// for the circuit; the layout subscribes to <see cref="Changed"/> and re-renders when a page sets
/// its crumbs. Simpler than cascading parameters flowing upward, which Blazor does not support.
/// </summary>
public sealed class AdminPageState
{
    public IReadOnlyList<Crumb> Crumbs { get; private set; } = [];

    public event Action? Changed;

    public void SetCrumbs(params Crumb[] crumbs)
    {
        Crumbs = crumbs;
        Changed?.Invoke();
    }
}
