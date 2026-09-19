using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Erp;

/// <summary>
/// One sync of the chart from the ERP: who ran it, from what, and what it did. Append-only, like
/// the audit trail; the field-level changes to funds, departments, and accounts are recorded by
/// the audit interceptor as usual, so this row is the summary and <see cref="ChangesJson"/> the
/// detail the sync log shows.
/// </summary>
public sealed class ChartSync : Entity, ITenantOwned
{
    public const int NameMaxLength = 200;

    public Guid GovernmentId { get; private set; }
    public string SourceName { get; private set; }
    public string FileName { get; private set; }
    public DateTimeOffset SyncedAtUtc { get; private set; }
    public string UserId { get; private set; }
    public string UserName { get; private set; }
    public int Added { get; private set; }
    public int Updated { get; private set; }
    public int Deactivated { get; private set; }
    public int Reactivated { get; private set; }
    public int Unchanged { get; private set; }

    /// <summary>The preview's change list, serialized, for the drill-down.</summary>
    public string ChangesJson { get; private set; }

    public ChartSync(Guid governmentId, string sourceName, string fileName, DateTimeOffset syncedAtUtc, string userId, string userName,
        int added, int updated, int deactivated, int reactivated, int unchanged, string changesJson)
    {
        GovernmentId = governmentId;
        SourceName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName)), NameMaxLength, nameof(sourceName));
        FileName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(fileName, nameof(fileName)), NameMaxLength, nameof(fileName));
        SyncedAtUtc = syncedAtUtc;
        UserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
        UserName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(userName, nameof(userName)), NameMaxLength, nameof(userName));
        Added = added;
        Updated = updated;
        Deactivated = deactivated;
        Reactivated = reactivated;
        Unchanged = unchanged;
        ChangesJson = changesJson;
    }

    private ChartSync()
    {
        SourceName = null!;
        FileName = null!;
        UserId = null!;
        UserName = null!;
        ChangesJson = null!;
    }
}
