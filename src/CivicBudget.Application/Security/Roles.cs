namespace CivicBudget.Application.Security;

/// <summary>
/// The four roles from SPEC section 7.1. A user has exactly one role within their government.
/// Role names are also the ASP.NET Core Identity role names, so they never change once deployed.
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
