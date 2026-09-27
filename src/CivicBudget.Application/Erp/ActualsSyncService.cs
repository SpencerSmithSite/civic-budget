using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Erp;

public sealed class ActualsSyncService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IErpActualsFileSource fileSource,
    IEnumerable<IErpActualsApi> apis,
    TimeProvider clock) : IActualsSyncService
{
    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can sync actuals from the ERP.";
    private const string NoApi = "No ERP connection is set up for this government. Upload an actuals export instead.";

    /// <summary>At most one connection is registered; with none, the page offers the file upload alone.</summary>
    private readonly IErpActualsApi? api = apis.FirstOrDefault();

    public async Task<ActualsStatusDto> StatusAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<ActualsSync> syncs = await db.ActualsSyncs.OrderByDescending(s => s.SyncedAtUtc).ToListAsync(ct);
        List<ActualsYearDto> years = syncs.GroupBy(s => s.FiscalYear).Select(g => ToYear(g.First())).OrderByDescending(y => y.FiscalYear).ToList();

        // Only years that have begun can have books: the ERP has nothing for next year's budget yet.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        List<int> fetchable = api is null
            ? []
            : await db.FiscalYears.Where(fy => fy.StartDate <= today).OrderByDescending(fy => fy.Year).Select(fy => fy.Year).ToListAsync(ct);
        return new ActualsStatusDto(api?.Name, fetchable, years);
    }

    public async Task<Result<ActualsPreviewDto>> PreviewFileAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<ActualsPreviewDto>(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        Result<ErpActuals> read = fileSource.Read(fileName, content, government.AccountNumberFormat);
        return read.IsFailure ? Result.Failure<ActualsPreviewDto>(read.Errors) : await PreviewAsync(db, government, read.Value, fileSource.Name, fileName, ct);
    }

    public async Task<Result<ActualsPreviewDto>> PreviewFromErpAsync(int fiscalYear, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<ActualsPreviewDto>(NotAllowed);
        }

        if (api is null)
        {
            return Result.Failure<ActualsPreviewDto>(NoApi);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        Result<ErpActuals> fetched = await api.FetchAsync(EntityFor(government), fiscalYear, ct);
        return fetched.IsFailure ? Result.Failure<ActualsPreviewDto>(fetched.Errors) : await PreviewAsync(db, government, fetched.Value, api.Name, null, ct);
    }

    public async Task<Result<ActualsSyncDto>> CommitFileAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<ActualsSyncDto>(NotAllowed);
        }

        // Read and compared again rather than trusting the preview: a budget line may have changed
        // since, and the prior-year actuals must be written against what is here now.
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        Result<ErpActuals> read = fileSource.Read(fileName, content, government.AccountNumberFormat);
        return read.IsFailure ? Result.Failure<ActualsSyncDto>(read.Errors) : await CommitAsync(db, government, read.Value, fileSource.Name, fileName, ct);
    }

    public async Task<Result<ActualsSyncDto>> CommitFromErpAsync(int fiscalYear, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<ActualsSyncDto>(NotAllowed);
        }

        if (api is null)
        {
            return Result.Failure<ActualsSyncDto>(NoApi);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        Result<ErpActuals> fetched = await api.FetchAsync(EntityFor(government), fiscalYear, ct);
        return fetched.IsFailure ? Result.Failure<ActualsSyncDto>(fetched.Errors) : await CommitAsync(db, government, fetched.Value, api.Name, null, ct);
    }

    public async Task<IReadOnlyList<ActualsSyncDto>> HistoryAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<ActualsSync> syncs = await db.ActualsSyncs.OrderByDescending(s => s.SyncedAtUtc).Take(50).ToListAsync(ct);
        return syncs.Select(ToDto).ToList();
    }

    // ---- the two steps ------------------------------------------------------------------------

    /// <summary>Everything a sync decides before it writes, shared by the preview and the commit.</summary>
    private sealed record Plan(
        MatchedActuals Matched, DateOnly AsOf, ActualsSync? Replaces, List<PriorActualChange> Changes, List<string> Notes,
        Dictionary<Guid, Fund> Funds, Dictionary<Guid, BudgetVersion> VersionsById, int BudgetYear);

    private static async Task<Result<ActualsPreviewDto>> PreviewAsync(ICivicBudgetDbContext db, Government government, ErpActuals actuals, string source, string? fileName, CancellationToken ct)
    {
        Result<Plan> planned = await PlanAsync(db, government, actuals, ct);
        if (planned.IsFailure)
        {
            return Result.Failure<ActualsPreviewDto>(planned.Errors);
        }

        Plan plan = planned.Value;
        MatchedActuals m = plan.Matched;
        List<ActualsFundDto> funds = plan.Funds.Values
            .Select(f => new ActualsFundDto(
                f.Code, f.Name,
                m.Activity.Where(a => a.FundId == f.Id && a.Type.IsResource()).Sum(a => a.Amount),
                m.Activity.Where(a => a.FundId == f.Id && !a.Type.IsResource()).Sum(a => a.Amount),
                m.Encumbrances.Where(e => e.FundId == f.Id).Sum(e => e.Amount),
                m.Cash.TryGetValue(f.Id, out decimal cash) ? cash : null))
            .Where(f => f.Receipts != 0m || f.Disbursements != 0m || f.Encumbered != 0m || f.Cash is not null)
            .OrderBy(f => f.FundCode, StringComparer.Ordinal)
            .ToList();
        List<PriorActualChangeDto> changes = plan.Changes
            .Select(c => new PriorActualChangeDto(
                $"FY{plan.BudgetYear} {plan.VersionsById[c.Line.BudgetVersionId].Label}",
                AccountNumber.Compose(government.AccountNumberFormat, c.Line.Fund.Code, c.Line.Department?.Code, c.Line.Account.Code),
                c.Line.Account.Name, c.Before, c.After))
            .OrderBy(c => c.AccountNumber, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new ActualsPreviewDto(
            source, fileName, m.FiscalYear, m.ThroughPeriod, plan.AsOf, m.Activity.Count,
            m.Receipts, m.Disbursements, m.Encumbered, m.CashTotal, funds, changes, plan.Notes,
            plan.Replaces is null ? null : ToYear(plan.Replaces)));
    }

    private async Task<Result<ActualsSyncDto>> CommitAsync(ICivicBudgetDbContext db, Government government, ErpActuals actuals, string source, string? fileName, CancellationToken ct)
    {
        Result<Plan> planned = await PlanAsync(db, government, actuals, ct);
        if (planned.IsFailure)
        {
            return Result.Failure<ActualsSyncDto>(planned.Errors);
        }

        Plan plan = planned.Value;
        int year = plan.Matched.FiscalYear;

        // The year is replaced as a whole: the ERP's books are the truth, and a row it no longer
        // reports (a reversed posting) must disappear here too.
        db.ErpActuals.RemoveRange(await db.ErpActuals.Where(a => a.FiscalYear == year).ToListAsync(ct));
        db.ErpEncumbrances.RemoveRange(await db.ErpEncumbrances.Where(e => e.FiscalYear == year).ToListAsync(ct));
        db.ErpFundCash.RemoveRange(await db.ErpFundCash.Where(c => c.FiscalYear == year).ToListAsync(ct));

        // The name comes from the user's computer; the log keeps what fits rather than refusing the sync.
        string? loggedName = fileName is null || fileName.Length <= ActualsSync.NameMaxLength ? fileName : fileName[..(ActualsSync.NameMaxLength - 1)] + "…";
        string userName = currentUser.DisplayName ?? currentUser.UserId!;
        ActualsSync sync = ActualsWriter.Add(db, government.Id, plan.Matched, plan.AsOf, source, loggedName, currentUser.UserId!, userName, clock.GetUtcNow(), plan.Changes.Count);

        string through = plan.Matched.IsYearClosed ? "the full year" : LongDate(plan.AsOf);
        string fromWhere = loggedName is null ? source : $"{source} ({loggedName})";
        string priorNote = plan.Changes.Count == 0 ? "" : $"; updated {plan.Changes.Count} prior-year actual{(plan.Changes.Count == 1 ? "" : "s")} in the FY{plan.BudgetYear} budget";
        db.AuditEntries.Add(AuditEntry.Event(government.Id, nameof(Government), government.Id,
            $"Synced FY{year} actuals from {fromWhere} through {through}{priorNote}", currentUser.UserId!, userName, clock.GetUtcNow()));

        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return Result.Failure<ActualsSyncDto>(conflict.Errors);
        }

        return Result.Success(ToDto(sync));
    }

    private static async Task<Result<Plan>> PlanAsync(ICivicBudgetDbContext db, Government government, ErpActuals actuals, CancellationToken ct)
    {
        List<Fund> funds = await db.Funds.ToListAsync(ct);
        List<Department> departments = await db.Departments.ToListAsync(ct);
        List<Account> accounts = await db.Accounts.ToListAsync(ct);
        Result<MatchedActuals> matched = ActualsMatcher.Match(
            actuals,
            funds.Select(f => new ChartCode(f.Id, f.Code, f.Name)).ToList(),
            departments.Select(d => new ChartCode(d.Id, d.Code, d.Name)).ToList(),
            accounts.Select(a => new ChartCode(a.Id, a.Code, a.Name, a.Type)).ToList());
        if (matched.IsFailure)
        {
            return Result.Failure<Plan>(matched.Errors);
        }

        MatchedActuals m = matched.Value;
        DateOnly asOf = FiscalPeriod.For(m.FiscalYear, government.FiscalYearStartMonth).Start.AddMonths(m.ThroughPeriod).AddDays(-1);
        var notes = new List<string>();
        ActualsSync? replaces = await ErpActualsReader.LatestSyncAsync(db, m.FiscalYear, ct);
        if (replaces is not null && replaces.ThroughPeriod > m.ThroughPeriod)
        {
            notes.Add($"These figures run through {LongDate(asOf)}, but the FY{m.FiscalYear} actuals here already run through {LongDate(replaces.AsOf)}. Applying replaces newer figures with older ones.");
        }

        // A closed year is the prior-year actual of the budget two years on; write it into every
        // version of that budget still open. An open year fills nothing (see PriorYearActuals).
        int budgetYear = m.FiscalYear + 2;
        var changes = new List<PriorActualChange>();
        var versionsById = new Dictionary<Guid, BudgetVersion>();
        if (m.IsYearClosed)
        {
            List<BudgetVersion> open = await db.BudgetVersions
                .Where(v => v.Status != BudgetStatus.Adopted && db.FiscalYears.Any(fy => fy.Id == v.FiscalYearId && fy.Year == budgetYear))
                .Include(v => v.Lines).ThenInclude(l => l.Fund)
                .Include(v => v.Lines).ThenInclude(l => l.Department)
                .Include(v => v.Lines).ThenInclude(l => l.Account)
                .ToListAsync(ct);
            foreach (BudgetVersion version in open)
            {
                versionsById[version.Id] = version;
                Dictionary<LineKey, decimal> totals = ActualsByLine.Sum(
                    version.Lines.Select(l => new LineKey(l.FundId, l.DepartmentId, l.AccountId)),
                    m.Activity.Select(a => (new LineKey(a.FundId, a.DepartmentId, a.AccountId), a.Amount)));
                changes.AddRange(PriorYearActuals.Apply(version, totals, notes));
            }
        }

        return Result.Success(new Plan(m, asOf, replaces, changes, notes, funds.ToDictionary(f => f.Id), versionsById, budgetYear));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private async Task<Government> GovernmentAsync(ICivicBudgetDbContext db, CancellationToken ct) =>
        await db.Governments.SingleAsync(g => g.Id == currentUser.GovernmentId, ct);

    private static string LongDate(DateOnly date) => date.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static ErpEntity EntityFor(Government g) => new(g.Id, g.PublicSlug, g.Name, g.FiscalYearStartMonth, g.AccountNumberFormat);

    private static ActualsYearDto ToYear(ActualsSync s) => new(s.FiscalYear, s.ThroughPeriod, s.AsOf, s.SyncedAtUtc, s.UserName, s.SourceName);

    private static ActualsSyncDto ToDto(ActualsSync s) => new(
        s.Id, s.FiscalYear, s.ThroughPeriod, s.AsOf, s.SourceName, s.FileName, s.SyncedAtUtc, s.UserName,
        s.ActivityRows, s.Receipts, s.Disbursements, s.Encumbered, s.Cash, s.PriorActualsUpdated);
}

/// <summary>
/// Stages a matched year's rows and its log entry on a context. The sync service calls it after
/// removing the year's old rows; the development seeder calls it to give the demo a history.
/// </summary>
public static class ActualsWriter
{
    public static ActualsSync Add(ICivicBudgetDbContext db, Guid governmentId, MatchedActuals m, DateOnly asOf, string sourceName, string? fileName,
        string userId, string userName, DateTimeOffset nowUtc, int priorActualsUpdated)
    {
        db.ErpActuals.AddRange(m.Activity.Select(a => new ErpActual(governmentId, m.FiscalYear, a.Period, a.FundId, a.DepartmentId, a.AccountId, a.Amount)));
        db.ErpEncumbrances.AddRange(m.Encumbrances.Select(e => new ErpEncumbrance(governmentId, m.FiscalYear, e.FundId, e.DepartmentId, e.AccountId, e.Amount)));
        db.ErpFundCash.AddRange(m.Cash.Select(c => new ErpFundCash(governmentId, m.FiscalYear, c.Key, c.Value)));
        var sync = new ActualsSync(governmentId, m.FiscalYear, m.ThroughPeriod, asOf, sourceName, fileName, nowUtc, userId, userName,
            m.Activity.Count, m.Receipts, m.Disbursements, m.Encumbered, m.CashTotal, priorActualsUpdated);
        db.ActualsSyncs.Add(sync);
        return sync;
    }
}
