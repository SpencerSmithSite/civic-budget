namespace CivicBudget.Application.Security;

/// <summary>
/// Named authorization policies. Pages and services refer to these names; the mapping from name to
/// roles lives in one place in the Web project (AuthorizationPolicies) and is covered by tests.
/// Using policies instead of role names at call sites means "who may publish" can change without
/// touching every page that publishes.
/// </summary>
public static class Policies
{
    /// <summary>Manage users and government settings. Admin.</summary>
    public const string CanManageUsers = nameof(CanManageUsers);

    /// <summary>Maintain funds, departments, accounts, fiscal years. Admin, Finance Director.</summary>
    public const string CanMaintainSetup = nameof(CanMaintainSetup);

    /// <summary>See budget versions and reports. Every role (department heads see their departments).</summary>
    public const string CanViewBudget = nameof(CanViewBudget);

    /// <summary>Enter beginning fund balances. Finance Director.</summary>
    public const string CanEditBeginningBalances = nameof(CanEditBeginningBalances);

    /// <summary>Propose, return, adopt, and start amendments. Finance Director.</summary>
    public const string CanAdvanceWorkflow = nameof(CanAdvanceWorkflow);

    /// <summary>Publish and unpublish snapshots to the public portal. Finance Director.</summary>
    public const string CanPublish = nameof(CanPublish);

    /// <summary>Import budget lines from files. Finance Director.</summary>
    public const string CanImport = nameof(CanImport);

    /// <summary>See the audit trail. Admin, Finance Director, Department Head.</summary>
    public const string CanViewAudit = nameof(CanViewAudit);

    /// <summary>Edit a specific budget line. Resource-based: depends on the line and the version status.</summary>
    public const string CanEditBudgetLine = nameof(CanEditBudgetLine);
}
