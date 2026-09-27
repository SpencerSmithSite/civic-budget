using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Reports;

/// <summary>How far the ERP's figures for the budget's year run.</summary>
public sealed record ActualsPeriodDto(int FiscalYear, int ThroughPeriod, DateOnly AsOf)
{
    public bool IsClosed => ThroughPeriod == 12;

    /// <summary>The share of the year gone: the pace an evenly spent line would be at.</summary>
    public decimal Pace => ThroughPeriod / 12m;
}

// ---- budget against actual ------------------------------------------------------------------

/// <summary>One appropriation line, or a subtotal, against what the ERP says was spent and committed.</summary>
public sealed record BudgetActualRowDto(string AccountNumber, string Label, decimal Budget, decimal Actual, decimal Encumbered, decimal? LastYearAtThisPoint)
{
    public decimal Remaining => Budget - Actual - Encumbered;

    /// <summary>Spent and committed as a share of the budget; null when there is no budget.</summary>
    public decimal? Used => Budget == 0m ? null : (Actual + Encumbered) / Budget;

    public bool IsOver => Actual + Encumbered > Budget;
}

public sealed record BudgetActualGroupDto(string Label, IReadOnlyList<BudgetActualRowDto> Lines, BudgetActualRowDto Subtotal);

public sealed record BudgetActualFundDto(string FundCode, string FundName, IReadOnlyList<BudgetActualGroupDto> Departments, BudgetActualRowDto Subtotal);

/// <param name="Period">Null when the ERP has sent nothing for the budget's year.</param>
public sealed record BudgetActualReportDto(ReportHeaderDto Header, ActualsPeriodDto? Period, IReadOnlyList<BudgetActualFundDto> Funds, BudgetActualRowDto Total);

// ---- revenue against receipts ---------------------------------------------------------------

/// <param name="NormallyByNow">The share of last year's receipts that had arrived by the same month; null without last year's books.</param>
public sealed record RevenueReceiptRowDto(string AccountNumber, string Label, decimal Estimate, decimal Received, decimal? NormallyByNow)
{
    public decimal StillToCollect => Estimate - Received;
    public decimal? Collected => Estimate == 0m ? null : Received / Estimate;

    /// <summary>Ten points behind where the same account stood last year: real estate settlements arrive twice a year, so the calendar alone would cry wolf.</summary>
    public bool IsBehind => Collected is { } c && NormallyByNow is { } n && c < n - 0.10m;
}

public sealed record RevenueReceiptFundDto(string FundCode, string FundName, IReadOnlyList<RevenueReceiptRowDto> Lines, RevenueReceiptRowDto Subtotal);

public sealed record RevenueReceiptReportDto(ReportHeaderDto Header, ActualsPeriodDto? Period, IReadOnlyList<RevenueReceiptFundDto> Funds, RevenueReceiptRowDto Total);

// ---- projected fund balances ----------------------------------------------------------------

public sealed record FundProjectionRowDto(
    string FundCode,
    string FundName,
    decimal BeginningBalance,
    decimal BudgetedReceipts,
    decimal ReceivedToDate,
    decimal ProjectedReceipts,
    decimal Appropriations,
    decimal SpentToDate,
    decimal Encumbered,
    decimal ProjectedSpending)
{
    public decimal BudgetedEnding => BeginningBalance + BudgetedReceipts - Appropriations;
    public decimal ProjectedEnding => BeginningBalance + ProjectedReceipts - ProjectedSpending;
    public decimal Difference => ProjectedEnding - BudgetedEnding;
}

/// <param name="LinesProjectedFromBudget">Lines with no history to project from, carried at their budget instead.</param>
public sealed record FundProjectionReportDto(ReportHeaderDto Header, ActualsPeriodDto? Period, IReadOnlyList<FundProjectionRowDto> Funds, FundProjectionRowDto Total, int LinesProjectedFromBudget);

// ---- multi-year trends ----------------------------------------------------------------------

/// <param name="Actuals">"full year", "to Aug 31, 2026", or null when the ERP has not sent the year.</param>
public sealed record TrendYearDto(int FiscalYear, string BudgetLabel, string? Actuals);

/// <summary>One fund in one year. Actual figures are null when the ERP has not sent the year.</summary>
public sealed record TrendCellDto(int FiscalYear, decimal? BudgetedReceipts, decimal? ActualReceipts, decimal? Appropriations, decimal? ActualSpending);

public sealed record TrendFundDto(string FundCode, string FundName, IReadOnlyList<TrendCellDto> Years);

public sealed record TrendReportDto(ReportHeaderDto Header, IReadOnlyList<TrendYearDto> Years, IReadOnlyList<TrendFundDto> Funds, IReadOnlyList<TrendCellDto> Totals);

// ---- the appropriation measure --------------------------------------------------------------

/// <summary>One department's appropriation, split into the measure's columns and "Other".</summary>
public sealed record MeasureRowDto(string Label, IReadOnlyList<decimal> Columns, decimal Other)
{
    public decimal Total => Columns.Sum() + Other;
}

public sealed record MeasureFundDto(string FundCode, string FundName, FundCategory Category, IReadOnlyList<MeasureRowDto> Departments, decimal TransfersOut, MeasureRowDto Subtotal)
{
    public decimal Total => Subtotal.Total + TransfersOut;
}

public sealed record AppropriationMeasureDto(ReportHeaderDto Header, IReadOnlyList<string> ColumnLabels, IReadOnlyList<MeasureFundDto> Funds, MeasureRowDto DepartmentsTotal, decimal TransfersOut, bool UsingDefaultColumns)
{
    public decimal Total => DepartmentsTotal.Total + TransfersOut;
}

// ---- what the builder is given --------------------------------------------------------------

/// <summary>A budget line with the ERP's figures for its year and, when there are any, last year's.</summary>
public sealed record ActualsLine(
    LineKey Key,
    string AccountNumber,
    string AccountName,
    string FundCode,
    string FundName,
    string? DepartmentCode,
    string? DepartmentName,
    AccountType Type,
    decimal Budget,
    decimal Actual,
    decimal Encumbered,
    /// <summary>Last year's figure through the same fiscal month.</summary>
    decimal? PriorAtPeriod,
    /// <summary>Last year's whole-year figure, when that year is closed.</summary>
    decimal? PriorFull);

/// <summary>
/// The reports that put the ERP's books beside the budget, as pure functions over lines that already
/// carry both. The one judgment in here is how to project the rest of a year: a straight line from
/// eight months is wrong for anything seasonal (by August both real estate settlements are in), so
/// each line is projected from how much of last year's total had arrived by the same month.
/// </summary>
public static class ActualsReportBuilder
{
    public static BudgetActualReportDto BudgetVsActual(ReportHeaderDto header, ActualsPeriodDto? period, IReadOnlyList<ActualsLine> lines)
    {
        var appropriations = lines.Where(l => l.Type.IsAppropriation()).ToList();
        var funds = appropriations.GroupBy(l => (l.FundCode, l.FundName)).OrderBy(g => g.Key.FundCode, StringComparer.Ordinal)
            .Select(fund =>
            {
                // Lines without a department are transfers out, which Ohio appropriates as the fund's other financing uses.
                var departments = fund.GroupBy(l => l.DepartmentCode is null ? "Other financing uses" : $"{l.DepartmentCode} {l.DepartmentName}")
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(d =>
                    {
                        var rows = d.OrderBy(l => l.AccountNumber, StringComparer.Ordinal).Select(ToActualRow).ToList();
                        return new BudgetActualGroupDto(d.Key, rows, SumActual($"Total, {d.Key}", rows));
                    })
                    .ToList();
                return new BudgetActualFundDto(fund.Key.FundCode, fund.Key.FundName, departments, SumActual($"Total, {fund.Key.FundName}", departments.Select(d => d.Subtotal).ToList()));
            })
            .ToList();
        return new BudgetActualReportDto(header, period, funds, SumActual("All funds", funds.Select(f => f.Subtotal).ToList()));
    }

    public static RevenueReceiptReportDto RevenueVsReceipts(ReportHeaderDto header, ActualsPeriodDto? period, IReadOnlyList<ActualsLine> lines)
    {
        var funds = lines.Where(l => l.Type.IsResource()).GroupBy(l => (l.FundCode, l.FundName)).OrderBy(g => g.Key.FundCode, StringComparer.Ordinal)
            .Select(fund =>
            {
                var rows = fund.OrderBy(l => l.AccountNumber, StringComparer.Ordinal)
                    .Select(l => new RevenueReceiptRowDto(l.AccountNumber, l.AccountName, l.Budget, l.Actual, NormallyByNow(l)))
                    .ToList();
                return new RevenueReceiptFundDto(fund.Key.FundCode, fund.Key.FundName, rows, SumRevenue($"Total, {fund.Key.FundName}", fund.ToList()));
            })
            .ToList();
        return new RevenueReceiptReportDto(header, period, funds, SumRevenue("All funds", lines.Where(l => l.Type.IsResource()).ToList()));
    }

    public static FundProjectionReportDto FundProjection(ReportHeaderDto header, ActualsPeriodDto? period, IReadOnlyList<ActualsLine> lines,
        IReadOnlyDictionary<string, decimal> beginningBalances, IReadOnlyList<(string Code, string Name)> funds)
    {
        int fromBudget = 0;
        var rows = funds.OrderBy(f => f.Code, StringComparer.Ordinal).Select(f =>
        {
            var fundLines = lines.Where(l => l.FundCode == f.Code).ToList();
            decimal projectedReceipts = 0m, projectedSpending = 0m;
            foreach (ActualsLine line in fundLines)
            {
                (decimal projected, bool usedBudget) = Project(line, period);
                fromBudget += usedBudget ? 1 : 0;
                if (line.Type.IsResource())
                {
                    projectedReceipts += projected;
                }
                else
                {
                    // Money already committed will be spent whatever the pattern says.
                    projectedSpending += Math.Max(projected, line.Actual + line.Encumbered);
                }
            }

            return new FundProjectionRowDto(f.Code, f.Name, beginningBalances.GetValueOrDefault(f.Code),
                fundLines.Where(l => l.Type.IsResource()).Sum(l => l.Budget), fundLines.Where(l => l.Type.IsResource()).Sum(l => l.Actual), projectedReceipts,
                fundLines.Where(l => l.Type.IsAppropriation()).Sum(l => l.Budget), fundLines.Where(l => l.Type.IsAppropriation()).Sum(l => l.Actual),
                fundLines.Where(l => l.Type.IsAppropriation()).Sum(l => l.Encumbered), projectedSpending);
        }).ToList();

        var total = new FundProjectionRowDto("", "All funds", rows.Sum(r => r.BeginningBalance), rows.Sum(r => r.BudgetedReceipts), rows.Sum(r => r.ReceivedToDate),
            rows.Sum(r => r.ProjectedReceipts), rows.Sum(r => r.Appropriations), rows.Sum(r => r.SpentToDate), rows.Sum(r => r.Encumbered), rows.Sum(r => r.ProjectedSpending));
        return new FundProjectionReportDto(header, period, rows, total, period is null || period.IsClosed ? 0 : fromBudget);
    }

    /// <summary>
    /// Where a line will end the year. A closed year is its actual. Otherwise the year to date is
    /// scaled by last year's full year over last year at the same month. Without that history the
    /// budget stands in (never less than what has already happened), and the caller counts it.
    /// </summary>
    public static (decimal Projected, bool UsedBudget) Project(ActualsLine line, ActualsPeriodDto? period)
    {
        if (period is null)
        {
            return (line.Budget, false);
        }

        if (period.IsClosed)
        {
            return (line.Actual, false);
        }

        if (line.PriorFull is { } full && line.PriorAtPeriod is > 0m and { } atPeriod)
        {
            return (Domain.Common.Money.Round(line.Actual * full / atPeriod), false);
        }

        return (Math.Max(line.Budget, line.Actual), true);
    }

    private static decimal? NormallyByNow(ActualsLine line) =>
        line.PriorFull is > 0m and { } full && line.PriorAtPeriod is { } atPeriod ? atPeriod / full : null;

    private static BudgetActualRowDto ToActualRow(ActualsLine l) => new(l.AccountNumber, l.AccountName, l.Budget, l.Actual, l.Encumbered, l.PriorAtPeriod);

    private static BudgetActualRowDto SumActual(string label, IReadOnlyList<BudgetActualRowDto> rows) => new("", label,
        rows.Sum(r => r.Budget), rows.Sum(r => r.Actual), rows.Sum(r => r.Encumbered),
        rows.All(r => r.LastYearAtThisPoint is null) ? null : rows.Sum(r => r.LastYearAtThisPoint ?? 0m));

    private static RevenueReceiptRowDto SumRevenue(string label, IReadOnlyList<ActualsLine> lines)
    {
        decimal? priorFull = lines.All(l => l.PriorFull is null) ? null : lines.Sum(l => l.PriorFull ?? 0m);
        decimal? priorAt = lines.All(l => l.PriorAtPeriod is null) ? null : lines.Sum(l => l.PriorAtPeriod ?? 0m);
        return new RevenueReceiptRowDto("", label, lines.Sum(l => l.Budget), lines.Sum(l => l.Actual), priorFull is > 0m && priorAt is { } at ? at / priorFull.Value : null);
    }
}

/// <summary>The appropriation measure as a pure function over the version's appropriation lines.</summary>
public static class AppropriationMeasureBuilder
{
    /// <param name="lines">Appropriation lines: expenditures by department, and transfers out.</param>
    public static AppropriationMeasureDto Build(ReportHeaderDto header, IReadOnlyList<(string Label, HashSet<Guid> Accounts)> columns, bool usingDefault,
        IReadOnlyList<(string FundCode, string FundName, FundCategory Category, string? DepartmentCode, string? DepartmentName, Guid AccountId, AccountType Type, decimal Amount)> lines)
    {
        MeasureRowDto Row(string label, IEnumerable<(Guid AccountId, decimal Amount)> amounts)
        {
            var list = amounts.ToList();
            decimal[] byColumn = columns.Select(c => list.Where(a => c.Accounts.Contains(a.AccountId)).Sum(a => a.Amount)).ToArray();
            return new MeasureRowDto(label, byColumn, list.Sum(a => a.Amount) - byColumn.Sum());
        }

        var funds = lines.Where(l => l.Type.IsAppropriation()).GroupBy(l => (l.FundCode, l.FundName, l.Category))
            .OrderBy(g => g.Key.FundCode, StringComparer.Ordinal)
            .Select(fund =>
            {
                var departments = fund.Where(l => l.Type == AccountType.Expenditure)
                    .GroupBy(l => $"{l.DepartmentCode} {l.DepartmentName}".Trim())
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(d => Row(d.Key, d.Select(l => (l.AccountId, l.Amount))))
                    .ToList();
                MeasureRowDto subtotal = Row($"Total, {fund.Key.FundName}", fund.Where(l => l.Type == AccountType.Expenditure).Select(l => (l.AccountId, l.Amount)));
                return new MeasureFundDto(fund.Key.FundCode, fund.Key.FundName, fund.Key.Category, departments,
                    fund.Where(l => l.Type == AccountType.TransferOut).Sum(l => l.Amount), subtotal);
            })
            .ToList();

        MeasureRowDto departmentsTotal = Row("All departments", lines.Where(l => l.Type == AccountType.Expenditure).Select(l => (l.AccountId, l.Amount)));
        return new AppropriationMeasureDto(header, columns.Select(c => c.Label).ToList(), funds, departmentsTotal, funds.Sum(f => f.TransfersOut), usingDefault);
    }
}
