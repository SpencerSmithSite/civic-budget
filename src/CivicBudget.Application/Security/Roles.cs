namespace CivicBudget.Application.Security;

/// <summary>
/// The four roles from SPEC section 7.1. A user has exactly one role within their government.
/// <para>
/// The constants are the role names stored in the Identity tables, and they are older than the
/// labels people see. The app first said "Finance Director" and "Department Head"; Ohio villages and
/// townships say "Fiscal Officer", and a department's logon is often a clerk rather than the head,
/// so the screens now say "Fiscal Officer" and "Department User". Renaming the stored names would
/// mean migrating every user's role row for no change in behavior, so only <see cref="DisplayName"/>
/// changed.
/// </para>
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string FinanceDirector = "FinanceDirector";
    public const string DepartmentHead = "DepartmentHead";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, FinanceDirector, DepartmentHead, Viewer];

    /// <summary>Human-readable label for screens and reports.</summary>
    public static string DisplayName(string role) => role switch
    {
        Admin => "Administrator",
        FinanceDirector => "Fiscal Officer",
        DepartmentHead => "Department User",
        Viewer => "Viewer",
        _ => role,
    };
}
