using System.Security.Claims;
using CivicBudget.Application.Security;

namespace CivicBudget.Infrastructure.Security;

/// <summary>
/// Reads <see cref="ICurrentUser"/> straight off a <see cref="ClaimsPrincipal"/>. Used by
/// <see cref="CurrentUserContext"/> for the ambient user and by the authorization handler, which is
/// handed a principal by ASP.NET Core and must evaluate the same permission rule.
/// </summary>
public sealed class ClaimsPrincipalUser(ClaimsPrincipal principal) : ICurrentUser
{
    public bool IsAuthenticated => principal.Identity?.IsAuthenticated == true;

    public string? UserId => principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? DisplayName => principal.FindFirstValue(ClaimNames.DisplayName) ?? principal.Identity?.Name;

    public Guid? GovernmentId =>
        Guid.TryParse(principal.FindFirstValue(ClaimNames.GovernmentId), out Guid id) ? id : null;

    public IReadOnlyCollection<string> Roles =>
        principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    public IReadOnlyCollection<Guid> DepartmentIds =>
        principal.FindAll(ClaimNames.DepartmentId)
            .Select(c => Guid.TryParse(c.Value, out Guid id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

    public bool IsInRole(string role) => principal.IsInRole(role);
}
