namespace CivicBudget.Web.Components.Admin.Personnel;

/// <summary>Where a department's positions live, so every link to them is written once.</summary>
public static class PersonnelLinks
{
    public static string For(Guid versionId, Guid? departmentId) =>
        departmentId is { } department ? $"admin/budgets/{versionId}/personnel/{department}" : $"admin/budgets/{versionId}";
}
