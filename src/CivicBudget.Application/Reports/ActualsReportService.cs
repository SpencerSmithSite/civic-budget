using System.Globalization;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Reports;

/// <summary>
/// The reports that combine the ERP's books with the budget, which the ERP cannot produce alone, and
/// the appropriation measure. Budget against actual and revenue against receipts follow the same
/// visibility as the workspace (a department user sees their departments). The fund projection, the
/// trends, and the appropriation measure cover whole funds, so department users do not get them.
/// Null means not found, or not available to this user.
/// </summary>
public interface IActualsReportService
{
    Task<BudgetActualReportDto?> BudgetVsActualAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<RevenueReceiptReportDto?> RevenueVsReceiptsAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<FundProjectionReportDto?> FundProjectionAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<TrendReportDto?> TrendsAsync(Guid budgetVersionId, CancellationToken ct = default);

    Task<AppropriationMeasureDto?> AppropriationMeasureAsync(Guid budgetVersionId, CancellationToken ct = default);
}

public sealed class ActualsReportService(
    IBudgetEntryService entry,
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : IActualsReportService
{
    /// <summary>The trend report looks back this many years before the budget's own.</summary>
    public const int TrendYearsBack = 3;

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public async Task<BudgetActualReportDto?> BudgetVsActualAsync(Guid budgetVersionId, CancellationToken ct = default) =>
        await LoadAsync(budgetVersionId, wholeFundsOnly: false, ct) is { } l ? ActualsReportBuilder.BudgetVsActual(l.Header, l.Period, l.Lines) : null;

    public async Task<RevenueReceiptReportDto?> RevenueVsReceiptsAsync(Guid budgetVersionId, CancellationToken ct = default) =>
        await LoadAsync(budgetVersionId, wholeFundsOnly: false, ct) is { } l ? ActualsReportBuilder.RevenueVsReceipts(l.Header, l.Period, l.Lines) : null;

    public async Task<FundProjectionReportDto?> FundProjectionAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        if (await LoadAsync(budgetVersionId, wholeFundsOnly: true, ct) is not { } l)
        {
            return null;
        }

        return ActualsReportBuilder.FundProjection(l.Header, l.Period, l.Lines,
            l.Workspace.FundBalances.ToDictionary(f => f.FundCode, f => f.Summary.BeginningBalance),
            l.Workspace.FundBalances.Select(f => (f.FundCode, f.FundName)).ToList());
    }

    public async Task<TrendReportDto?> TrendsAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        if (await LoadAsync(budgetVersionId, wholeFundsOnly: true, ct) is not { } l)
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int thisYear = l.Workspace.Version.Year;
        Dictionary<Guid, (string Code, string Name)> funds = await db.Funds.ToDictionaryAsync(f => f.Id, f => (f.Code, f.Name), ct);
        var years = new List<TrendYearDto>();
        var cells = new Dictionary<(Guid Fund, int Year), TrendCellDto>();

        for (int year = thisYear - TrendYearsBack; year <= thisYear; year++)
        {
            // The budget for each year: this report's version for its own year, the latest adopted version for the years before.
            List<(Guid FundId, AccountType Type, decimal Amount)> budget = year == thisYear
                ? l.Workspace.Lines.Select(x => (x.FundId, x.AccountType, x.Amount)).ToList()
                : await AdoptedBudgetAsync(db, year, ct);
            string? budgetLabel = year == thisYear ? $"{l.Workspace.Version.Label}{(l.Workspace.Version.Status == BudgetStatus.Adopted ? "" : $" ({l.Workspace.Version.Status.ToString().ToLowerInvariant()})")}"
                : budget.Count > 0 ? "Adopted" : null;

            ActualsSync? sync = await ErpActualsReader.LatestSyncAsync(db, year, ct);
            var actuals = sync is null
                ? []
                : await db.ErpActuals.Where(a => a.FiscalYear == year)
                    .Join(db.Accounts, a => a.AccountId, acc => acc.Id, (a, acc) => new { a.FundId, acc.Type, a.Amount })
                    .GroupBy(x => new { x.FundId, x.Type })
                    .Select(g => new { g.Key.FundId, g.Key.Type, Amount = g.Sum(x => x.Amount) })
                    .ToListAsync(ct);

            if (budgetLabel is null && sync is null)
            {
                continue;
            }

            years.Add(new TrendYearDto(year, budgetLabel ?? "No budget", sync is null ? null : sync.IsYearClosed ? "full year" : $"to {sync.AsOf.ToString("MMM d, yyyy", Us)}"));
            foreach (Guid fundId in budget.Select(b => b.FundId).Concat(actuals.Select(a => a.FundId)).Distinct())
            {
                cells[(fundId, year)] = new TrendCellDto(year,
                    budgetLabel is null ? null : budget.Where(b => b.FundId == fundId && b.Type.IsResource()).Sum(b => b.Amount),
                    sync is null ? null : actuals.Where(a => a.FundId == fundId && a.Type.IsResource()).Sum(a => a.Amount),
                    budgetLabel is null ? null : budget.Where(b => b.FundId == fundId && b.Type.IsAppropriation()).Sum(b => b.Amount),
                    sync is null ? null : actuals.Where(a => a.FundId == fundId && a.Type.IsAppropriation()).Sum(a => a.Amount));
            }
        }

        List<TrendFundDto> rows = cells.Keys.Select(k => k.Fund).Distinct()
            .Select(id => new TrendFundDto(funds[id].Code, funds[id].Name,
                years.Select(y => cells.GetValueOrDefault((id, y.FiscalYear)) ?? new TrendCellDto(y.FiscalYear, null, null, null, null)).ToList()))
            .OrderBy(f => f.FundCode, StringComparer.Ordinal)
            .ToList();
        List<TrendCellDto> totals = years.Select(y =>
        {
            var inYear = rows.Select(r => r.Years.Single(c => c.FiscalYear == y.FiscalYear)).ToList();
            return new TrendCellDto(y.FiscalYear, SumOrNull(inYear, c => c.BudgetedReceipts), SumOrNull(inYear, c => c.ActualReceipts),
                SumOrNull(inYear, c => c.Appropriations), SumOrNull(inYear, c => c.ActualSpending));
        }).ToList();

        return new TrendReportDto(l.Header, years, rows, totals);
    }

    public async Task<AppropriationMeasureDto?> AppropriationMeasureAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        if (await LoadAsync(budgetVersionId, wholeFundsOnly: true, ct) is not { } l)
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        (List<(string Label, HashSet<Guid> Accounts)> columns, bool isDefault) = await MeasureColumnService.ColumnsAsync(db, ct);
        Dictionary<string, Domain.Funds.FundCategory> categories = l.Workspace.FundBalances.ToDictionary(f => f.FundCode, f => f.Category);
        return AppropriationMeasureBuilder.Build(l.Header, columns, isDefault,
            l.Workspace.Lines.Select(x => (x.FundCode, x.FundName, categories[x.FundCode], x.DepartmentCode, x.DepartmentName, x.AccountId, x.AccountType, x.Amount)).ToList());
    }

    // ---- loading ------------------------------------------------------------------------------

    private sealed record Loaded(ReportHeaderDto Header, BudgetWorkspaceDto Workspace, ActualsPeriodDto? Period, List<ActualsLine> Lines);

    /// <summary>
    /// The workspace read (tenant filter and department visibility), then the ERP's figures for the
    /// budget's year and last year's for pacing, added up per line with the same rules as the sync.
    /// </summary>
    private async Task<Loaded?> LoadAsync(Guid budgetVersionId, bool wholeFundsOnly, CancellationToken ct)
    {
        if (wholeFundsOnly && currentUser.IsDepartmentUser())
        {
            return null;
        }

        BudgetWorkspaceDto? workspace = await entry.GetWorkspaceAsync(budgetVersionId, ct);
        if (workspace is null)
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == currentUser.GovernmentId, ct);
        var header = new ReportHeaderDto(budgetVersionId, government.Name, workspace.Version.Year, workspace.Version.Label, workspace.Version.Status,
            workspace.Version.ResolutionNumber, currentUser.DisplayName ?? "", clock.GetUtcNow());

        int year = workspace.Version.Year;
        var keys = workspace.Lines.Select(x => new LineKey(x.FundId, x.DepartmentId, x.AccountId)).ToList();
        ActualsSync? sync = await ErpActualsReader.LatestSyncAsync(db, year, ct);
        ActualsPeriodDto? period = sync is null ? null : new ActualsPeriodDto(year, sync.ThroughPeriod, sync.AsOf);

        Dictionary<LineKey, decimal> actual = [], encumbered = [];
        Dictionary<LineKey, decimal>? priorFull = null, priorAt = null;
        if (period is not null)
        {
            actual = await ErpActualsReader.ActivityTotalsAsync(db, year, keys, ct);
            encumbered = await ErpActualsReader.EncumbranceTotalsAsync(db, year, keys, ct);
            if (await ErpActualsReader.LatestSyncAsync(db, year - 1, ct) is { ThroughPeriod: 12 })
            {
                priorFull = await ErpActualsReader.ActivityTotalsAsync(db, year - 1, keys, ct);
                priorAt = await ErpActualsReader.ActivityThroughPeriodAsync(db, year - 1, period.ThroughPeriod, keys, ct);
            }
        }

        List<ActualsLine> lines = workspace.Lines.Select(x =>
        {
            var key = new LineKey(x.FundId, x.DepartmentId, x.AccountId);
            return new ActualsLine(key, x.AccountNumber, x.AccountName, x.FundCode, x.FundName, x.DepartmentCode, x.DepartmentName, x.AccountType, x.Amount,
                actual.GetValueOrDefault(key), encumbered.GetValueOrDefault(key),
                priorAt is null ? null : priorAt.GetValueOrDefault(key), priorFull is null ? null : priorFull.GetValueOrDefault(key));
        }).ToList();

        return new Loaded(header, workspace, period, lines);
    }

    /// <summary>A past year's budget: the latest adopted version, the same one the portal publishes.</summary>
    private static async Task<List<(Guid FundId, AccountType Type, decimal Amount)>> AdoptedBudgetAsync(ICivicBudgetDbContext db, int year, CancellationToken ct)
    {
        var lines = await db.BudgetVersions
            .Where(v => v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null && db.FiscalYears.Any(fy => fy.Id == v.FiscalYearId && fy.Year == year))
            .SelectMany(v => v.Lines)
            .Select(x => new { x.FundId, x.Account.Type, x.Amount })
            .ToListAsync(ct);
        return lines.Select(x => (x.FundId, x.Type, x.Amount)).ToList();
    }

    private static decimal? SumOrNull(IEnumerable<TrendCellDto> cells, Func<TrendCellDto, decimal?> pick)
    {
        var values = cells.Select(pick).ToList();
        return values.All(v => v is null) ? null : values.Sum(v => v ?? 0m);
    }
}
