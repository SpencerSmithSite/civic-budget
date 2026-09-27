using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Erp;

/// <summary>
/// One sync of a fiscal year's actuals from the ERP: which year, how far into it the ERP's books
/// ran, where the figures came from, who ran it, and the totals it brought. Append-only. The latest
/// row for a year is the one whose figures are in <see cref="ErpActual"/> and its siblings.
/// </summary>
public sealed class ActualsSync : Entity, ITenantOwned
{
    public const int NameMaxLength = 200;

    public Guid GovernmentId { get; private set; }
    public int FiscalYear { get; private set; }

    /// <summary>The last fiscal month the figures include; 12 means the year is closed.</summary>
    public int ThroughPeriod { get; private set; }

    /// <summary>The last day the figures cover: the end of <see cref="ThroughPeriod"/>.</summary>
    public DateOnly AsOf { get; private set; }

    public string SourceName { get; private set; }

    /// <summary>The uploaded file's name, or null when the figures came straight from the ERP's API.</summary>
    public string? FileName { get; private set; }

    public DateTimeOffset SyncedAtUtc { get; private set; }
    public string UserId { get; private set; }
    public string UserName { get; private set; }

    /// <summary>How many account-and-month amounts arrived.</summary>
    public int ActivityRows { get; private set; }

    /// <summary>Revenue and transfers in: the cash-basis "receipts".</summary>
    public decimal Receipts { get; private set; }

    /// <summary>Expenditures and transfers out: the cash-basis "disbursements".</summary>
    public decimal Disbursements { get; private set; }

    public decimal Encumbered { get; private set; }

    public decimal Cash { get; private set; }

    /// <summary>Budget lines whose prior-year actual this sync changed, across every open version it reaches.</summary>
    public int PriorActualsUpdated { get; private set; }

    public bool IsYearClosed => ThroughPeriod == 12;

    public ActualsSync(Guid governmentId, int fiscalYear, int throughPeriod, DateOnly asOf, string sourceName, string? fileName, DateTimeOffset syncedAtUtc,
        string userId, string userName, int activityRows, decimal receipts, decimal disbursements, decimal encumbered, decimal cash, int priorActualsUpdated)
    {
        GovernmentId = governmentId;
        Guard.Against(throughPeriod is < 1 or > 12, "A sync runs through a fiscal month from 1 to 12.");
        FiscalYear = fiscalYear;
        ThroughPeriod = throughPeriod;
        AsOf = asOf;
        SourceName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName)), NameMaxLength, nameof(sourceName));
        FileName = fileName is null ? null : Guard.MaxLength(fileName, NameMaxLength, nameof(fileName));
        SyncedAtUtc = syncedAtUtc;
        UserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
        UserName = Guard.MaxLength(Guard.NotNullOrWhiteSpace(userName, nameof(userName)), NameMaxLength, nameof(userName));
        ActivityRows = activityRows;
        Receipts = Money.Round(receipts);
        Disbursements = Money.Round(disbursements);
        Encumbered = Money.Round(encumbered);
        Cash = Money.Round(cash);
        PriorActualsUpdated = priorActualsUpdated;
    }

    private ActualsSync()
    {
        SourceName = null!;
        UserId = null!;
        UserName = null!;
    }
}
