using CivicBudget.Application.Security;

namespace CivicBudget.Application.Tests.Security;

/// <summary>Hand-built ICurrentUser for rule tests: no claims, no Identity, just the facts the rule needs.</summary>
internal sealed class FakeUser(string? role, params Guid[] departmentIds) : ICurrentUser
{
    public bool IsAuthenticated => role is not null;
    public string? UserId => role is null ? null : "user-" + role;
    public string? DisplayName => role;
    public Guid? GovernmentId => Guid.Empty;
    public IReadOnlyCollection<string> Roles => role is null ? [] : [role];
    public IReadOnlyCollection<Guid> DepartmentIds => departmentIds;
    public bool IsInRole(string r) => role == r;
}
