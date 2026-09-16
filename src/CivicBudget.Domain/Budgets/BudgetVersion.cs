using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Budgets;

/// <summary>
/// Aggregate root for one version of a fiscal year's budget: the original (version 1) or an
/// amendment (2, 3, …). All edits to lines and beginning balances go through this class so the
/// "adopted versions are immutable" rule lives in exactly one place.
/// </summary>
public sealed class BudgetVersion : Entity, ITenantOwned
{
    public const int ReasonMaxLength = 1000;
    public const int ResolutionNumberMaxLength = 50;

    private readonly List<BudgetLine> _lines = [];
    private readonly List<FundBeginningBalance> _beginningBalances = [];

    public Guid GovernmentId { get; private set; }
    public Guid FiscalYearId { get; private set; }

    /// <summary>1 = original budget; 2+ = amendments.</summary>
    public int VersionNumber { get; private set; }

    public BudgetStatus Status { get; private set; }

    /// <summary>Why this amendment exists. Required for amendments, null for the original.</summary>
    public string? AmendmentReason { get; private set; }

    /// <summary>Ordinance/resolution number recorded at adoption.</summary>
    public string? ResolutionNumber { get; private set; }

    public DateTimeOffset? AdoptedOnUtc { get; private set; }
    public string? AdoptedByUserId { get; private set; }

    /// <summary>Set on an adopted version when a later amendment is adopted in its place.</summary>
    public Guid? SupersededByVersionId { get; private set; }

    public IReadOnlyCollection<BudgetLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<FundBeginningBalance> BeginningBalances => _beginningBalances.AsReadOnly();

    public bool IsAmendment => VersionNumber > 1;
    public bool IsEditable => Status != BudgetStatus.Adopted;
    public string Label => IsAmendment ? $"Amendment {VersionNumber - 1}" : "Original";

    private BudgetVersion(Guid governmentId, Guid fiscalYearId, int versionNumber, string? amendmentReason)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        Guard.Against(fiscalYearId == Guid.Empty, "FiscalYearId is required.");
        GovernmentId = governmentId;
        FiscalYearId = fiscalYearId;
        VersionNumber = versionNumber;
        Status = BudgetStatus.Draft;
        AmendmentReason = amendmentReason;
    }

    private BudgetVersion()
    {
    }

    public static BudgetVersion CreateOriginal(Guid governmentId, Guid fiscalYearId) =>
        new(governmentId, fiscalYearId, versionNumber: 1, amendmentReason: null);

    /// <summary>
    /// Ohio rule of thumb encoded as a domain rule: a fiscal year has at most one budget in progress.
    /// Call with the year's existing versions before creating a new one.
    /// </summary>
    public static void EnsureNoOpenVersion(IEnumerable<BudgetVersion> existingVersionsForYear)
    {
        BudgetVersion? open = existingVersionsForYear.FirstOrDefault(v => v.Status != BudgetStatus.Adopted);
        Guard.Against(
            open is not null,
            $"Version {open?.VersionNumber} ({open?.Label}) is still {open?.Status}; adopt it before starting another.");
    }

    // ---- Workflow -------------------------------------------------------------------------

    public void Propose()
    {
        Guard.Against(Status != BudgetStatus.Draft, $"Only a Draft budget can be proposed (current status: {Status}).");
        Status = BudgetStatus.Proposed;
    }

    public void ReturnToDraft()
    {
        Guard.Against(Status != BudgetStatus.Proposed, $"Only a Proposed budget can be returned to Draft (current status: {Status}).");
        Status = BudgetStatus.Draft;
    }

    public void Adopt(string resolutionNumber, string adoptedByUserId, DateTimeOffset nowUtc)
    {
        Guard.Against(Status != BudgetStatus.Proposed, $"Only a Proposed budget can be adopted (current status: {Status}).");
        ResolutionNumber = Guard.MaxLength(
            Guard.NotNullOrWhiteSpace(resolutionNumber, nameof(resolutionNumber)),
            ResolutionNumberMaxLength,
            nameof(resolutionNumber));
        AdoptedByUserId = Guard.NotNullOrWhiteSpace(adoptedByUserId, nameof(adoptedByUserId));
        AdoptedOnUtc = nowUtc;
        Status = BudgetStatus.Adopted;
    }

    /// <summary>
    /// Starts an amendment: a new Draft with the next version number containing copies of this
    /// version's lines and beginning balances. This version is left untouched.
    /// </summary>
    public BudgetVersion CreateAmendment(string reason)
    {
        Guard.Against(Status != BudgetStatus.Adopted, "Only an Adopted budget can be amended.");
        string trimmedReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason, nameof(reason)), ReasonMaxLength, nameof(reason));

        var amendment = new BudgetVersion(GovernmentId, FiscalYearId, VersionNumber + 1, trimmedReason);
        amendment._lines.AddRange(_lines.Select(l => l.CopyTo(amendment.Id)));
        amendment._beginningBalances.AddRange(_beginningBalances.Select(b => b.CopyTo(amendment.Id)));
        return amendment;
    }

    /// <summary>Called on the previously adopted version when an amendment is adopted.</summary>
    public void MarkSupersededBy(BudgetVersion amendment)
    {
        Guard.Against(Status != BudgetStatus.Adopted, "Only an Adopted budget can be superseded.");
        Guard.Against(amendment.Status != BudgetStatus.Adopted, "A budget can only be superseded by an Adopted amendment.");
        Guard.Against(amendment.FiscalYearId != FiscalYearId, "The amendment belongs to a different fiscal year.");
        Guard.Against(amendment.VersionNumber <= VersionNumber, "A budget can only be superseded by a later version.");
        SupersededByVersionId = amendment.Id;
    }

    // ---- Lines ----------------------------------------------------------------------------

    public BudgetLine AddLine(
        Fund fund,
        Department? department,
        Account account,
        decimal amount,
        decimal priorYearActual = 0m,
        decimal currentYearBudget = 0m,
        string? justification = null)
    {
        EnsureEditable();
        Guard.Against(fund.GovernmentId != GovernmentId, "Fund belongs to a different government.");
        Guard.Against(account.GovernmentId != GovernmentId, "Account belongs to a different government.");
        Guard.Against(department is not null && department.GovernmentId != GovernmentId, "Department belongs to a different government.");
        Guard.Against(!fund.IsActive, $"Fund {fund.Code} is inactive.");
        Guard.Against(!account.IsActive, $"Account {account.Code} is inactive.");
        Guard.Against(department is { IsActive: false }, $"Department {department?.Code} is inactive.");
        Guard.Against(
            account.Type == AccountType.Expenditure && department is null,
            "Expenditure lines require a department.");
        Guard.Against(
            _lines.Any(l => l.FundId == fund.Id && l.DepartmentId == department?.Id && l.AccountId == account.Id),
            $"A line for fund {fund.Code}, department {department?.Code ?? "(none)"}, account {account.Code} already exists.");

        var line = new BudgetLine(GovernmentId, Id, fund, department, account, amount, priorYearActual, currentYearBudget, justification);
        _lines.Add(line);
        return line;
    }

    public void UpdateLineAmount(Guid lineId, decimal amount)
    {
        EnsureEditable();
        FindLine(lineId).SetAmount(amount);
    }

    public void UpdateLineComparatives(Guid lineId, decimal priorYearActual, decimal currentYearBudget)
    {
        EnsureEditable();
        FindLine(lineId).SetComparatives(priorYearActual, currentYearBudget);
    }

    public void UpdateLineJustification(Guid lineId, string? justification)
    {
        EnsureEditable();
        FindLine(lineId).SetJustification(justification);
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();
        _lines.Remove(FindLine(lineId));
    }

    // ---- Beginning balances ----------------------------------------------------------------

    public void SetBeginningBalance(Fund fund, decimal amount)
    {
        EnsureEditable();
        Guard.Against(fund.GovernmentId != GovernmentId, "Fund belongs to a different government.");

        FundBeginningBalance? existing = _beginningBalances.FirstOrDefault(b => b.FundId == fund.Id);
        if (existing is null)
        {
            _beginningBalances.Add(new FundBeginningBalance(GovernmentId, Id, fund.Id, amount));
        }
        else
        {
            existing.SetAmount(amount);
        }
    }

    public decimal GetBeginningBalance(Guid fundId) =>
        _beginningBalances.FirstOrDefault(b => b.FundId == fundId)?.Amount ?? 0m;

    private void EnsureEditable() =>
        Guard.Against(!IsEditable, $"Budget version {Label} is Adopted and cannot be changed; create an amendment instead.");

    private BudgetLine FindLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainException($"Budget line {lineId} does not belong to this version.");
}
