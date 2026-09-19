using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Budgets;

public sealed class BudgetEntryService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IValidator<AddBudgetLineRequest> addValidator) : IBudgetEntryService
{
    private const string NotAllowed = "You do not have permission to change this line.";

    public async Task<IReadOnlyList<BudgetVersionSummaryDto>> ListVersionsAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.BudgetVersions
            .Join(db.FiscalYears, v => v.FiscalYearId, fy => fy.Id, (v, fy) => new { v, fy })
            .OrderByDescending(x => x.fy.Year).ThenByDescending(x => x.v.VersionNumber)
            .Select(x => new BudgetVersionSummaryDto(
                x.v.Id, x.fy.Year, x.v.VersionNumber,
                x.v.VersionNumber == 1 ? "Original" : "Amendment " + (x.v.VersionNumber - 1),
                x.v.Status, x.v.AmendmentReason, x.v.ResolutionNumber,
                db.BudgetLines.Count(l => l.BudgetVersionId == x.v.Id)))
            .ToListAsync(ct);
    }

    public async Task<BudgetWorkspaceDto?> GetWorkspaceAsync(Guid versionId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);

        BudgetVersion? version = await LoadVersionAsync(db, versionId, ct);
        if (version is null)
        {
            return null;
        }

        FiscalYear fiscalYear = await db.FiscalYears.SingleAsync(fy => fy.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);

        // department users see only their departments' lines; everyone else sees the whole version.
        bool isDepartmentHead = currentUser.IsDepartmentUser();
        IEnumerable<BudgetLine> visible = isDepartmentHead
            ? version.Lines.Where(l => l.DepartmentId is { } d && currentUser.DepartmentIds.Contains(d))
            : version.Lines;

        List<BudgetLineDto> lines = visible
            .OrderBy(l => l.Fund.Code).ThenBy(l => l.Department?.Code).ThenBy(l => l.Account.Code)
            .Select(l => ToDto(l, government.AccountNumberFormat, BudgetLinePermissions.CanEdit(currentUser, version.Status, l.DepartmentId)))
            .ToList();

        // Fund balances always use every line in the version, not just the visible ones: a Department
        // Head's fund total must reflect the whole fund or the appropriation check would be meaningless.
        // Active funds plus any inactive fund that still has lines in this version (history must keep its fund).
        List<Guid> usedFundIds = version.Lines.Select(l => l.FundId).Distinct().ToList();
        Dictionary<Guid, Fund> fundsById = await db.Funds
            .Where(f => f.IsActive || usedFundIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);
        bool canEditBalances = BudgetLinePermissions.CanEditBeginningBalances(currentUser, version.Status);
        List<FundBalanceDto> balances = FundBalanceCalculator.CalculateAll(version)
            .Concat(fundsById.Values.Where(f => version.Lines.All(l => l.FundId != f.Id) && version.BeginningBalances.All(b => b.FundId != f.Id))
                .Select(f => FundBalanceCalculator.Calculate(f.Id, 0m, [])))
            .Select(summary =>
            {
                Fund fund = fundsById[summary.FundId];
                return new FundBalanceDto(
                    fund.Id, fund.Code, fund.Name, fund.Category, summary,
                    AppropriationLimitCheck.Evaluate(summary, government.AppropriationLimitMode),
                    canEditBalances);
            })
            .OrderBy(b => b.FundCode)
            .ToList();

        List<LookupDto> funds = fundsById.Values.Where(f => f.IsActive).OrderBy(f => f.Code).Select(f => new LookupDto(f.Id, f.Code, f.Name)).ToList();
        List<LookupDto> departments = await db.Departments.Where(d => d.IsActive).OrderBy(d => d.Code)
            .Select(d => new LookupDto(d.Id, d.Code, d.Name)).ToListAsync(ct);
        List<AccountLookupDto> accounts = await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Code)
            .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type, a.Category)).ToListAsync(ct);

        if (isDepartmentHead)
        {
            departments = departments.Where(d => currentUser.DepartmentIds.Contains(d.Id)).ToList();
        }

        bool canAddLines = version.IsEditable
            && (currentUser.IsFiscalAuthority() || (isDepartmentHead && version.Status == BudgetStatus.Draft));

        return new BudgetWorkspaceDto(
            ToSummary(version, fiscalYear.Year, version.Lines.Count),
            government.AccountNumberFormat,
            version.IsEditable, canAddLines, lines, balances, funds, departments, accounts);
    }

    public async Task<Result> UpdateLineAmountAsync(Guid versionId, Guid lineId, decimal amount, CancellationToken ct = default)
    {
        if (amount < 0m)
        {
            return Result.Failure(nameof(amount), "Budgeted amounts cannot be negative.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (BudgetVersion version, BudgetLine line, Result? denied) = await LoadLineForEditAsync(db, versionId, lineId, ct);
        if (denied is not null)
        {
            return denied;
        }

        version.UpdateLineAmount(line.Id, amount);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateLineJustificationAsync(Guid versionId, Guid lineId, string? justification, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (BudgetVersion version, BudgetLine line, Result? denied) = await LoadLineForEditAsync(db, versionId, lineId, ct);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            version.UpdateLineJustification(line.Id, justification);
        }
        catch (DomainException ex)
        {
            return Result.Failure(nameof(justification), ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> AddLineAsync(AddBudgetLineRequest request, CancellationToken ct = default)
    {
        if (await addValidator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return Result.Failure<Guid>(invalid.Errors);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadVersionAsync(db, request.BudgetVersionId, ct);
        if (version is null)
        {
            return Result.Failure<Guid>("Budget version was not found.");
        }

        if (!BudgetLinePermissions.CanAddLine(currentUser, version.Status, request.DepartmentId))
        {
            return Result.Failure<Guid>(NotAllowed);
        }

        Fund? fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == request.FundId, ct);
        Account? account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);
        Department? department = request.DepartmentId is { } departmentId
            ? await db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId, ct)
            : null;
        if (fund is null || account is null || (request.DepartmentId is not null && department is null))
        {
            return Result.Failure<Guid>("Fund, department, or account was not found.");
        }

        BudgetLine line;
        try
        {
            line = version.AddLine(fund, department, account, request.Amount, request.PriorYearActual, request.CurrentYearBudget, request.Justification);
        }
        catch (DomainException ex)
        {
            // The domain's rules (duplicate line, expenditure without department, inactive account) become field errors.
            return Result.Failure<Guid>(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(line.Id);
    }

    public async Task<Result> RemoveLineAsync(Guid versionId, Guid lineId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (BudgetVersion version, BudgetLine line, Result? denied) = await LoadLineForEditAsync(db, versionId, lineId, ct);
        if (denied is not null)
        {
            return denied;
        }

        version.RemoveLine(line.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetBeginningBalanceAsync(Guid versionId, Guid fundId, decimal amount, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadVersionAsync(db, versionId, ct);
        if (version is null)
        {
            return Result.Failure("Budget version was not found.");
        }

        if (!BudgetLinePermissions.CanEditBeginningBalances(currentUser, version.Status))
        {
            return Result.Failure("Only an Administrator or the Fiscal Officer can set beginning balances, and only before adoption.");
        }

        Fund? fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == fundId, ct);
        if (fund is null)
        {
            return Result.Failure("Fund was not found.");
        }

        version.SetBeginningBalance(fund, amount);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>The whole aggregate with the navigations the domain methods and DTOs need.</summary>
    private static Task<BudgetVersion?> LoadVersionAsync(ICivicBudgetDbContext db, Guid versionId, CancellationToken ct) =>
        db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.BeginningBalances)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);

    /// <summary>Loads version and line, and returns a failed Result when the user may not edit that line.</summary>
    private async Task<(BudgetVersion Version, BudgetLine Line, Result? Denied)> LoadLineForEditAsync(
        ICivicBudgetDbContext db, Guid versionId, Guid lineId, CancellationToken ct)
    {
        BudgetVersion? version = await LoadVersionAsync(db, versionId, ct);
        BudgetLine? line = version?.Lines.FirstOrDefault(l => l.Id == lineId);
        if (version is null || line is null)
        {
            return (null!, null!, Result.Failure("Budget line was not found."));
        }

        if (!BudgetLinePermissions.CanEdit(currentUser, version.Status, line.DepartmentId))
        {
            return (version, line, Result.Failure(NotAllowed));
        }

        return (version, line, null);
    }

    private static BudgetVersionSummaryDto ToSummary(BudgetVersion v, int year, int lineCount) =>
        new(v.Id, year, v.VersionNumber, v.Label, v.Status, v.AmendmentReason, v.ResolutionNumber, lineCount);

    private static BudgetLineDto ToDto(BudgetLine l, AccountNumberFormat format, bool canEdit) => new(
        l.Id,
        l.FundId, l.Fund.Code, l.Fund.Name,
        l.DepartmentId, l.Department?.Code, l.Department?.Name,
        l.AccountId, l.Account.Code, l.Account.Name,
        AccountNumber.Compose(format, l.Fund.Code, l.Department?.Code, l.Account.Code),
        l.Account.Type, l.Account.Category,
        l.Amount, l.PriorYearActual, l.CurrentYearBudget, l.Justification, canEdit);
}
