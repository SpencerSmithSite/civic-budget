using CivicBudget.Application.Common;
using Microsoft.AspNetCore.Components.Forms;

namespace CivicBudget.Web.Components.Common;

/// <summary>
/// Connects a service <see cref="Result"/> to Blazor's form validation UI. Field errors (those with a
/// property name) go into a <see cref="ValidationMessageStore"/> so <c>&lt;ValidationMessage For&gt;</c>
/// shows them next to the input; form-level errors are surfaced by <c>ResultAlert</c>.
/// This is how FluentValidation in the Application layer reaches the screen without the components
/// knowing FluentValidation exists.
/// </summary>
public static class EditContextResultExtensions
{
    public static void ApplyErrors(this EditContext editContext, ValidationMessageStore store, Result result)
    {
        store.Clear();
        foreach (ValidationError error in result.Errors.Where(e => !string.IsNullOrEmpty(e.PropertyName)))
        {
            store.Add(editContext.Field(error.PropertyName), error.Message);
        }

        editContext.NotifyValidationStateChanged();
    }
}
