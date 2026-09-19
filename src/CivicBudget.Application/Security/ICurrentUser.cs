namespace CivicBudget.Application.Security;

/// <summary>
/// The signed-in user as the Application layer sees it: identity, tenant, role, and departments.
/// Read from claims, so it is cheap and needs no database call. Infrastructure supplies it; in the
/// admin app it is filled from the circuit's authentication state, in plain HTTP requests from
/// HttpContext.User.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Identity user id (a string, per ASP.NET Core Identity). Null when anonymous.</summary>
    string? UserId { get; }

    string? DisplayName { get; }

    Guid? GovernmentId { get; }

    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Departments a department user may edit. Empty for other roles.</summary>
    IReadOnlyCollection<Guid> DepartmentIds { get; }

    bool IsInRole(string role);
}
