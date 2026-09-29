using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Budgets.Planning;

/// <summary>One future year's percentages. YearOffset counts from the budget year (1 is the year after it).</summary>
public sealed record PlanRate(int YearOffset, decimal RevenuePercent, decimal ExpenditurePercent);

/// <summary>A line as the plan needs it: its budget-year amount and the future years typed over the calculation.</summary>
public sealed record PlanLineInput(Guid LineId, Guid FundId, AccountType AccountType, decimal Amount, IReadOnlyDictionary<int, decimal> Typed);

/// <summary>A line's amount in every year of the plan (index 0 is the budget year) and which ones were typed.</summary>
public sealed record PlanLineProjection(Guid LineId, IReadOnlyList<decimal> Amounts, IReadOnlyList<bool> Typed);

/// <summary>One fund in one year of the plan. Its beginning balance is the year before's projected ending balance.</summary>
public sealed record PlanFundYear(Guid FundId, int YearOffset, FundBalanceSummary Summary);

public sealed record MultiYearProjection(int Years, IReadOnlyList<PlanLineProjection> Lines, IReadOnlyList<PlanFundYear> Funds);

/// <summary>
/// The multi-year plan, worked out: each line year by year from its budget-year amount, and each
/// fund's balance rolled forward. Pure, so every rule is a unit test.
/// <list type="bullet">
/// <item>A future year is the year before it changed by that year's percentage: the revenue one for
/// revenues and transfers in, the expenditure one for expenditures and transfers out. It is the same
/// calculation as starting a budget from last year's (<see cref="BudgetSeedOptions"/>), done once a
/// year.</item>
/// <item>A typed amount replaces the calculation for that year, and the years after it follow from it.</item>
/// <item>Each fund's ending balance becomes its next year's beginning balance, so a year in which a
/// fund would spend more than it has shows up as over its limit, the multi-year form of the Ohio rule.</item>
/// </list>
/// </summary>
public static class MultiYearPlanCalculator
{
    public static MultiYearProjection Project(
        int years,
        bool wholeDollars,
        IEnumerable<PlanRate> rates,
        IReadOnlyList<PlanLineInput> lines,
        IReadOnlyDictionary<Guid, decimal> beginningBalances)
    {
        Guard.Against(years < 1, "A plan has at least one year.");
        Dictionary<int, PlanRate> byYear = rates.ToDictionary(r => r.YearOffset);

        var projectedLines = new List<PlanLineProjection>(lines.Count);
        foreach (PlanLineInput line in lines)
        {
            var amounts = new decimal[years];
            var typed = new bool[years];
            amounts[0] = line.Amount;
            for (int year = 1; year < years; year++)
            {
                if (line.Typed.TryGetValue(year, out decimal typedAmount))
                {
                    amounts[year] = typedAmount;
                    typed[year] = true;
                    continue;
                }

                decimal percent = byYear.TryGetValue(year, out PlanRate? rate)
                    ? (line.AccountType.IsResource() ? rate.RevenuePercent : rate.ExpenditurePercent)
                    : 0m;
                decimal next = amounts[year - 1] * (1m + percent / 100m);
                amounts[year] = wholeDollars ? Math.Round(next, 0, MidpointRounding.AwayFromZero) : Money.Round(next);
            }

            projectedLines.Add(new PlanLineProjection(line.LineId, amounts, typed));
        }

        Dictionary<Guid, PlanLineInput> inputs = lines.ToDictionary(l => l.LineId);
        IEnumerable<Guid> fundIds = lines.Select(l => l.FundId).Concat(beginningBalances.Keys).Distinct();
        var funds = new List<PlanFundYear>();
        foreach (Guid fundId in fundIds)
        {
            decimal beginning = beginningBalances.GetValueOrDefault(fundId);
            for (int year = 0; year < years; year++)
            {
                int y = year;
                IEnumerable<LineAmount> amounts = projectedLines
                    .Where(p => inputs[p.LineId].FundId == fundId)
                    .Select(p => new LineAmount(fundId, inputs[p.LineId].AccountType, p.Amounts[y]));
                FundBalanceSummary summary = FundBalanceCalculator.Calculate(fundId, beginning, amounts);
                funds.Add(new PlanFundYear(fundId, year, summary));
                beginning = summary.ProjectedEndingBalance;
            }
        }

        return new MultiYearProjection(years, projectedLines, funds);
    }

    /// <summary>The version's own plan. Requires its lines' accounts to be loaded, as fund balances do.</summary>
    public static MultiYearProjection Project(BudgetVersion version) =>
        Project(
            version.PlanYears,
            version.PlanInWholeDollars,
            version.PlanAssumptions.Select(a => new PlanRate(a.YearOffset, a.RevenuePercent, a.ExpenditurePercent)),
            [.. version.Lines.Select(l => new PlanLineInput(l.Id, l.FundId, l.Account.Type, l.Amount,
                l.PlannedAmounts.ToDictionary(p => p.YearOffset, p => p.Amount)))],
            version.BeginningBalances.ToDictionary(b => b.FundId, b => b.Amount));
}
