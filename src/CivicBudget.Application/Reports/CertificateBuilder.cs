using System.Globalization;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Reports;

/// <summary>
/// Builds the certificate of estimated resources (ORC 5705.36) as a pure function, so every figure
/// and every check has a unit test. For each fund:
/// <code>
/// carryover       = cash at 12/31 − carried encumbrances − nonspendable − reserves ± unpaid advances
/// total available = carryover + estimated revenue (the report's revenue columns + other sources)
/// </code>
/// Cash and encumbrances come from the ERP's closed prior year. Before the ERP has closed that year
/// (an original certificate is prepared months before year end) the carryover is the budget's own
/// estimated beginning balance, and the report says so.
/// </summary>
public static class CertificateBuilder
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    public static CertificateReportDto Build(CertificateInputs input)
    {
        var notes = new List<string>();
        bool fromErp = input.Carryover is not null;
        DateOnly asOf = input.Carryover?.AsOf ?? input.FiscalYearStart.AddDays(-1);
        if (!fromErp)
        {
            notes.Add($"The ERP has not closed the year ending {Date(asOf)}, so each fund's balance is the budget's estimated beginning balance. Sync that year's actuals once it closes and the certificate uses its cash and encumbrances.");
            if (input.Adjustments.Values.Any(a => a.Nonspendable != 0m || a.Reserves != 0m || a.UnpaidAdvances != 0m))
            {
                notes.Add("Reserves, nonspendable balances, and unpaid advances apply once the carryover comes from the ERP; the budget's estimate already allows for them.");
            }
        }

        if (input.DefaultColumns)
        {
            notes.Add($"{string.Join(" and ", input.Columns.Select(c => c.Label))} are the revenue accounts categorized as taxes. Choose the accounts in Report settings if the county counts them differently.");
        }

        ILookup<Guid, CertificateResourceLine> resourcesByFund = input.Resources.ToLookup(r => r.FundId);
        var rows = input.Funds.Select(f => Row(f, resourcesByFund[f.Id].ToList(), input, fromErp)).ToList();

        List<CertificateSectionDto> sections = rows
            .Zip(input.Funds)
            .GroupBy(x => x.Second.Category)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var funds = g.Select(x => x.First).OrderBy(r => r.FundCode, StringComparer.Ordinal).ToList();
                string label = CategoryLabel(g.Key);
                return new CertificateSectionDto(label, funds, Sum($"Total {label}", funds, input.Columns.Count));
            })
            .ToList();
        CertificateRowDto total = Sum("All funds", rows, input.Columns.Count);

        return new CertificateReportDto(
            input.Header, input.BalanceLabel, input.Columns.Select(c => c.Label).ToList(), input.OtherSourcesLabel,
            fromErp, asOf, sections, total,
            Checks(input, rows, total, fromErp),
            input.PriorLabel,
            Changes(input),
            notes);
    }

    private static CertificateRowDto Row(CertificateFund fund, List<CertificateResourceLine> resources, CertificateInputs input, bool fromErp)
    {
        decimal[] columns = input.Columns.Select(c => resources.Where(r => c.AccountIds.Contains(r.AccountId)).Sum(r => r.Amount)).ToArray();
        decimal other = resources.Sum(r => r.Amount) - columns.Sum();
        CertificateAdjustment adjustment = input.Adjustments.GetValueOrDefault(fund.Id) ?? new CertificateAdjustment(0m, 0m, 0m);

        if (!fromErp)
        {
            return new CertificateRowDto(fund.Code, fund.Name, fund.Category, null, null, 0m, 0m, 0m,
                fund.BudgetBeginningBalance, columns, other, fund.Appropriations, fund.BudgetBeginningBalance);
        }

        decimal cash = input.Carryover!.Cash.GetValueOrDefault(fund.Id);
        decimal encumbrances = input.Carryover.Encumbrances.GetValueOrDefault(fund.Id);
        decimal carryover = cash - encumbrances - adjustment.Nonspendable - adjustment.Reserves + adjustment.UnpaidAdvances;
        return new CertificateRowDto(fund.Code, fund.Name, fund.Category, cash, encumbrances, adjustment.Nonspendable, adjustment.Reserves, adjustment.UnpaidAdvances,
            carryover, columns, other, fund.Appropriations, fund.BudgetBeginningBalance);
    }

    private static CertificateRowDto Sum(string label, IReadOnlyList<CertificateRowDto> rows, int columnCount) => new(
        "", label, null,
        rows.All(r => r.Cash is null) ? null : rows.Sum(r => r.Cash ?? 0m),
        rows.All(r => r.Encumbrances is null) ? null : rows.Sum(r => r.Encumbrances ?? 0m),
        rows.Sum(r => r.Nonspendable), rows.Sum(r => r.Reserves), rows.Sum(r => r.UnpaidAdvances), rows.Sum(r => r.Carryover),
        Enumerable.Range(0, columnCount).Select(i => rows.Sum(r => r.RevenueColumns[i])).ToList(),
        rows.Sum(r => r.OtherSources), rows.Sum(r => r.Appropriations), rows.Sum(r => r.BudgetBeginningBalance));

    /// <summary>
    /// The reconciliations a budget commission would run. Two can fail on real data (a fund over its
    /// total, a budget that starts a fund from a different balance than the one certified), one fails
    /// when the settings are incomplete, and one proves the columns account for every revenue dollar.
    /// </summary>
    private static List<CertificateCheckDto> Checks(CertificateInputs input, List<CertificateRowDto> rows, CertificateRowDto total, bool fromErp)
    {
        var checks = new List<CertificateCheckDto>();

        List<CertificateRowDto> over = rows.Where(r => !r.IsWithinLimit).ToList();
        checks.Add(new CertificateCheckDto(
            "Appropriations stay within each fund's total available (ORC 5705.39)",
            over.Count == 0,
            over.Count == 0
                ? $"Every fund: {Money(total.Appropriations)} appropriated against {Money(total.TotalAvailable)} available."
                : string.Join(" ", over.Select(r => $"{r.FundCode} {r.FundName} appropriates {Money(r.Appropriations - r.TotalAvailable)} more than its {Money(r.TotalAvailable)}."))));

        if (fromErp)
        {
            List<CertificateRowDto> differs = rows.Where(r => !r.BalanceMatchesBudget).ToList();
            checks.Add(new CertificateCheckDto(
                "The budget starts each fund from the certified balance",
                differs.Count == 0,
                differs.Count == 0
                    ? "Every fund's beginning balance in the budget equals its carryover."
                    : string.Join(" ", differs.Select(r => $"{r.FundCode} {r.FundName}: the budget starts with {Money(r.BudgetBeginningBalance)}, the certificate certifies {Money(r.Carryover)}.")) +
                      (differs.Count == 0 ? "" : " Update the beginning balances so the appropriation check uses the certified figures.")));
        }

        List<CertificateColumn> empty = input.Columns.Where(c => c.AccountIds.Count == 0).ToList();
        checks.Add(new CertificateCheckDto(
            "Every revenue column has accounts",
            empty.Count == 0 && input.Columns.Count > 0,
            input.Columns.Count == 0
                ? $"No revenue columns are set, so every receipt is under {input.OtherSourcesLabel}. Add a column in Report settings."
                : empty.Count == 0
                    ? string.Join("; ", input.Columns.Select(c => $"{c.Label}: {c.AccountIds.Count} account{(c.AccountIds.Count == 1 ? "" : "s")}")) + "."
                    : $"{string.Join(" and ", empty.Select(c => c.Label))} {(empty.Count == 1 ? "has" : "have")} no accounts. Choose them in Report settings."));

        HashSet<Guid> fundIds = input.Funds.Select(f => f.Id).ToHashSet();
        decimal resources = input.Resources.Where(r => fundIds.Contains(r.FundId)).Sum(r => r.Amount);
        checks.Add(new CertificateCheckDto(
            $"{string.Join(" + ", input.Columns.Select(c => c.Label).Append(input.OtherSourcesLabel))} = estimated revenue",
            total.EstimatedRevenue == resources,
            $"{Money(total.EstimatedRevenue)} on the certificate; {Money(resources)} of revenue and transfers in the budget."));

        return checks;
    }

    /// <summary>For an amended certificate: every revenue estimate that moved, with the justification typed on its line.</summary>
    private static List<RevenueChangeDto> Changes(CertificateInputs input)
    {
        if (input.PriorLabel is null)
        {
            return [];
        }

        Dictionary<Guid, string> fundCodes = input.Funds.ToDictionary(f => f.Id, f => f.Code);
        var prior = input.PriorResources.ToDictionary(r => new LineKey(r.FundId, r.DepartmentId, r.AccountId));
        var now = input.Resources.ToDictionary(r => new LineKey(r.FundId, r.DepartmentId, r.AccountId));
        return prior.Keys.Union(now.Keys)
            .Select(k => (Prior: prior.GetValueOrDefault(k), Now: now.GetValueOrDefault(k)))
            .Where(p => (p.Prior?.Amount ?? 0m) != (p.Now?.Amount ?? 0m))
            .Select(p =>
            {
                CertificateResourceLine line = (p.Now ?? p.Prior)!;
                return new RevenueChangeDto(fundCodes.GetValueOrDefault(line.FundId, ""), line.AccountNumber, line.AccountName, p.Prior?.Amount ?? 0m, p.Now?.Amount ?? 0m, p.Now?.Justification);
            })
            .OrderBy(c => c.AccountNumber, StringComparer.Ordinal)
            .ToList();
    }

    public static string CategoryLabel(FundCategory category) => category switch
    {
        FundCategory.SpecialRevenue => "Special Revenue",
        FundCategory.DebtService => "Debt Service",
        FundCategory.CapitalProjects => "Capital Projects",
        FundCategory.InternalService => "Internal Service",
        _ => category.ToString(),
    };

    private static string Money(decimal amount) => amount.ToString("C2", UsCulture);

    private static string Date(DateOnly date) => date.ToString("MMMM d, yyyy", UsCulture);
}
