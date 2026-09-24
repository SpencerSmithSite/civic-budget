namespace CivicBudget.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one government (tenant). Infrastructure adds a global
/// query filter to every type that implements this, so one government never reads another's rows,
/// and a save interceptor refuses any write whose <see cref="GovernmentId"/> is not the signed-in
/// user's government. Entities take the id in their constructor, so it is never filled in later.
/// </summary>
public interface ITenantOwned
{
    Guid GovernmentId { get; }
}
