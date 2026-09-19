using CivicBudget.Domain.Budgets;

namespace CivicBudget.Application.Security;

/// <summary>
/// The one rule for "may this user edit this budget line", written once as a pure function so the
/// Blazor authorization handler, the application services, and the unit tests all call the same code.
/// <list type="bullet">
/// <item>Administrator or Fiscal Officer: any line while the version is Draft or Proposed.</item>
/// <item>Department user: only lines in their assigned departments, only while Draft, and only until
/// the department submits its request (a returned request reopens it).</item>
/// <item>Everyone else: no. Adopted versions: no one.</item>
/// </list>
/// </summary>
public static class BudgetLinePermissions
{
    public static bool CanEdit(ICurrentUser user, BudgetStatus versionStatus, Guid? lineDepartmentId, bool departmentSubmitted = false)
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
                && !departmentSubmitted
                && lineDepartmentId is { } departmentId
                && user.DepartmentIds.Contains(departmentId);
        }

        return false;
    }

    /// <summary>Department users may create lines only in their departments (and not after submitting); the fiscal authority anywhere.</summary>
    public static bool CanAddLine(ICurrentUser user, BudgetStatus versionStatus, Guid? departmentId, bool departmentSubmitted = false) =>
        CanEdit(user, versionStatus, departmentId, departmentSubmitted);

    /// <summary>The narrative follows the same rule as the department's lines.</summary>
    public static bool CanEditNarrative(ICurrentUser user, BudgetStatus versionStatus, Guid departmentId, bool departmentSubmitted) =>
        CanEdit(user, versionStatus, departmentId, departmentSubmitted);

    /// <summary>
    /// Submitting is the department's act (or the fiscal authority's, on its behalf): Draft only,
    /// and not twice. Returning is the fiscal authority's alone, and only of a submitted request.
    /// </summary>
    public static bool CanSubmitDepartment(ICurrentUser user, BudgetStatus versionStatus, Guid departmentId, DepartmentRequestStatus requestStatus) =>
        versionStatus == BudgetStatus.Draft
        && requestStatus != DepartmentRequestStatus.Submitted
        && (user.IsFiscalAuthority() || (user.IsDepartmentUser() && user.DepartmentIds.Contains(departmentId)));

    public static bool CanReturnDepartment(ICurrentUser user, BudgetStatus versionStatus, DepartmentRequestStatus requestStatus) =>
        versionStatus == BudgetStatus.Draft
        && requestStatus == DepartmentRequestStatus.Submitted
        && user.IsFiscalAuthority();

    public static bool CanEditBeginningBalances(ICurrentUser user, BudgetStatus versionStatus) =>
        versionStatus != BudgetStatus.Adopted && user.IsFiscalAuthority();
}
