using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Budgets.Planning;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Budgets;

/// <summary>One future year's percentages. YearOffset 1 is the year after the budget year.</summary>
public sealed record PlanRateDto(int YearOffset, int FiscalYear, decimal RevenuePercent, decimal ExpenditurePercent);

/// <summary>One fund in one year of the plan, with the Ohio limit check for that year.</summary>
public sealed record PlanFundYearDto(int FiscalYear, decimal BeginningBalance, decimal Revenues, decimal TransfersIn,
    decimal Expenditures, decimal TransfersOut, decimal EndingBalance, bool OverLimit, decimal AmountOverLimit)
{
    public decimal EstimatedResources => BeginningBalance + Revenues + TransfersIn;
    public decimal Appropriations => Expenditures + TransfersOut;
}

public sealed record PlanFundDto(Guid FundId, string FundCode, string FundName, IReadOnlyList<PlanFundYearDto> Years);

/// <summary>A line in every year of the plan (index 0 is the budget year), which years were typed, and whether this user may change them.</summary>
public sealed record PlanLineDto(Guid LineId, string FundCode, string FundName, Guid? DepartmentId, string? DepartmentCode, string? DepartmentName,
    string AccountCode, string AccountName, string AccountNumber, AccountType AccountType, bool CanEdit,
    IReadOnlyList<decimal> Amounts, IReadOnlyList<bool> Typed);

public sealed record BudgetPlanDto(
    Guid VersionId,
    int BudgetYear,
    string VersionLabel,
    BudgetStatus Status,
    int Years,
    bool WholeDollars,
    IReadOnlyList<PlanRateDto> Rates,
    bool CanEditAssumptions,
    IReadOnlyList<PlanFundDto> Funds,
    IReadOnlyList<PlanLineDto> Lines)
{
    public IReadOnlyList<int> FiscalYears => [.. Enumerable.Range(BudgetYear, Years)];
}

/// <summary>Saves the plan's length, rounding, and each future year's percentages together.</summary>
public sealed record SavePlanRequest(Guid VersionId, int Years, bool WholeDollars, IReadOnlyList<PlanRateDto> Rates);

/// <summary>
/// The multi-year plan of a budget version: read it worked out, set its assumptions, and type over a
/// line's future year. Department users see and change only their own departments' lines, under the
/// same rule as this year's amounts, and see no fund balances (a fund spans departments).
/// </summary>
public interface IBudgetPlanService
{
    Task<BudgetPlanDto?> GetAsync(Guid versionId, CancellationToken ct = default);

    Task<Result> SavePlanAsync(SavePlanRequest request, CancellationToken ct = default);

    /// <summary>Types over one future year of a line, or with a null amount goes back to the calculation.</summary>
    Task<Result> SetPlannedAmountAsync(Guid versionId, Guid lineId, int yearOffset, decimal? amount, CancellationToken ct = default);
}

public sealed class BudgetPlanService(ICivicBudgetDbContextFactory dbFactory, ICurrentUser currentUser) : IBudgetPlanService
{
    public async Task<BudgetPlanDto?> GetAsync(Guid versionId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadAsync(db, versionId, ct);
        if (version is null)
        {
            return null;
        }

        FiscalYear fiscalYear = await db.FiscalYears.SingleAsync(f => f.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        MultiYearProjection plan = MultiYearPlanCalculator.Project(version);
        Dictionary<Guid, PlanLineProjection> byLine = plan.Lines.ToDictionary(l => l.LineId);

        bool departmentUser = currentUser.IsDepartmentUser();
        IEnumerable<BudgetLine> visible = departmentUser
            ? version.Lines.Where(l => l.DepartmentId is { } d && currentUser.DepartmentIds.Contains(d))
            : version.Lines;

        List<PlanLineDto> lines = [.. visible
            .OrderBy(l => l.Fund.Code).ThenBy(l => l.Department?.Code).ThenBy(l => l.Account.Code)
            .Select(l => new PlanLineDto(
                l.Id, l.Fund.Code, l.Fund.Name, l.DepartmentId, l.Department?.Code, l.Department?.Name,
                l.Account.Code, l.Account.Name, AccountNumber.Compose(government.AccountNumberFormat, l.Fund.Code, l.Department?.Code, l.Account.Code),
                l.Account.Type, CanEdit(version, l), byLine[l.Id].Amounts, byLine[l.Id].Typed))];

        // A fund with only a beginning balance has no line to reach it through, so names come from the funds.
        List<Guid> fundIds = [.. plan.Funds.Select(f => f.FundId).Distinct()];
        Dictionary<Guid, (string Code, string Name)> fundNames = departmentUser ? [] : await db.Funds
            .Where(f => fundIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => (f.Code, f.Name), ct);
        List<PlanFundDto> funds = departmentUser ? [] : [.. plan.Funds
            .GroupBy(f => f.FundId)
            .Select(g =>
            {
                (string code, string name) = fundNames[g.Key];
                return new PlanFundDto(g.Key, code, name, [.. g.OrderBy(y => y.YearOffset).Select(y => new PlanFundYearDto(
                    fiscalYear.Year + y.YearOffset, y.Summary.BeginningBalance, y.Summary.Revenues, y.Summary.TransfersIn,
                    y.Summary.Expenditures, y.Summary.TransfersOut, y.Summary.ProjectedEndingBalance,
                    !y.Summary.IsWithinAppropriationLimit, y.Summary.AmountOverLimit))]);
            })
            .OrderBy(f => f.FundCode)];

        Dictionary<int, PlanAssumption> assumptions = version.PlanAssumptions.ToDictionary(a => a.YearOffset);
        List<PlanRateDto> rates = [.. Enumerable.Range(1, Math.Max(0, version.PlanYears - 1)).Select(offset =>
            assumptions.TryGetValue(offset, out PlanAssumption? a)
                ? new PlanRateDto(offset, fiscalYear.Year + offset, a.RevenuePercent, a.ExpenditurePercent)
                : new PlanRateDto(offset, fiscalYear.Year + offset, 0m, 0m))];

        return new BudgetPlanDto(version.Id, fiscalYear.Year, version.Label, version.Status, version.PlanYears, version.PlanInWholeDollars,
            rates, CanEditAssumptions(version), funds, lines);
    }

    public async Task<Result> SavePlanAsync(SavePlanRequest request, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadAsync(db, request.VersionId, ct);
        if (version is null)
        {
            return Result.Failure("Budget version was not found.");
        }

        if (!CanEditAssumptions(version))
        {
            return Result.Failure("Only an Administrator or Fiscal Officer can change the plan's years and percentages, and only before adoption.");
        }

        try
        {
            version.SetPlan(request.Years, request.WholeDollars,
                [.. request.Rates.Where(r => r.YearOffset < request.Years).Select(r => new PlanRate(r.YearOffset, r.RevenuePercent, r.ExpenditurePercent))]);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        return await db.TrySaveAsync(ct) ?? Result.Success();
    }

    public async Task<Result> SetPlannedAmountAsync(Guid versionId, Guid lineId, int yearOffset, decimal? amount, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadAsync(db, versionId, ct);
        BudgetLine? line = version?.Lines.FirstOrDefault(l => l.Id == lineId);
        if (version is null || line is null)
        {
            return Result.Failure("Budget line was not found.");
        }

        if (!CanEdit(version, line))
        {
            return Result.Failure("You do not have permission to change this line.");
        }

        try
        {
            version.SetPlannedAmount(lineId, yearOffset, amount);
        }
        catch (DomainException ex)
        {
            return Result.Failure(nameof(amount), ex.Message);
        }

        return await db.TrySaveAsync(ct) ?? Result.Success();
    }

    // A department user plans their own lines the way they enter this year's (only while the round is
    // open for them); the fiscal authority any line until adoption.
    private bool CanEdit(BudgetVersion version, BudgetLine line) =>
        BudgetLinePermissions.CanEdit(currentUser, version.Status, line.DepartmentId,
            line.DepartmentId is { } departmentId && version.IsDepartmentSubmitted(departmentId));

    private bool CanEditAssumptions(BudgetVersion version) => version.IsEditable && currentUser.IsFiscalAuthority();

    private static Task<BudgetVersion?> LoadAsync(ICivicBudgetDbContext db, Guid versionId, CancellationToken ct) =>
        db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.Lines).ThenInclude(l => l.PlannedAmounts)
            .Include(v => v.BeginningBalances)
            .Include(v => v.DepartmentRequests)
            .Include(v => v.PlanAssumptions)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
}
