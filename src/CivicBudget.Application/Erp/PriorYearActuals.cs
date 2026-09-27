using CivicBudget.Application.Persistence;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Erp;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Erp;

/// <summary>One line whose prior-year actual an ERP figure replaces.</summary>
public sealed record PriorActualChange(BudgetLine Line, decimal Before, decimal After);

/// <summary>
/// A budget for FY Y compares against FY Y-2's actuals (the last year whose books are closed while
/// Y is being prepared) and FY Y-1's budget. Once the ERP has sent Y-2 as a closed year, it is the
/// source of that column: a sync writes it into every open version of Y, and starting a budget or
/// adding a line takes it from there. A year the ERP has only sent part of never fills the column,
/// because eight months of spending posing as a full year's actual would mislead every comparison.
/// </summary>
public static class PriorYearActuals
{
    /// <summary>The year whose actuals a budget for <paramref name="budgetYear"/> compares against.</summary>
    public static int ActualsYearFor(int budgetYear) => budgetYear - 2;

    /// <summary>The ERP's totals for these lines in a closed year, or null when that year has not arrived closed.</summary>
    public static async Task<Dictionary<LineKey, decimal>?> ClosedYearTotalsAsync(ICivicBudgetDbContext db, int fiscalYear, IEnumerable<LineKey> lines, CancellationToken ct) =>
        await ErpActualsReader.LatestSyncAsync(db, fiscalYear, ct) is { ThroughPeriod: 12 }
            ? await ErpActualsReader.ActivityTotalsAsync(db, fiscalYear, lines, ct)
            : null;

    /// <summary>Fills a version's prior-year actuals from the ERP when it holds that year closed; returns how many changed.</summary>
    public static async Task<int> FillAsync(ICivicBudgetDbContext db, BudgetVersion version, int budgetYear, CancellationToken ct)
    {
        Dictionary<LineKey, decimal>? totals = await ClosedYearTotalsAsync(
            db, ActualsYearFor(budgetYear), version.Lines.Select(l => new LineKey(l.FundId, l.DepartmentId, l.AccountId)), ct);
        return totals is null ? 0 : Apply(version, totals).Count;
    }

    /// <summary>
    /// Writes the totals into the version's lines and returns what changed. A negative total (refunds
    /// larger than the year's receipts) is left alone and reported: budget amounts are never
    /// negative, and a zero would hide the oddity instead of showing it.
    /// </summary>
    public static List<PriorActualChange> Apply(BudgetVersion version, IReadOnlyDictionary<LineKey, decimal> totals, List<string>? notes = null)
    {
        var changes = new List<PriorActualChange>();
        foreach (BudgetLine line in version.Lines)
        {
            if (!totals.TryGetValue(new LineKey(line.FundId, line.DepartmentId, line.AccountId), out decimal total))
            {
                continue;
            }

            if (total < 0m)
            {
                notes?.Add($"{line.Fund.Code} {line.Department?.Code} {line.Account.Code} {line.Account.Name}: the ERP total is negative ({total:N2}), so its prior-year actual was left as it was.".Replace("  ", " ", StringComparison.Ordinal));
                continue;
            }

            if (total != line.PriorYearActual)
            {
                changes.Add(new PriorActualChange(line, line.PriorYearActual, total));
                version.UpdateLineComparatives(line.Id, total, line.CurrentYearBudget);
            }
        }

        return changes;
    }
}

/// <summary>Reads the ERP's figures CivicBudget holds, added up per budget line.</summary>
public static class ErpActualsReader
{
    /// <summary>Latest sync of a fiscal year, or null when the ERP has not sent it.</summary>
    public static Task<ActualsSync?> LatestSyncAsync(ICivicBudgetDbContext db, int fiscalYear, CancellationToken ct) =>
        db.ActualsSyncs.Where(s => s.FiscalYear == fiscalYear).OrderByDescending(s => s.SyncedAtUtc).FirstOrDefaultAsync(ct);

    /// <summary>Receipts or spending per line for the year, through whatever month the last sync reached.</summary>
    public static async Task<Dictionary<LineKey, decimal>> ActivityTotalsAsync(ICivicBudgetDbContext db, int fiscalYear, IEnumerable<LineKey> lines, CancellationToken ct)
    {
        var rows = await db.ErpActuals.Where(a => a.FiscalYear == fiscalYear)
            .GroupBy(a => new { a.FundId, a.DepartmentId, a.AccountId })
            .Select(g => new { g.Key.FundId, g.Key.DepartmentId, g.Key.AccountId, Amount = g.Sum(a => a.Amount) })
            .ToListAsync(ct);
        return ActualsByLine.Sum(lines, rows.Select(r => (new LineKey(r.FundId, r.DepartmentId, r.AccountId), r.Amount)));
    }

    /// <summary>Receipts or spending per line through a fiscal month: last year "at this point", for pacing this year.</summary>
    public static async Task<Dictionary<LineKey, decimal>> ActivityThroughPeriodAsync(ICivicBudgetDbContext db, int fiscalYear, int throughPeriod, IEnumerable<LineKey> lines, CancellationToken ct)
    {
        var rows = await db.ErpActuals.Where(a => a.FiscalYear == fiscalYear && a.Period <= throughPeriod)
            .GroupBy(a => new { a.FundId, a.DepartmentId, a.AccountId })
            .Select(g => new { g.Key.FundId, g.Key.DepartmentId, g.Key.AccountId, Amount = g.Sum(a => a.Amount) })
            .ToListAsync(ct);
        return ActualsByLine.Sum(lines, rows.Select(r => (new LineKey(r.FundId, r.DepartmentId, r.AccountId), r.Amount)));
    }

    /// <summary>Open encumbrances per line as of the year's last sync.</summary>
    public static async Task<Dictionary<LineKey, decimal>> EncumbranceTotalsAsync(ICivicBudgetDbContext db, int fiscalYear, IEnumerable<LineKey> lines, CancellationToken ct)
    {
        var rows = await db.ErpEncumbrances.Where(e => e.FiscalYear == fiscalYear)
            .Select(e => new { e.FundId, e.DepartmentId, e.AccountId, e.Amount })
            .ToListAsync(ct);
        return ActualsByLine.Sum(lines, rows.Select(r => (new LineKey(r.FundId, r.DepartmentId, r.AccountId), r.Amount)));
    }
}
