using Microsoft.JSInterop;

namespace CivicBudget.Web.Components.Common;

/// <summary>
/// Calls the focus helpers in App.razor. Focus is a courtesy, never a reason to fail: if the circuit
/// is going away or the script is not on the page, the dialog still opens and closes.
/// </summary>
internal static class FocusJs
{
    public static async Task TryAsync(IJSRuntime js, string function)
    {
        try
        {
            await js.InvokeVoidAsync(function);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // Nothing to do: the next Tab still works, it just starts from the top.
        }
    }
}
