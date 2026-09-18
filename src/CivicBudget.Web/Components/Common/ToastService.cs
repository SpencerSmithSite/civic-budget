namespace CivicBudget.Web.Components.Common;

public enum ToastKind
{
    Success,
    Warning,
    Danger,
}

public sealed record ToastMessage(Guid Id, ToastKind Kind, string Text);

/// <summary>
/// Success and error feedback that does not push the page around. Scoped per circuit; the
/// <c>ToastHost</c> in the admin layout renders whatever is queued and removes each toast after a
/// few seconds. Validation errors stay inline next to their fields; this is for outcomes.
/// </summary>
public sealed class ToastService
{
    private readonly List<ToastMessage> _toasts = [];

    public IReadOnlyList<ToastMessage> Toasts => _toasts;

    public event Action? Changed;

    public void Success(string text) => Add(ToastKind.Success, text);

    public void Warning(string text) => Add(ToastKind.Warning, text);

    public void Danger(string text) => Add(ToastKind.Danger, text);

    public void Remove(Guid id)
    {
        _toasts.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private void Add(ToastKind kind, string text)
    {
        _toasts.Add(new ToastMessage(Guid.NewGuid(), kind, text));
        Changed?.Invoke();
    }
}
