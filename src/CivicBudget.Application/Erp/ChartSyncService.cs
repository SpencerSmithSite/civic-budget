using System.Text.Json;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Erp;

public sealed class ChartSyncService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IErpChartFileSource chartSource,
    TimeProvider clock) : IChartSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChartSourceStatusDto> StatusAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        ChartSync? last = await db.ChartSyncs.OrderByDescending(s => s.SyncedAtUtc).FirstOrDefaultAsync(ct);
        return new ChartSourceStatusDto(government.ChartSource, last?.SyncedAtUtc, last?.UserName, last?.SourceName);
    }

    public async Task<Result<ChartSyncPreviewDto>> PreviewAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (!CanSync())
        {
            return Result.Failure<ChartSyncPreviewDto>(NotAllowed);
        }

        Result<ErpChart> chart = chartSource.Read(fileName, content);
        if (chart.IsFailure)
        {
            return Result.Failure<ChartSyncPreviewDto>(chart.Errors);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Local local = await LoadLocalAsync(db, ct);
        List<Account> accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        if (await RetypedAccountsInUseAsync(db, chart.Value, accounts, ct) is { Count: > 0 } retyped)
        {
            return Result.Failure<ChartSyncPreviewDto>(RetypeRefusal(retyped));
        }

        return Result.Success(new ChartSyncPreviewDto(chartSource.Name, fileName, ChartDiff.Compute(chart.Value, local.Funds, local.Departments, local.Objects), chart.Value.NumberFormat));
    }

    public async Task<Result<ChartSyncDto>> CommitAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (!CanSync())
        {
            return Result.Failure<ChartSyncDto>(NotAllowed);
        }

        // The file is read and compared again rather than trusting the preview: someone may have changed
        // the chart between the preview and this click, and the diff must be against what is here now.
        Result<ErpChart> read = chartSource.Read(fileName, content);
        if (read.IsFailure)
        {
            return Result.Failure<ChartSyncDto>(read.Errors);
        }

        ErpChart chart = read.Value;
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        List<Fund> funds = await db.Funds.ToListAsync(ct);
        List<Department> departments = await db.Departments.ToListAsync(ct);
        List<Account> accounts = await db.Accounts.ToListAsync(ct);
        IReadOnlyList<ChartChange> changes = ChartDiff.Compute(chart, ToLocal(funds), ToLocal(departments), ToLocal(accounts));
        if (await RetypedAccountsInUseAsync(db, chart, accounts, ct) is { Count: > 0 } retyped)
        {
            return Result.Failure<ChartSyncDto>(RetypeRefusal(retyped));
        }

        if (changes.All(c => c.Change == ChartChangeKind.Unchanged))
        {
            return Result.Failure<ChartSyncDto>("The chart already matches the file; nothing to sync.");
        }

        try
        {
            Apply(government.Id, chart, changes, db, funds, departments, accounts);
        }
        catch (DomainException ex)
        {
            return Result.Failure<ChartSyncDto>(ex.Message);
        }

        if (chart.NumberFormat is not null)
        {
            government.SetAccountNumberFormat(chart.NumberFormat);
        }

        government.SetChartSource(ChartSource.Erp);

        int added = changes.Count(c => c.Change == ChartChangeKind.Add), updated = changes.Count(c => c.Change == ChartChangeKind.Update);
        int deactivated = changes.Count(c => c.Change == ChartChangeKind.Deactivate), reactivated = changes.Count(c => c.Change == ChartChangeKind.Reactivate);
        int unchanged = changes.Count(c => c.Change == ChartChangeKind.Unchanged);
        // The name comes from the user's computer; the log keeps what fits rather than refusing the sync.
        string loggedName = fileName.Length <= ChartSync.NameMaxLength ? fileName : fileName[..(ChartSync.NameMaxLength - 1)] + "…";
        var sync = new ChartSync(government.Id, chartSource.Name, loggedName, clock.GetUtcNow(), currentUser.UserId!, currentUser.DisplayName ?? currentUser.UserId!,
            added, updated, deactivated, reactivated, unchanged, JsonSerializer.Serialize(changes.Where(c => c.Change != ChartChangeKind.Unchanged), JsonOptions));
        db.ChartSyncs.Add(sync);
        db.AuditEntries.Add(AuditEntry.Event(government.Id, nameof(Government), government.Id,
            $"Synced the chart of accounts from {chartSource.Name} ({loggedName}): {added} added, {updated} updated, {deactivated} deactivated, {reactivated} reactivated",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(sync));
    }

    public async Task<IReadOnlyList<ChartSyncDto>> HistoryAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<ChartSync> syncs = await db.ChartSyncs.OrderByDescending(s => s.SyncedAtUtc).Take(50).ToListAsync(ct);
        return syncs.Select(ToDto).ToList();
    }

    public async Task<Result> SetChartSourceAsync(ChartSource source, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin))
        {
            return Result.Failure("Only an Administrator can change where the chart of accounts is maintained.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await GovernmentAsync(db, ct);
        if (government.ChartSource == source)
        {
            return Result.Success();
        }

        government.SetChartSource(source);
        db.AuditEntries.Add(AuditEntry.Event(government.Id, nameof(Government), government.Id,
            source == ChartSource.Erp ? "Chart of accounts is now maintained by the ERP" : "Chart of accounts is now maintained here",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can sync the chart of accounts.";

    private sealed record Local(IReadOnlyList<LocalFund> Funds, IReadOnlyList<LocalDepartment> Departments, IReadOnlyList<LocalObject> Objects);

    private bool CanSync() => currentUser.IsFiscalAuthority();

    private async Task<Government> GovernmentAsync(ICivicBudgetDbContext db, CancellationToken ct) =>
        await db.Governments.SingleAsync(g => g.Id == currentUser.GovernmentId, ct);

    private static async Task<Local> LoadLocalAsync(ICivicBudgetDbContext db, CancellationToken ct) => new(
        ToLocal(await db.Funds.ToListAsync(ct)), ToLocal(await db.Departments.ToListAsync(ct)), ToLocal(await db.Accounts.ToListAsync(ct)));

    private static List<LocalFund> ToLocal(List<Fund> funds) => funds.Select(f => new LocalFund(f.Id, f.Code, f.Name, f.Category, f.Description, f.IsActive)).ToList();
    private static List<LocalDepartment> ToLocal(List<Department> departments) => departments.Select(d => new LocalDepartment(d.Id, d.Code, d.Name, d.Description, d.IsActive)).ToList();
    private static List<LocalObject> ToLocal(List<Account> accounts) => accounts.Select(a => new LocalObject(a.Id, a.Code, a.Name, a.Type, a.Category, a.IsActive)).ToList();

    /// <summary>Applies each change through the entities' own methods so the domain rules and the audit interceptor both run.</summary>
    private static void Apply(Guid governmentId, ErpChart chart, IReadOnlyList<ChartChange> changes, ICivicBudgetDbContext db, List<Fund> funds, List<Department> departments, List<Account> accounts)
    {
        foreach (ChartChange change in changes.Where(c => c.Change != ChartChangeKind.Unchanged))
        {
            switch (change.Kind)
            {
                case "Fund":
                    {
                        ErpFund? erp = chart.Funds.FirstOrDefault(f => f.Code.Equals(change.Code, StringComparison.OrdinalIgnoreCase));
                        Fund? mine = funds.FirstOrDefault(f => f.Code.Equals(change.Code, StringComparison.OrdinalIgnoreCase));
                        if (change.Change == ChartChangeKind.Add)
                        {
                            db.Funds.Add(new Fund(governmentId, erp!.Code, erp.Name, erp.Category, erp.Description));
                        }
                        else if (change.Change == ChartChangeKind.Deactivate)
                        {
                            mine!.Deactivate();
                        }
                        else
                        {
                            mine!.Update(erp!.Code, erp.Name, erp.Category, erp.Description);
                            if (change.Change == ChartChangeKind.Reactivate)
                            {
                                mine.Reactivate();
                            }
                        }

                        break;
                    }

                case "Department":
                    {
                        ErpDepartment? erp = chart.Departments.FirstOrDefault(d => d.Code.Equals(change.Code, StringComparison.OrdinalIgnoreCase));
                        Department? mine = departments.FirstOrDefault(d => d.Code.Equals(change.Code, StringComparison.OrdinalIgnoreCase));
                        if (change.Change == ChartChangeKind.Add)
                        {
                            db.Departments.Add(new Department(governmentId, erp!.Code, erp.Name, erp.Description));
                        }
                        else if (change.Change == ChartChangeKind.Deactivate)
                        {
                            mine!.Deactivate();
                        }
                        else
                        {
                            mine!.Update(erp!.Code, erp.Name, erp.Description);
                            if (change.Change == ChartChangeKind.Reactivate)
                            {
                                mine.Reactivate();
                            }
                        }

                        break;
                    }

                default:
                    {
                        ErpObject? erp = chart.Objects.FirstOrDefault(o => o.Code.Equals(change.Code, StringComparison.OrdinalIgnoreCase));
                        Account? mine = accounts.FirstOrDefault(a => a.Code.Equals(change.Code, StringComparison.OrdinalIgnoreCase));
                        if (change.Change == ChartChangeKind.Add)
                        {
                            db.Accounts.Add(new Account(governmentId, erp!.Code, erp.Name, erp.Type, erp.Category));
                        }
                        else if (change.Change == ChartChangeKind.Deactivate)
                        {
                            mine!.Deactivate();
                        }
                        else
                        {
                            mine!.Update(erp!.Code, erp.Name, erp.Type, erp.Category);
                            if (change.Change == ChartChangeKind.Reactivate)
                            {
                                mine.Reactivate();
                            }
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Codes whose account type the file would change although budget lines use them. The same rule
    /// as editing an account by hand (AccountService): a revenue account turning into an expenditure
    /// would move money between resources and appropriations in every version, adopted ones included,
    /// because balances read the type from the account.
    /// </summary>
    private static async Task<List<string>> RetypedAccountsInUseAsync(ICivicBudgetDbContext db, ErpChart chart, List<Account> accounts, CancellationToken ct)
    {
        List<Account> retyped = accounts
            .Where(a => chart.Objects.FirstOrDefault(o => o.Code.Equals(a.Code, StringComparison.OrdinalIgnoreCase)) is { } erp && erp.Type != a.Type)
            .ToList();
        if (retyped.Count == 0)
        {
            return [];
        }

        List<Guid> ids = retyped.Select(a => a.Id).ToList();
        HashSet<Guid> used = (await db.BudgetLines.Where(l => ids.Contains(l.AccountId)).Select(l => l.AccountId).Distinct().ToListAsync(ct)).ToHashSet();
        return retyped.Where(a => used.Contains(a.Id)).Select(a => a.Code).Order(StringComparer.Ordinal).ToList();
    }

    private static string RetypeRefusal(List<string> codes) =>
        $"The file changes the account type of {string.Join(", ", codes)}, which budget lines already use. " +
        "Retire those codes in the ERP and add new ones instead; changing the type would move money between revenues and appropriations in every budget, adopted ones included.";

    private static ChartSyncDto ToDto(ChartSync s) => new(
        s.Id, s.SourceName, s.FileName, s.SyncedAtUtc, s.UserName, s.Added, s.Updated, s.Deactivated, s.Reactivated, s.Unchanged,
        JsonSerializer.Deserialize<List<ChartChange>>(s.ChangesJson, JsonOptions) ?? []);
}
