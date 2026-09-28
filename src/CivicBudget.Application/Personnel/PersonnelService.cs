using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Personnel;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Personnel;

/// <summary>
/// A department's positions in one budget version. Who may change them is exactly who may change the
/// department's lines (<see cref="BudgetLinePermissions.CanEdit"/>), because changing a position
/// changes those lines. Every save prices the department again and updates its lines in the same
/// transaction, through <see cref="BudgetVersion"/>.
/// </summary>
public interface IPersonnelService
{
    Task<PersonnelPageDto?> GetDepartmentAsync(Guid versionId, Guid departmentId, CancellationToken ct = default);
    Task<Result<PersonnelSavedDto>> AddPositionAsync(Guid versionId, Guid departmentId, PositionDetails details, CancellationToken ct = default);
    Task<Result<PersonnelSavedDto>> UpdatePositionAsync(Guid versionId, Guid positionId, PositionDetails details, CancellationToken ct = default);
    Task<Result<PersonnelSavedDto>> RemovePositionAsync(Guid versionId, Guid positionId, CancellationToken ct = default);
}

public sealed class PersonnelService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : IPersonnelService
{
    private const string NotAllowed = "You do not have permission to change this department's positions.";

    public async Task<PersonnelPageDto?> GetDepartmentAsync(Guid versionId, Guid departmentId, CancellationToken ct = default)
    {
        if (currentUser.IsDepartmentUser() && !currentUser.DepartmentIds.Contains(departmentId))
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await PersonnelData.VersionsWithPositions(db).FirstOrDefaultAsync(v => v.Id == versionId, ct);
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (version is null || department is null)
        {
            return null;
        }

        FiscalYear year = await db.FiscalYears.SingleAsync(f => f.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        PayrollRules? rules = await PersonnelData.RulesAsync(db, year, ct);

        List<PositionDto> positions = version.Positions
            .Where(p => p.DepartmentId == departmentId)
            .Select(p => Priced(p.Id, p.ToDetails(), rules))
            .OrderBy(p => p.Details.Title).ThenBy(p => p.Details.EmployeeName ?? "￿")
            .ToList();

        List<BudgetLine> personnelLines = version.Lines.Where(l => l.DepartmentId == departmentId && l.IsFromPersonnel).ToList();
        List<PersonnelLineDto> lines = personnelLines
            .OrderBy(l => l.Fund.Code).ThenBy(l => l.Account.Code)
            .Select(l => new PersonnelLineDto(l.Id, l.Fund.Code, l.Fund.Name,
                AccountNumber.Compose(government.AccountNumberFormat, l.Fund.Code, department.Code, l.Account.Code),
                l.Account.Code, l.Account.Name, l.Amount, l.PositionCount!.Value))
            .ToList();

        bool linesMatch = rules is null || positions.Any(p => p.Cost is null) || Matches(personnelLines, positions);

        List<Guid> usedFunds = positions.SelectMany(p => p.Details.Funds.Select(f => f.FundId)).ToList();
        List<LookupDto> funds = await db.Funds.Where(f => f.IsActive || usedFunds.Contains(f.Id)).OrderBy(f => f.Code)
            .Select(f => new LookupDto(f.Id, f.Code, f.Name)).ToListAsync(ct);
        List<AccountLookupDto> accounts = await db.Accounts.OrderBy(a => a.Code)
            .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type, a.Category)).ToListAsync(ct);
        IQueryable<Department> pickable = db.Departments.Where(d => d.IsActive || d.Id == departmentId);
        List<LookupDto> departments = await pickable.OrderBy(d => d.Code).Select(d => new LookupDto(d.Id, d.Code, d.Name)).ToListAsync(ct);
        if (currentUser.IsDepartmentUser())
        {
            departments = departments.Where(d => currentUser.DepartmentIds.Contains(d.Id)).ToList();
        }

        bool canEdit = rules is not null && CanEdit(version, departmentId);
        return new PersonnelPageDto(
            version.Id, year.Year, version.Label, version.Status, government.FiscalYearStartMonth,
            new LookupDto(department.Id, department.Code, department.Name), departments,
            canEdit, rules, currentUser.IsFiscalAuthority(),
            positions, lines, funds, accounts, linesMatch);
    }

    public Task<Result<PersonnelSavedDto>> AddPositionAsync(Guid versionId, Guid departmentId, PositionDetails details, CancellationToken ct = default) =>
        SaveAsync(versionId, departmentId, null, details, ct);

    public Task<Result<PersonnelSavedDto>> UpdatePositionAsync(Guid versionId, Guid positionId, PositionDetails details, CancellationToken ct = default) =>
        SaveAsync(versionId, null, positionId, details, ct);

    public async Task<Result<PersonnelSavedDto>> RemovePositionAsync(Guid versionId, Guid positionId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Result<Loaded> loaded = await LoadForEditAsync(db, versionId, null, positionId, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<PersonnelSavedDto>(loaded.Errors);
        }

        (BudgetVersion version, Department department, Position? found, PayrollRules rules) = loaded.Value;
        Position position = found!; // a position id was given, so LoadForEditAsync found it or failed
        string described = PersonnelData.Describe(position.ToDetails());
        IReadOnlyList<PersonnelLineChange> changes;
        try
        {
            changes = version.RemovePosition(position.Id, await PersonnelData.ChartAsync(db, department, ct), rules);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PersonnelSavedDto>(ex.Message);
        }

        return await CommitAsync(db, version, $"Removed position {described} from {department.Name}", position.Id, changes, ct);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private sealed record Loaded(BudgetVersion Version, Department Department, Position? Position, PayrollRules Rules);

    private async Task<Result<PersonnelSavedDto>> SaveAsync(Guid versionId, Guid? departmentId, Guid? positionId, PositionDetails details, CancellationToken ct)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Result<Loaded> loaded = await LoadForEditAsync(db, versionId, departmentId, positionId, ct);
        if (loaded.IsFailure)
        {
            return Result.Failure<PersonnelSavedDto>(loaded.Errors);
        }

        (BudgetVersion version, Department department, Position? existing, PayrollRules rules) = loaded.Value;
        List<ValidationError> errors = details.Problems(rules).Select(p => new ValidationError(p.Field, p.Message)).ToList();
        errors.AddRange(await ChartProblemsAsync(db, details, existing?.ToDetails(), ct));
        if (errors.Count > 0)
        {
            return Result.Failure<PersonnelSavedDto>(errors);
        }

        PersonnelChart chart = await PersonnelData.ChartAsync(db, department, ct);
        Guid id;
        IReadOnlyList<PersonnelLineChange> changes;
        try
        {
            if (existing is null)
            {
                (Position added, changes) = version.AddPosition(chart, details, rules);
                id = added.Id;
            }
            else
            {
                changes = version.UpdatePosition(existing.Id, chart, details, rules);
                id = existing.Id;
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<PersonnelSavedDto>(ex.Message);
        }

        string verb = existing is null ? "Added" : "Changed";
        return await CommitAsync(db, version, $"{verb} position {PersonnelData.Describe(details)} in {department.Name}", id, changes, ct);
    }

    /// <summary>Loads what a change needs and refuses it when the user may not make it or the year has no settings.</summary>
    private async Task<Result<Loaded>> LoadForEditAsync(ICivicBudgetDbContext db, Guid versionId, Guid? departmentId, Guid? positionId, CancellationToken ct)
    {
        BudgetVersion? version = await PersonnelData.VersionsWithPositions(db).FirstOrDefaultAsync(v => v.Id == versionId, ct);
        Position? position = positionId is { } id ? version?.Positions.FirstOrDefault(p => p.Id == id) : null;
        Guid? department = position?.DepartmentId ?? departmentId;
        if (version is null || department is null || (positionId is not null && position is null))
        {
            return Result.Failure<Loaded>(positionId is null ? "Budget version was not found." : "That position was not found; it may have been removed.");
        }

        if (!CanEdit(version, department.Value))
        {
            return Result.Failure<Loaded>(NotAllowed);
        }

        Department? found = await db.Departments.FirstOrDefaultAsync(d => d.Id == department.Value, ct);
        FiscalYear year = await db.FiscalYears.SingleAsync(f => f.Id == version.FiscalYearId, ct);
        PayrollRules? rules = await PersonnelData.RulesAsync(db, year, ct);
        if (found is null)
        {
            return Result.Failure<Loaded>("Department was not found.");
        }

        if (rules is null)
        {
            return Result.Failure<Loaded>($"{year.Label} has no personnel settings yet. An Administrator or the Fiscal Officer sets them up in Personnel settings.");
        }

        return Result.Success(new Loaded(version, found, position, rules));
    }

    /// <summary>
    /// The chart's side of a valid position: its funds exist and are active, and a pay account of its
    /// own is an active expenditure account. A fund or account the position already had may stay, so
    /// retiring a fund does not stop anyone editing the positions it pays for.
    /// </summary>
    private static async Task<List<ValidationError>> ChartProblemsAsync(ICivicBudgetDbContext db, PositionDetails details, PositionDetails? before, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        List<Guid> fundIds = details.Funds.Select(f => f.FundId).ToList();
        List<Fund> funds = await db.Funds.Where(f => fundIds.Contains(f.Id)).ToListAsync(ct);
        if (funds.Count != fundIds.Distinct().Count())
        {
            errors.Add(new ValidationError(nameof(PositionDetails.Funds), "A fund was not found."));
        }
        else if (funds.FirstOrDefault(f => !f.IsActive && before?.Funds.Any(b => b.FundId == f.Id) != true) is { } inactive)
        {
            errors.Add(new ValidationError(nameof(PositionDetails.Funds), $"Fund {inactive.Code} {inactive.Name} is inactive."));
        }

        if (details.PayAccountId is { } accountId && accountId != before?.PayAccountId)
        {
            Account? account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is not { IsActive: true, Type: AccountType.Expenditure })
            {
                errors.Add(new ValidationError(nameof(PositionDetails.PayAccountId), "Base pay must land on an active expenditure account."));
            }
        }

        return errors;
    }

    private async Task<Result<PersonnelSavedDto>> CommitAsync(ICivicBudgetDbContext db, BudgetVersion version, string description, Guid positionId,
        IReadOnlyList<PersonnelLineChange> changes, CancellationToken ct)
    {
        db.AuditEntries.Add(AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id,
            changes.Count == 0 ? description : $"{description}; {changes.Count} budget line{(changes.Count == 1 ? "" : "s")} recalculated",
            currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow()));
        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return Result.Failure<PersonnelSavedDto>(conflict.Errors);
        }

        return Result.Success(new PersonnelSavedDto(positionId, changes.Count));
    }

    private bool CanEdit(BudgetVersion version, Guid departmentId) =>
        BudgetLinePermissions.CanEdit(currentUser, version.Status, departmentId, version.IsDepartmentSubmitted(departmentId));

    private static PositionDto Priced(Guid id, PositionDetails details, PayrollRules? rules)
    {
        if (rules is null)
        {
            return new PositionDto(id, details, null, "The year has no personnel settings.");
        }

        IReadOnlyList<(string Field, string Message)> problems = details.Problems(rules);
        return problems.Count > 0
            ? new PositionDto(id, details, null, problems[0].Message)
            : new PositionDto(id, details, PositionCostCalculator.Calculate(details, rules), null);
    }

    /// <summary>Whether the stored calculated lines equal what the positions cost now.</summary>
    private static bool Matches(List<BudgetLine> lines, List<PositionDto> positions)
    {
        var costs = positions
            .SelectMany(p => p.Cost!.ByFund.Where(c => c.Amount != 0m))
            .GroupBy(c => (c.FundId, c.AccountId))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        return costs.Count == lines.Count
            && lines.All(l => costs.TryGetValue((l.FundId, l.AccountId), out decimal amount) && amount == l.Amount);
    }
}
