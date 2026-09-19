namespace CivicBudget.Application.Security;

/// <summary>
/// The one place that says which roles carry the fiscal officer's powers. An Administrator has
/// complete access: everything the Fiscal Officer can do, plus users and settings. Services ask
/// this instead of testing role names, so "who may adopt a budget" is answered in one line.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>Administrator or Fiscal Officer: may enter any line, set balances, run the workflow, publish, import, and sync the chart.</summary>
    public static bool IsFiscalAuthority(this ICurrentUser user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.FinanceDirector);

    /// <summary>A department user: sees and edits only the departments assigned to them.</summary>
    public static bool IsDepartmentUser(this ICurrentUser user) => user.IsInRole(Roles.DepartmentHead);
}
