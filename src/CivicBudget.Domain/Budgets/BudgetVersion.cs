using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Budgets;

/// <summary>
/// One version of a fiscal year's budget: the original (version 1) or an amendment (2, 3, …).
/// This is the aggregate root: lines, beginning balances, and department requests are only ever
/// changed through its methods, so rules like "an adopted budget cannot change" live in exactly one
/// place instead of being re-checked (or forgotten) by every screen and service that edits a budget.
/// </summary>
[Audited]
public sealed class BudgetVersion : Entity, ITenantOwned
{
    public const int ReasonMaxLength = 1000;
    public const int ResolutionNumberMaxLength = 50;

    private readonly List<BudgetLine> _lines = [];
    private readonly List<FundBeginningBalance> _beginningBalances = [];
    private readonly List<DepartmentRequest> _departmentRequests = [];

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

    /// <summary>One per department that has written a narrative or submitted; departments without a row are simply in progress.</summary>
    public IReadOnlyCollection<DepartmentRequest> DepartmentRequests => _departmentRequests.AsReadOnly();

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
    /// Starts a fiscal year's Original budget from the prior year's latest adopted version. Each line
    /// carries over with last year's adopted amount as the comparative and, changed by the options'
    /// percentage, as the starting request; the prior-year actual starts at zero until actuals are
    /// imported, because the adopted budget is not what was actually spent. Each fund's beginning
    /// balance is last year's projected ending balance, the best estimate until the books close.
    /// Lines whose fund, department, or account has since been retired are left out and listed, so
    /// nothing is silently dropped.
    /// </summary>
    /// <param name="funds">Every fund of the government, active or not (a fund with only a balance has no line to reach it through).</param>
    public static (BudgetVersion Version, IReadOnlyList<string> Skipped) CreateOriginalFrom(
        BudgetVersion priorAdopted, Guid fiscalYearId, IReadOnlyDictionary<Guid, Fund> funds, BudgetSeedOptions options)
    {
        Guard.Against(priorAdopted.Status != BudgetStatus.Adopted, "A new budget can only start from an adopted one.");
        Guard.Against(priorAdopted.SupersededByVersionId is not null, "Start from the latest adopted version; a later amendment replaced this one.");
        Guard.Against(priorAdopted.FiscalYearId == fiscalYearId, "A budget cannot be started from its own fiscal year.");
        Guard.Against(
            options.AdjustmentPercent < BudgetSeedOptions.MinPercent || options.AdjustmentPercent > BudgetSeedOptions.MaxPercent,
            $"The change must be between {BudgetSeedOptions.MinPercent}% and {BudgetSeedOptions.MaxPercent}%.");

        BudgetVersion version = CreateOriginal(priorAdopted.GovernmentId, fiscalYearId);
        var skipped = new List<string>();
        foreach (BudgetLine line in priorAdopted.Lines.OrderBy(l => l.Fund.Code).ThenBy(l => l.Department?.Code).ThenBy(l => l.Account.Code))
        {
            if (!line.Fund.IsActive || !line.Account.IsActive || line.Department is { IsActive: false })
            {
                skipped.Add($"{line.Fund.Code} {line.Department?.Code} {line.Account.Code} {line.Account.Name}".Replace("  ", " ", StringComparison.Ordinal));
                continue;
            }

            version.AddLine(line.Fund, line.Department, line.Account,
                amount: options.Apply(line.Amount, line.Account.Type),
                priorYearActual: 0m,
                currentYearBudget: line.Amount);
        }

        foreach (FundBalanceSummary summary in FundBalanceCalculator.CalculateAll(priorAdopted))
        {
            if (funds.TryGetValue(summary.FundId, out Fund? fund) && fund.IsActive)
            {
                version.SetBeginningBalance(fund, summary.ProjectedEndingBalance);
            }
        }

        return (version, skipped);
    }

    /// <summary>
    /// A fiscal year has at most one budget in progress. Two open drafts for the same year would leave
    /// no clear answer to "which one is the budget?". Call with the year's existing versions before
    /// creating a new one.
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
        Touch();
        Guard.Against(Status != BudgetStatus.Draft, $"Only a Draft budget can be proposed (current status: {Status}).");
        Status = BudgetStatus.Proposed;
    }

    public void ReturnToDraft()
    {
        Touch();
        Guard.Against(Status != BudgetStatus.Proposed, $"Only a Proposed budget can be returned to Draft (current status: {Status}).");
        Status = BudgetStatus.Draft;
    }

    public void Adopt(string resolutionNumber, string adoptedByUserId, DateTimeOffset nowUtc)
    {
        Touch();
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
        amendment._departmentRequests.AddRange(_departmentRequests.Select(r => r.CopyTo(amendment.Id)));
        return amendment;
    }

    /// <summary>Called on the previously adopted version when an amendment is adopted.</summary>
    public void MarkSupersededBy(BudgetVersion amendment)
    {
        Touch();
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
        Touch();
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
        Touch();
        EnsureEditable();
        FindLine(lineId).SetAmount(amount);
    }

    public void UpdateLineComparatives(Guid lineId, decimal priorYearActual, decimal currentYearBudget)
    {
        Touch();
        EnsureEditable();
        FindLine(lineId).SetComparatives(priorYearActual, currentYearBudget);
    }

    public void UpdateLineJustification(Guid lineId, string? justification)
    {
        Touch();
        EnsureEditable();
        FindLine(lineId).SetJustification(justification);
    }

    public void RemoveLine(Guid lineId)
    {
        Touch();
        EnsureEditable();
        _lines.Remove(FindLine(lineId));
    }

    // ---- Beginning balances ----------------------------------------------------------------

    public void SetBeginningBalance(Fund fund, decimal amount)
    {
        Touch();
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

    // ---- Department requests ---------------------------------------------------------------

    public DepartmentRequest? GetDepartmentRequest(Guid departmentId) =>
        _departmentRequests.FirstOrDefault(r => r.DepartmentId == departmentId);

    /// <summary>True once the department has handed its request in and it has not been returned.</summary>
    public bool IsDepartmentSubmitted(Guid departmentId) =>
        GetDepartmentRequest(departmentId)?.IsSubmitted == true;

    public void SetDepartmentNarrative(Department department, string? narrative)
    {
        Touch();
        EnsureEditable();
        EnsureOwnDepartment(department);
        DepartmentRequest request = GetDepartmentRequest(department.Id) ?? StartDepartmentRequest(department);
        request.SetNarrative(narrative);
    }

    /// <summary>
    /// The department hands its request to the fiscal officer. Only while the version is Draft: once
    /// proposed, the officer owns the whole budget and department rounds are over. A department with
    /// nothing budgeted has nothing to submit.
    /// </summary>
    public void SubmitDepartment(Department department, string userId, string userName, DateTimeOffset nowUtc)
    {
        Touch();
        Guard.Against(Status != BudgetStatus.Draft, $"Departments submit while the budget is Draft (current status: {Status}).");
        EnsureOwnDepartment(department);
        Guard.Against(_lines.All(l => l.DepartmentId != department.Id), $"{department.Name} has no budget lines in this version to submit.");
        DepartmentRequest request = GetDepartmentRequest(department.Id) ?? StartDepartmentRequest(department);
        request.Submit(userId, userName, nowUtc);
    }

    /// <summary>The fiscal officer sends a submitted request back to the department with a reason.</summary>
    public void ReturnDepartment(Department department, string note, DateTimeOffset nowUtc)
    {
        Touch();
        Guard.Against(Status != BudgetStatus.Draft, $"Requests are returned while the budget is Draft (current status: {Status}).");
        EnsureOwnDepartment(department);
        DepartmentRequest request = GetDepartmentRequest(department.Id)
            ?? throw new DomainException($"{department.Name} has not submitted a request.");
        request.Return(note, nowUtc);
    }

    private DepartmentRequest StartDepartmentRequest(Department department)
    {
        var request = new DepartmentRequest(GovernmentId, Id, department.Id);
        _departmentRequests.Add(request);
        return request;
    }

    private void EnsureOwnDepartment(Department department) =>
        Guard.Against(department.GovernmentId != GovernmentId, "Department belongs to a different government.");

    /// <summary>
    /// Counts every change to the budget, its lines, balances, and department requests included. The
    /// database checks it on save (a concurrency token), so two people who both loaded revision 7 cannot
    /// both save: the second is told the budget changed under them instead of silently overwriting,
    /// and an amount edit can no longer land on a budget adopted a moment earlier.
    /// </summary>
    [NotAudited]
    public int Revision { get; private set; }

    /// <summary>
    /// Every mutating method calls this first, before its guards. If a guard then throws, the change is
    /// never saved, so the extra count is harmless; and a method that returns early cannot skip it.
    /// </summary>
    private void Touch() => Revision++;

    private void EnsureEditable() =>
        Guard.Against(!IsEditable, $"Budget version {Label} is Adopted and cannot be changed; create an amendment instead.");

    private BudgetLine FindLine(Guid lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainException($"Budget line {lineId} does not belong to this version.");
}
