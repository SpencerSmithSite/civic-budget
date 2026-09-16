using System.Security.Claims;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;

namespace CivicBudget.Infrastructure.Security;

/// <summary>
/// Holds "who is acting" for the current DI scope and answers both <see cref="ICurrentUser"/> and
/// <see cref="ITenantContext"/> from it. Registered as scoped, so there is one per HTTP request and
/// one per Blazor circuit.
/// <para>
/// Who fills it in depends on where the scope came from:
/// the Web project's CurrentUserMiddleware (HTTP requests, including static SSR pages),
/// its CurrentUserCircuitHandler (Interactive Server circuits),
/// the public portal's slug resolver (Phase 5, tenant only, no user),
/// and the seeder and tests, which call <see cref="SetTenant"/> directly.
/// </para>
/// </summary>
public sealed class CurrentUserContext : ICurrentUser, ITenantContext
{
    private ClaimsPrincipalUser _user = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private Guid? _tenantOverride;

    public void SetPrincipal(ClaimsPrincipal principal) => _user = new ClaimsPrincipalUser(principal);

    /// <summary>Acts for a government without a signed-in user (seeding, portal, tests).</summary>
    public void SetTenant(Guid governmentId) => _tenantOverride = governmentId;

    public void Clear()
    {
        _user = new ClaimsPrincipalUser(new ClaimsPrincipal(new ClaimsIdentity()));
        _tenantOverride = null;
    }

    public bool IsAuthenticated => _user.IsAuthenticated;
    public string? UserId => _user.UserId;
    public string? DisplayName => _user.DisplayName;

    /// <summary>An explicit tenant wins; otherwise the signed-in user's government claim; otherwise null (no rows).</summary>
    public Guid? GovernmentId => _tenantOverride ?? _user.GovernmentId;

    public IReadOnlyCollection<string> Roles => _user.Roles;
    public IReadOnlyCollection<Guid> DepartmentIds => _user.DepartmentIds;
    public bool IsInRole(string role) => _user.IsInRole(role);
}
