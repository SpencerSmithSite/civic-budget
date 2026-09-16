using CivicBudget.Application.Tenancy;

namespace CivicBudget.Infrastructure.Tenancy;

/// <summary>
/// A settable tenant context, registered as scoped. Whoever knows the tenant for the current scope
/// (the sign-in claims factory in the admin app, the slug resolver in the portal, the seeder, a test)
/// calls <see cref="Set"/> once; everything downstream reads <see cref="GovernmentId"/>.
/// </summary>
public sealed class AmbientTenantContext : ITenantContext
{
    public Guid? GovernmentId { get; private set; }

    public void Set(Guid governmentId) => GovernmentId = governmentId;

    public void Clear() => GovernmentId = null;
}
