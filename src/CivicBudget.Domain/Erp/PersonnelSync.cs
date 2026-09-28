using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Erp;

/// <summary>
/// One time a budget's positions were brought up to date from the ERP's employee list: which budget,
/// from where, by whom, and what it changed. The positions and lines themselves are audited field by
/// field; this is the log the sync page shows, like <see cref="ActualsSync"/>.
/// </summary>
public sealed class PersonnelSync : Entity, ITenantOwned
{
    public const int SourceMaxLength = 100;
    public const int FileNameMaxLength = 260;

    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public DateOnly AsOf { get; private set; }
    public string SourceName { get; private set; } = null!;
    public string? FileName { get; private set; }
    public DateTimeOffset SyncedAtUtc { get; private set; }
    public string UserId { get; private set; } = null!;
    public string UserName { get; private set; } = null!;
    public int Employees { get; private set; }
    public int Added { get; private set; }
    public int Filled { get; private set; }
    public int Updated { get; private set; }
    public int Vacated { get; private set; }
    public int LinesChanged { get; private set; }

    public PersonnelSync(Guid governmentId, Guid budgetVersionId, DateOnly asOf, string sourceName, string? fileName,
        string userId, string userName, DateTimeOffset syncedAtUtc, int employees, int added, int filled, int updated, int vacated, int linesChanged)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        AsOf = asOf;
        SourceName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName)), SourceMaxLength, nameof(sourceName));
        FileName = fileName is null ? null : Guard.MaxLength(fileName, FileNameMaxLength, nameof(fileName));
        UserId = userId;
        UserName = userName;
        SyncedAtUtc = syncedAtUtc;
        (Employees, Added, Filled, Updated, Vacated, LinesChanged) = (employees, added, filled, updated, vacated, linesChanged);
    }

    private PersonnelSync()
    {
    }
}
