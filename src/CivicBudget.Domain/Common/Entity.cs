namespace CivicBudget.Domain.Common;

/// <summary>
/// Base class for every persisted entity. Ids are version-7 GUIDs: globally unique like any GUID,
/// but time-ordered, so SQL Server clustered indexes don't fragment the way random GUIDs cause.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
