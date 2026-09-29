using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Personnel;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Personnel;

/// <summary>A budget that can take the ERP's employees: one still being prepared.</summary>
public sealed record SyncableVersionDto(Guid Id, int FiscalYear, string Label, bool HasSettings)
{
    public string Display => $"FY{FiscalYear} {Label}";
}

/// <summary>One step of a preview, in the page's words.</summary>
public sealed record SyncStepDto(SyncAction Action, string DepartmentCode, string DepartmentName, string Who, IReadOnlyList<string> Changes, decimal? CostBefore, decimal CostAfter);

/// <summary>A budget line the sync would change.</summary>
public sealed record SyncLineChangeDto(string AccountNumber, string AccountName, decimal? Before, decimal After);

/// <summary>What a sync would do to one budget, before anything is written.</summary>
public sealed record PersonnelSyncPreviewDto(
    Guid VersionId,
    string Version,
    string SourceName,
    string? FileName,
    DateOnly AsOf,
    int Employees,
    IReadOnlyList<SyncStepDto> Steps,
    IReadOnlyList<SyncLineChangeDto> Lines,
    /// <summary>Employees that cannot be matched; the sync is refused until there are none.</summary>
    IReadOnlyList<string> Errors)
{
    public int Count(SyncAction action) => Steps.Count(s => s.Action == action);
    public bool HasChanges => Steps.Any(s => s.Action != SyncAction.Unchanged);
    public bool CanCommit => Errors.Count == 0 && HasChanges;
}

/// <summary>One row of the personnel sync log.</summary>
public sealed record PersonnelSyncDto(
    Guid Id, string Version, DateOnly AsOf, string SourceName, string? FileName, DateTimeOffset SyncedAtUtc, string UserName,
    int Employees, int Added, int Filled, int Updated, int Vacated, int LinesChanged);

/// <summary>What the sync page needs up front.</summary>
public sealed record PersonnelSyncStatusDto(string? ApiName, IReadOnlyList<SyncableVersionDto> Versions, IReadOnlyList<PersonnelSyncDto> History);

/// <summary>
/// Brings a budget's positions up to date from the ERP's employee list, from an uploaded export or
/// straight from its API: usually once a year, when next year's budget starts. Preview shows each
/// employee's step and every budget line that would move; commit applies the same plan through
/// <see cref="BudgetVersion"/> (so each position and line change is audited) and logs the sync.
/// Administrators and the Fiscal Officer.
/// </summary>
public interface IPersonnelSyncService
{
    Task<PersonnelSyncStatusDto> StatusAsync(CancellationToken ct = default);
    Task<Result<PersonnelSyncPreviewDto>> PreviewFromErpAsync(Guid versionId, CancellationToken ct = default);
    Task<Result<PersonnelSyncPreviewDto>> PreviewFileAsync(Guid versionId, string fileName, Stream content, CancellationToken ct = default);
    Task<Result<PersonnelSyncDto>> CommitFromErpAsync(Guid versionId, CancellationToken ct = default);
    Task<Result<PersonnelSyncDto>> CommitFileAsync(Guid versionId, string fileName, Stream content, CancellationToken ct = default);
}

public sealed class PersonnelSyncService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IErpEmployeeFileSource fileSource,
    IEnumerable<IErpEmployeesApi> apis,
    TimeProvider clock) : IPersonnelSyncService
{
    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can bring employees in from the ERP.";
    private const string NoApi = "No ERP connection is set up for this government. Upload an employee export instead.";

    private readonly IErpEmployeesApi? adapter = apis.FirstOrDefault();

    // One adapter serves every government, but only those with a configured connection may use it.
    private IErpEmployeesApi? Api => adapter is not null && currentUser.GovernmentId is { } governmentId && adapter.IsConnected(governmentId) ? adapter : null;

    public async Task<PersonnelSyncStatusDto> StatusAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<int> setUp = await db.PersonnelSettings.Select(s => s.FiscalYear).ToListAsync(ct);
        var versions = (await db.BudgetVersions.Where(v => v.Status != BudgetStatus.Adopted)
                .Join(db.FiscalYears, v => v.FiscalYearId, f => f.Id, (v, f) => new { v.Id, f.Year, v.VersionNumber })
                .OrderByDescending(x => x.Year).ToListAsync(ct))
            .Select(x => new SyncableVersionDto(x.Id, x.Year, x.VersionNumber == 1 ? "Original" : $"Amendment {x.VersionNumber - 1}", setUp.Contains(x.Year)))
            .ToList();
        return new PersonnelSyncStatusDto(Api?.Name, versions, await HistoryAsync(db, ct));
    }

    public async Task<Result<PersonnelSyncPreviewDto>> PreviewFromErpAsync(Guid versionId, CancellationToken ct = default) =>
        (await RunAsync(versionId, FetchAsync, commit: false, ct)).Preview;

    public async Task<Result<PersonnelSyncPreviewDto>> PreviewFileAsync(Guid versionId, string fileName, Stream content, CancellationToken ct = default) =>
        (await RunAsync(versionId, (_, _) => Task.FromResult(ReadFile(fileName, content)), commit: false, ct)).Preview;

    public async Task<Result<PersonnelSyncDto>> CommitFromErpAsync(Guid versionId, CancellationToken ct = default) =>
        (await RunAsync(versionId, FetchAsync, commit: true, ct)).Committed;

    public async Task<Result<PersonnelSyncDto>> CommitFileAsync(Guid versionId, string fileName, Stream content, CancellationToken ct = default) =>
        (await RunAsync(versionId, (_, _) => Task.FromResult(ReadFile(fileName, content)), commit: true, ct)).Committed;

    // ---- the one path both preview and commit take ---------------------------------------------

    private sealed record Source(ErpEmployees Employees, string Name, string? FileName);

    private sealed record Outcome(Result<PersonnelSyncPreviewDto> Preview, Result<PersonnelSyncDto> Committed)
    {
        public static Outcome Failed(IEnumerable<ValidationError> errors) =>
            new(Result.Failure<PersonnelSyncPreviewDto>(errors), Result.Failure<PersonnelSyncDto>(errors));

        public static Outcome Failed(string message) => Failed([new ValidationError(string.Empty, message)]);
    }

    private async Task<Result<Source>> FetchAsync(Government government, CancellationToken ct)
    {
        if (Api is null)
        {
            return Result.Failure<Source>(NoApi);
        }

        var entity = new ErpEntity(government.Id, government.PublicSlug, government.Name, government.FiscalYearStartMonth, government.AccountNumberFormat);
        Result<ErpEmployees> fetched = await Api.FetchAsync(entity, ct);
        return fetched.IsFailure ? Result.Failure<Source>(fetched.Errors) : Result.Success(new Source(fetched.Value, Api.Name, null));
    }

    private Result<Source> ReadFile(string fileName, Stream content)
    {
        Result<ErpEmployees> read = fileSource.Read(fileName, content, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        return read.IsFailure ? Result.Failure<Source>(read.Errors) : Result.Success(new Source(read.Value, fileSource.Name, fileName));
    }

    /// <summary>
    /// Loads the budget, matches the employees, and applies the plan to the loaded budget. A preview
    /// reports what changed and throws the context away; a commit saves it. Running the real domain
    /// methods for the preview is what makes its line figures exactly what a commit will write.
    /// </summary>
    private async Task<Outcome> RunAsync(Guid versionId, Func<Government, CancellationToken, Task<Result<Source>>> source, bool commit, CancellationToken ct)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Outcome.Failed(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await PersonnelData.VersionsWithPositions(db).FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
        {
            return Outcome.Failed("Budget version was not found.");
        }

        if (!version.IsEditable)
        {
            return Outcome.Failed($"{version.Label} is adopted; bring employees into the budget being prepared, or into an amendment.");
        }

        FiscalYear year = await db.FiscalYears.SingleAsync(f => f.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        PayrollRules? rules = await PersonnelData.RulesAsync(db, year, ct);
        if (rules is null)
        {
            return Outcome.Failed($"{year.Label} has no personnel settings yet. Set them up first, so the employees' plans can be matched.");
        }

        Result<Source> got = await source(government, ct);
        if (got.IsFailure)
        {
            return Outcome.Failed(got.Errors);
        }

        Dictionary<Guid, Department> departments = await db.Departments.ToDictionaryAsync(d => d.Id, ct);
        Dictionary<Guid, Fund> funds = await db.Funds.ToDictionaryAsync(f => f.Id, ct);
        Dictionary<Guid, Account> accounts = await db.Accounts.ToDictionaryAsync(a => a.Id, ct);
        var chart = new EmployeeChart(
            departments.Values.Where(d => d.IsActive).ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase),
            funds.Values.Where(f => f.IsActive).ToDictionary(f => f.Code, f => f.Id, StringComparer.OrdinalIgnoreCase));
        SyncPlan plan = EmployeeMatcher.Plan(got.Value.Employees, chart, rules,
            version.Positions.Select(p => new ExistingPosition(p.Id, p.DepartmentId, p.ToDetails())).ToList());

        Dictionary<Guid, (decimal Amount, int? Positions)> linesBefore = version.Lines.ToDictionary(l => l.Id, l => (l.Amount, l.PositionCount));
        Dictionary<Guid, PositionDetails> detailsBefore = version.Positions.ToDictionary(p => p.Id, p => p.ToDetails());
        if (plan.Errors.Count == 0)
        {
            try
            {
                foreach (SyncStep step in plan.Steps.Where(s => s.Action != SyncAction.Unchanged))
                {
                    var personnelChart = new PersonnelChart(departments[step.DepartmentId], funds, accounts);
                    if (step.PositionId is { } positionId)
                    {
                        version.UpdatePosition(positionId, personnelChart, step.Details, rules);
                    }
                    else
                    {
                        version.AddPosition(personnelChart, step.Details, rules);
                    }
                }
            }
            catch (DomainException ex)
            {
                return Outcome.Failed(ex.Message);
            }
        }

        List<BudgetLine> changedLines = version.Lines
            .Where(l => !linesBefore.TryGetValue(l.Id, out (decimal Amount, int? Positions) was) || was.Amount != l.Amount || was.Positions != l.PositionCount)
            .OrderBy(l => l.Fund.Code).ThenBy(l => l.Department?.Code).ThenBy(l => l.Account.Code)
            .ToList();
        string versionName = $"{year.Label} {version.Label}";
        var preview = new PersonnelSyncPreviewDto(
            version.Id, versionName, got.Value.Name, got.Value.FileName, got.Value.Employees.AsOf, got.Value.Employees.Employees.Count,
            plan.Steps.OrderBy(s => s.Action).ThenBy(s => departments[s.DepartmentId].Code).ThenBy(s => s.Who)
                .Select(s => new SyncStepDto(s.Action, departments[s.DepartmentId].Code, departments[s.DepartmentId].Name, s.Who, s.Changes,
                    s.PositionId is { } id && detailsBefore.TryGetValue(id, out PositionDetails? was) ? Cost(was, rules) : null,
                    Cost(s.Details, rules) ?? 0m))
                .ToList(),
            changedLines.Select(l => new SyncLineChangeDto(
                AccountNumber.Compose(government.AccountNumberFormat, l.Fund.Code, l.Department?.Code, l.Account.Code), l.Account.Name,
                linesBefore.TryGetValue(l.Id, out (decimal Amount, int? Positions) b) ? b.Amount : null, l.Amount)).ToList(),
            plan.Errors);

        if (!commit)
        {
            return new Outcome(Result.Success(preview), Result.Failure<PersonnelSyncDto>("Preview only."));
        }

        if (plan.Errors.Count > 0)
        {
            return Outcome.Failed(plan.Errors.Take(20).Select(e => new ValidationError(string.Empty, e)));
        }

        if (!preview.HasChanges)
        {
            return Outcome.Failed("Every employee already matches the budget; there is nothing to bring in.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        var sync = new PersonnelSync(version.GovernmentId, version.Id, got.Value.Employees.AsOf, got.Value.Name, got.Value.FileName,
            currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", now, got.Value.Employees.Employees.Count,
            preview.Count(SyncAction.Add), preview.Count(SyncAction.Fill), preview.Count(SyncAction.Update), preview.Count(SyncAction.Vacate), changedLines.Count);
        db.PersonnelSyncs.Add(sync);
        db.AuditEntries.Add(AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id,
            $"Positions brought in from {got.Value.Name}: {sync.Added} added, {sync.Filled} filled, {sync.Updated} updated, {sync.Vacated} left vacant; {sync.LinesChanged} budget lines recalculated",
            sync.UserId, sync.UserName, now));
        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return Outcome.Failed(conflict.Errors);
        }

        return new Outcome(Result.Success(preview), Result.Success(ToDto(sync, versionName)));
    }

    private static decimal? Cost(PositionDetails details, PayrollRules rules) =>
        details.Problems(rules).Count == 0 ? PositionCostCalculator.Calculate(details, rules).Total : null;

    private static async Task<IReadOnlyList<PersonnelSyncDto>> HistoryAsync(ICivicBudgetDbContext db, CancellationToken ct)
    {
        var rows = await db.PersonnelSyncs.OrderByDescending(s => s.SyncedAtUtc).Take(20)
            .Join(db.BudgetVersions, s => s.BudgetVersionId, v => v.Id, (s, v) => new { s, v.VersionNumber, v.FiscalYearId })
            .Join(db.FiscalYears, x => x.FiscalYearId, f => f.Id, (x, f) => new { x.s, x.VersionNumber, f.Year })
            .ToListAsync(ct);
        return rows.OrderByDescending(x => x.s.SyncedAtUtc)
            .Select(x => ToDto(x.s, $"FY{x.Year} {(x.VersionNumber == 1 ? "Original" : $"Amendment {x.VersionNumber - 1}")}"))
            .ToList();
    }

    private static PersonnelSyncDto ToDto(PersonnelSync s, string version) =>
        new(s.Id, version, s.AsOf, s.SourceName, s.FileName, s.SyncedAtUtc, s.UserName, s.Employees, s.Added, s.Filled, s.Updated, s.Vacated, s.LinesChanged);
}
