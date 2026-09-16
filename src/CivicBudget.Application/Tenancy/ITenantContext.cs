namespace CivicBudget.Application.Tenancy;

/// <summary>
/// The government (tenant) the current operation is acting for.
/// <para>
/// Infrastructure binds every tenant-owned query to this value through EF Core global query filters,
/// and verifies writes against it. When <see cref="GovernmentId"/> is <c>null</c> (no signed-in user,
/// no portal slug resolved), tenant-owned queries return <b>no rows</b>. Failing closed is the point.
/// </para>
/// </summary>
public interface ITenantContext
{
    Guid? GovernmentId { get; }
}
