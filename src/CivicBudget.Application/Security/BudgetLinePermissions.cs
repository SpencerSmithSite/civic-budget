using CivicBudget.Domain.Budgets;

namespace CivicBudget.Application.Security;

/// <summary>
/// The one rule for "may this user edit this budget line", written once as a pure function so the
/// Blazor authorization handler, the application services, and the unit tests all call the same code.
/// <list type="bullet">
/// <item>Administrator or Fiscal Officer: any line while the version is Draft or Proposed.</item>
/// <item>Department user: only lines in their assigned departments, and only while Draft.</item>
/// <item>Everyone else: no. Adopted versions: no one.</item>
/// </list>
/// </summary>
public static class BudgetLinePermissions
{
    public static bool CanEdit(ICurrentUser user, BudgetStatus versionStatus, Guid? lineDepartmentId)
    {
        if (versionStatus == BudgetStatus.Adopted)
        {
            return false;
        }

        if (user.IsFiscalAuthority())
        {
            return true;
        }

        if (user.IsDepartmentUser())
        {
            return versionStatus == BudgetStatus.Draft
                && lineDepartmentId is { } departmentId
                && user.DepartmentIds.Contains(departmentId);
        }

        return false;
    }

    /// <summary>department users may create lines only in their departments; the FD anywhere.</summary>
    public static bool CanAddLine(ICurrentUser user, BudgetStatus versionStatus, Guid? departmentId) =>
        CanEdit(user, versionStatus, departmentId);

    public static bool CanEditBeginningBalances(ICurrentUser user, BudgetStatus versionStatus) =>
        versionStatus != BudgetStatus.Adopted && user.IsFiscalAuthority();
}
