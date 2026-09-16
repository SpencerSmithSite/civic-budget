namespace CivicBudget.Application.Security;

/// <summary>
/// Custom claims added at sign-in. Short, lowercase names in the JWT style so they look the same
/// whether they come from a cookie today or an OpenID Connect provider later.
/// </summary>
public static class ClaimNames
{
    /// <summary>The user's government (tenant). Exactly one per user.</summary>
    public const string GovernmentId = "government_id";

    /// <summary>One claim per department the user may edit. Only Department Heads have these.</summary>
    public const string DepartmentId = "department_id";

    public const string DisplayName = "display_name";
}
