using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Auditing;

/// <summary>What kind of history line an <see cref="AuditEntry"/> is.</summary>
public enum AuditKind
{
    /// <summary>A row was inserted.</summary>
    Created = 1,

    /// <summary>One property changed value. One entry per property.</summary>
    FieldChanged = 2,

    /// <summary>A row was deleted.</summary>
    Deleted = 3,

    /// <summary>A named business action (for example "Adopted"), recorded explicitly by a service.</summary>
    Event = 4,
}

/// <summary>
/// One line of the audit trail. Field-level entries are written automatically by the save
/// interceptor for entities marked <see cref="AuditedAttribute"/>; event entries are written by
/// services when a field diff alone would not explain what happened ("Proposed", "Published").
/// Append-only: there is no update path and the row is never edited.
/// </summary>
public sealed class AuditEntry : Entity, ITenantOwned
{
    public const int EntityNameMaxLength = 100;
    public const int PropertyNameMaxLength = 100;
    public const int ValueMaxLength = 500;
    public const int UserIdMaxLength = 450;
    public const int UserNameMaxLength = 256;
    public const int DescriptionMaxLength = 1000;

    public Guid GovernmentId { get; private set; }

    /// <summary>CLR type name of the entity, e.g. "BudgetLine".</summary>
    public string EntityName { get; private set; }

    public Guid EntityId { get; private set; }
    public AuditKind Kind { get; private set; }

    /// <summary>Set for <see cref="AuditKind.FieldChanged"/>; null otherwise.</summary>
    public string? PropertyName { get; private set; }

    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }

    /// <summary>For <see cref="AuditKind.Event"/>: what happened, in words.</summary>
    public string? Description { get; private set; }

    /// <summary>Identity user id, or "system" for seeding and migrations.</summary>
    public string UserId { get; private set; }

    public string UserName { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    private AuditEntry(
        Guid governmentId,
        string entityName,
        Guid entityId,
        AuditKind kind,
        string? propertyName,
        string? oldValue,
        string? newValue,
        string? description,
        string userId,
        string userName,
        DateTimeOffset timestampUtc)
    {
        GovernmentId = governmentId;
        EntityName = Guard.MaxLength(entityName, EntityNameMaxLength, nameof(entityName));
        EntityId = entityId;
        Kind = kind;
        PropertyName = propertyName;
        OldValue = Truncate(oldValue, ValueMaxLength);
        NewValue = Truncate(newValue, ValueMaxLength);
        // An event's text often quotes what a user typed (a return note, an amendment reason), which
        // can be as long as the field allows; a history line that fails to save would take the
        // user's action down with it, so it is cut like a value instead of rejected.
        Description = Truncate(description, DescriptionMaxLength);
        UserId = Guard.MaxLength(userId, UserIdMaxLength, nameof(userId));
        UserName = Guard.MaxLength(userName, UserNameMaxLength, nameof(userName));
        TimestampUtc = timestampUtc;
    }

    private AuditEntry()
    {
        EntityName = null!;
        UserId = null!;
        UserName = null!;
    }

    public static AuditEntry Created(Guid governmentId, string entityName, Guid entityId, string userId, string userName, DateTimeOffset nowUtc) =>
        new(governmentId, entityName, entityId, AuditKind.Created, null, null, null, null, userId, userName, nowUtc);

    public static AuditEntry FieldChanged(
        Guid governmentId, string entityName, Guid entityId, string propertyName, string? oldValue, string? newValue,
        string userId, string userName, DateTimeOffset nowUtc) =>
        new(governmentId, entityName, entityId, AuditKind.FieldChanged, propertyName, oldValue, newValue, null, userId, userName, nowUtc);

    public static AuditEntry Deleted(Guid governmentId, string entityName, Guid entityId, string userId, string userName, DateTimeOffset nowUtc) =>
        new(governmentId, entityName, entityId, AuditKind.Deleted, null, null, null, null, userId, userName, nowUtc);

    public static AuditEntry Event(Guid governmentId, string entityName, Guid entityId, string description, string userId, string userName, DateTimeOffset nowUtc) =>
        new(governmentId, entityName, entityId, AuditKind.Event, null, null, null, description, userId, userName, nowUtc);

    /// <summary>Values and descriptions are for humans reading history, not for replay; long text is cut rather than rejected.</summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is { } text && text.Length > maxLength ? text[..(maxLength - 1)] + "…" : value;
}
