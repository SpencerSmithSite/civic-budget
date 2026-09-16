namespace CivicBudget.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one government (tenant).
/// Infrastructure applies a global query filter to every type that implements this,
/// and a save interceptor stamps/verifies <see cref="GovernmentId"/> on write.
/// </summary>
public interface ITenantOwned
{
    Guid GovernmentId { get; }
}
