using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Domain.Publishing;

public enum SnapshotStatus
{
    /// <summary>What citizens see for this fiscal year right now.</summary>
    Active = 1,

    /// <summary>Replaced by a later publish of the same fiscal year (an amendment). Kept for history.</summary>
    Superseded = 2,

    /// <summary>Withdrawn by the Finance Director. Kept for history.</summary>
    Unpublished = 3,
}

/// <summary>
/// What the public portal shows: a frozen, denormalized copy of an adopted budget version taken at
/// the moment of publishing. It carries its own copies of fund, department, and account names, so
/// renaming an account next year does not rewrite what citizens saw this year. The portal reads only
/// these tables (through PublicPortalDbContext), never the live budget (ADR-0005, ADR-0006).
/// Immutable after capture except for its status.
/// </summary>
[Audited]
public sealed class PublishedBudgetSnapshot : Entity, ITenantOwned
{
    private readonly List<PublishedBudgetSnapshotLine> _lines = [];
    private readonly List<PublishedBudgetSnapshotFund> _funds = [];

    public Guid GovernmentId { get; private set; }

    /// <summary>Copied so the portal can resolve a URL slug without joining the live Governments table.</summary>
    public string GovernmentSlug { get; private set; }

    public string GovernmentName { get; private set; }
    public string? GovernmentDescription { get; private set; }

    public Guid BudgetVersionId { get; private set; }
    public int FiscalYear { get; private set; }
    public int VersionNumber { get; private set; }
    public string VersionLabel { get; private set; }
    public string? AmendmentReason { get; private set; }
    public string? ResolutionNumber { get; private set; }
    public DateTimeOffset? AdoptedOnUtc { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }
    public string PublishedByUserId { get; private set; }
    public string PublishedByUserName { get; private set; }

    public SnapshotStatus Status { get; private set; }
    public DateTimeOffset? StatusChangedAtUtc { get; private set; }
    public string? StatusChangedByUserId { get; private set; }

    public IReadOnlyCollection<PublishedBudgetSnapshotLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<PublishedBudgetSnapshotFund> Funds => _funds.AsReadOnly();

    public bool IsActive => Status == SnapshotStatus.Active;

    private PublishedBudgetSnapshot()
    {
        GovernmentSlug = null!;
        GovernmentName = null!;
        VersionLabel = null!;
        PublishedByUserId = null!;
        PublishedByUserName = null!;
    }

    /// <summary>
    /// Freezes an adopted version. Requires the version's lines with Fund, Department, and Account
    /// loaded, and its beginning balances; the publishing service guarantees that.
    /// </summary>
    public static PublishedBudgetSnapshot Capture(
        Government government,
        FiscalYear fiscalYear,
        BudgetVersion version,
        IReadOnlyCollection<Fund> funds,
        string userId,
        string userName,
        DateTimeOffset nowUtc)
    {
        Guard.Against(version.Status != BudgetStatus.Adopted, "Only an Adopted budget can be published.");
        Guard.Against(version.GovernmentId != government.Id, "Version belongs to a different government.");
        Guard.Against(fiscalYear.Id != version.FiscalYearId, "Fiscal year does not match the version.");

        var snapshot = new PublishedBudgetSnapshot
        {
            GovernmentId = government.Id,
            GovernmentSlug = government.PublicSlug,
            GovernmentName = government.Name,
            GovernmentDescription = government.Description,
            BudgetVersionId = version.Id,
            FiscalYear = fiscalYear.Year,
            VersionNumber = version.VersionNumber,
            VersionLabel = version.Label,
            AmendmentReason = version.AmendmentReason,
            ResolutionNumber = version.ResolutionNumber,
            AdoptedOnUtc = version.AdoptedOnUtc,
            PublishedAtUtc = nowUtc,
            PublishedByUserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId)),
            PublishedByUserName = Guard.NotNullOrWhiteSpace(userName, nameof(userName)),
            Status = SnapshotStatus.Active,
        };

        foreach (BudgetLine line in version.Lines)
        {
            snapshot._lines.Add(new PublishedBudgetSnapshotLine(snapshot.Id, government.Id, line));
        }

        // One fund row per fund that appears in the budget or has a beginning balance, so the portal
        // can show fund balances without the live Funds table.
        IEnumerable<Guid> fundIds = version.Lines.Select(l => l.FundId)
            .Concat(version.BeginningBalances.Select(b => b.FundId))
            .Distinct();
        foreach (Guid fundId in fundIds)
        {
            Fund fund = funds.FirstOrDefault(f => f.Id == fundId)
                ?? throw new DomainException($"Fund {fundId} was not supplied for the snapshot.");
            snapshot._funds.Add(new PublishedBudgetSnapshotFund(snapshot.Id, government.Id, fund, version.GetBeginningBalance(fundId)));
        }

        return snapshot;
    }

    public void Unpublish(string userId, DateTimeOffset nowUtc)
    {
        Guard.Against(Status != SnapshotStatus.Active, $"Only an Active snapshot can be unpublished (status: {Status}).");
        Status = SnapshotStatus.Unpublished;
        StatusChangedAtUtc = nowUtc;
        StatusChangedByUserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
    }

    /// <summary>Called on the previously active snapshot of the same fiscal year when a newer one is published.</summary>
    public void MarkSuperseded(PublishedBudgetSnapshot newer, string userId, DateTimeOffset nowUtc)
    {
        Guard.Against(Status != SnapshotStatus.Active, "Only an Active snapshot can be superseded.");
        Guard.Against(newer.FiscalYear != FiscalYear || newer.GovernmentId != GovernmentId, "Snapshots are for different budgets.");
        Guard.Against(newer.Id == Id, "A snapshot cannot supersede itself.");
        Status = SnapshotStatus.Superseded;
        StatusChangedAtUtc = nowUtc;
        StatusChangedByUserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
    }
}

/// <summary>A budget line as published: every code and name copied, nothing joined.</summary>
public sealed class PublishedBudgetSnapshotLine : Entity, ITenantOwned
{
    public Guid SnapshotId { get; private set; }
    public Guid GovernmentId { get; private set; }

    public string FundCode { get; private set; }
    public string FundName { get; private set; }
    public FundCategory FundCategory { get; private set; }

    public string? DepartmentCode { get; private set; }
    public string? DepartmentName { get; private set; }
    public string? DepartmentDescription { get; private set; }

    public string AccountCode { get; private set; }
    public string AccountName { get; private set; }
    public AccountType AccountType { get; private set; }
    public ReportingCategory Category { get; private set; }

    public decimal Amount { get; private set; }
    public decimal PriorYearActual { get; private set; }
    public decimal CurrentYearBudget { get; private set; }

    internal PublishedBudgetSnapshotLine(Guid snapshotId, Guid governmentId, BudgetLine line)
    {
        SnapshotId = snapshotId;
        GovernmentId = governmentId;
        FundCode = line.Fund.Code;
        FundName = line.Fund.Name;
        FundCategory = line.Fund.Category;
        DepartmentCode = line.Department?.Code;
        DepartmentName = line.Department?.Name;
        DepartmentDescription = line.Department?.Description;
        AccountCode = line.Account.Code;
        AccountName = line.Account.Name;
        AccountType = line.Account.Type;
        Category = line.Account.Category;
        Amount = line.Amount;
        PriorYearActual = line.PriorYearActual;
        CurrentYearBudget = line.CurrentYearBudget;
    }

    private PublishedBudgetSnapshotLine()
    {
        FundCode = null!;
        FundName = null!;
        AccountCode = null!;
        AccountName = null!;
    }
}

/// <summary>A fund as published, with its plain-language description and beginning balance.</summary>
public sealed class PublishedBudgetSnapshotFund : Entity, ITenantOwned
{
    public Guid SnapshotId { get; private set; }
    public Guid GovernmentId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public FundCategory Category { get; private set; }
    public string? Description { get; private set; }
    public decimal BeginningBalance { get; private set; }

    internal PublishedBudgetSnapshotFund(Guid snapshotId, Guid governmentId, Fund fund, decimal beginningBalance)
    {
        SnapshotId = snapshotId;
        GovernmentId = governmentId;
        Code = fund.Code;
        Name = fund.Name;
        Category = fund.Category;
        Description = fund.Description;
        BeginningBalance = beginningBalance;
    }

    private PublishedBudgetSnapshotFund()
    {
        Code = null!;
        Name = null!;
    }
}
