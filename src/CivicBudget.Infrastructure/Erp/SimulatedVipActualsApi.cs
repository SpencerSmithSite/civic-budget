using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Infrastructure.Seed;

namespace CivicBudget.Infrastructure.Erp;

/// <summary>
/// Stands in for VIP's API in development and the live demo, so the "Fetch from VIP" button has
/// somewhere real to go. Its books are the demo governments' own fictional history (the same seed
/// lines the budgets were built from), spread across the months the way municipal money actually
/// moves: real estate taxes in two settlements, payroll every other Friday, capital work in summer,
/// debt service twice a year. It is a stand-in and says so in its name; a real connection
/// implements <see cref="IErpActualsApi"/> the same way and nothing else changes.
/// </summary>
public sealed class SimulatedVipActualsApi(TimeProvider clock) : IErpActualsApi
{
    /// <summary>A month's books close about ten days after it ends, once bank reconciliation is done.</summary>
    public const int DaysToCloseAMonth = 10;

    public string Name => "VIP (simulated)";

    public Task<Result<ErpActuals>> FetchAsync(ErpEntity entity, int fiscalYear, CancellationToken ct = default)
    {
        (IReadOnlyList<SeedLine> Lines, IReadOnlyDictionary<(string FundCode, int Year), decimal> Balances)? books = entity.Slug switch
        {
            MapleRidgeSeed.Slug => (MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances),
            PineHollowSeed.Slug => (PineHollowSeed.Lines, PineHollowSeed.BeginningBalances),
            _ => null,
        };
        if (books is null)
        {
            return Task.FromResult(Result.Failure<ErpActuals>($"VIP has no entity set up for {entity.Name}."));
        }

        if (books.Value.Lines.All(l => l.ActualFor(fiscalYear) is null))
        {
            return Task.FromResult(Result.Failure<ErpActuals>($"VIP has no books for FY{fiscalYear}."));
        }

        FiscalPeriod year = FiscalPeriod.For(fiscalYear, entity.FiscalYearStartMonth);
        int through = ClosedMonths(year, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        if (through == 0)
        {
            return Task.FromResult(Result.Failure<ErpActuals>($"VIP has not closed a month of FY{fiscalYear} yet."));
        }

        return Task.FromResult(Result.Success(Build(books.Value.Lines, books.Value.Balances, fiscalYear, entity.FiscalYearStartMonth, through)));
    }

    /// <summary>How many fiscal months had closed by <paramref name="today"/>.</summary>
    public static int ClosedMonths(FiscalPeriod year, DateOnly today)
    {
        int closed = 0;
        for (int period = 1; period <= 12; period++)
        {
            DateOnly monthEnd = year.Start.AddMonths(period).AddDays(-1);
            if (monthEnd.AddDays(DaysToCloseAMonth) <= today)
            {
                closed = period;
            }
        }

        return closed;
    }

    private static ErpActuals Build(IReadOnlyList<SeedLine> lines, IReadOnlyDictionary<(string FundCode, int Year), decimal> balances, int fiscalYear, int startMonth, int through)
    {
        var activity = new List<ErpActivity>();
        var encumbrances = new List<ErpOpenEncumbrance>();
        var receipts = new Dictionary<string, decimal>();
        var disbursements = new Dictionary<string, decimal>();
        foreach (SeedLine line in lines)
        {
            if (line.ActualFor(fiscalYear) is not { } annual)
            {
                continue;
            }

            decimal[] months = Spread(line, annual, fiscalYear, startMonth);
            decimal toDate = 0m;
            for (int period = 1; period <= through; period++)
            {
                activity.Add(new ErpActivity(line.FundCode, line.DepartmentCode, line.AccountCode, period, months[period - 1]));
                toDate += months[period - 1];
            }

            Dictionary<string, decimal> side = IsReceipt(line.AccountCode) ? receipts : disbursements;
            side[line.FundCode] = side.GetValueOrDefault(line.FundCode) + toDate;

            decimal open = OpenEncumbrance(line, annual, toDate, fiscalYear, closed: through == 12);
            if (open > 0m)
            {
                encumbrances.Add(new ErpOpenEncumbrance(line.FundCode, line.DepartmentCode, line.AccountCode, open));
            }
        }

        // Cash agrees with the budgets' beginning balances: at year end, cash less the encumbrances
        // carried forward is next year's unencumbered balance, which is how Ohio defines it.
        var cash = new List<ErpCash>();
        foreach (string fund in lines.Select(l => l.FundCode).Distinct())
        {
            decimal encumbered = encumbrances.Where(e => e.FundCode == fund).Sum(e => e.Amount);
            if (through == 12 && balances.TryGetValue((fund, fiscalYear + 1), out decimal nextYear))
            {
                cash.Add(new ErpCash(fund, nextYear + encumbered));
            }
            else if (through < 12 && balances.TryGetValue((fund, fiscalYear), out decimal beginning))
            {
                decimal carried = lines.Where(l => l.FundCode == fund && l.ActualFor(fiscalYear - 1) is not null)
                    .Sum(l => OpenEncumbrance(l, l.ActualFor(fiscalYear - 1)!.Value, l.ActualFor(fiscalYear - 1)!.Value, fiscalYear - 1, closed: true));
                cash.Add(new ErpCash(fund, beginning + carried + receipts.GetValueOrDefault(fund) - disbursements.GetValueOrDefault(fund)));
            }
        }

        return new ErpActuals(fiscalYear, through, activity, encumbrances, cash);
    }

    /// <summary>The year's actual split into twelve fiscal months, in cents, adding back to the year exactly.</summary>
    private static decimal[] Spread(SeedLine line, decimal annual, int fiscalYear, int startMonth)
    {
        double[] weights = new double[12];
        for (int period = 1; period <= 12; period++)
        {
            int calendarMonth = ((startMonth - 1 + period - 1) % 12) + 1;
            weights[period - 1] = Weight(line, calendarMonth, fiscalYear);
        }

        double total = weights.Sum();
        decimal[] months = new decimal[12];
        decimal assigned = 0m;
        for (int i = 0; i < 11; i++)
        {
            months[i] = Math.Round(annual * (decimal)(weights[i] / total), 2, MidpointRounding.AwayFromZero);
            assigned += months[i];
        }

        months[11] = annual - assigned;
        return months;
    }

    /// <summary>How a calendar month's share of an account compares to an average month.</summary>
    private static double Weight(SeedLine line, int month, int fiscalYear)
    {
        // A little month-to-month noise so no two lines look machine-made, stable across runs.
        double jitter = 1 + ((Math.Abs(line.Hash(fiscalYear * 100 + month)) % 9) - 4) * 0.02;
        return line.AccountCode switch
        {
            "4110" => month switch { 3 => 45, 4 => 5, 8 => 45, 9 => 5, _ => 0 },   // real estate tax settlements
            "4910" or "5910" => month == 3 ? 1 : 0,                                  // the annual transfer, once appropriated
            "5610" or "5620" => month is 6 or 12 ? 1 : 0,                            // debt service twice a year
            "5510" or "5520" => month switch { 5 => 1, 6 => 2, 7 => 3, 8 => 2.5, 9 => 1.5, _ => 0 } * jitter,
            "5320" => month switch { 1 => 1.4, 2 => 1.3, 3 => 1.1, 7 or 8 => 1.1, 12 => 1.2, 4 or 9 or 11 => 0.9, _ => 0.8 } * jitter,
            // Biweekly payroll: 26 paydays, so two months a year have three.
            "5110" or "5120" or "5210" or "5220" or "5230" or "5240" => month is 1 or 7 ? 3 : 2,
            "4320" => (month is >= 6 and <= 9 ? 1.2 : 1.0) * jitter,               // water and sewer use peaks in summer
            _ => jitter,
        };
    }

    /// <summary>
    /// Purchase orders not yet paid. During the year, blanket certificates keep part of what is left
    /// committed; at year end a smaller share carries into the next year.
    /// </summary>
    private static decimal OpenEncumbrance(SeedLine line, decimal annual, decimal toDate, int fiscalYear, bool closed)
    {
        decimal share = line.AccountCode switch
        {
            "5310" => 0.35m,
            "5320" or "5420" => 0.25m,
            "5410" => 0.20m,
            "5510" or "5520" => 0.60m,
            _ => 0m,
        };
        if (share == 0m)
        {
            return 0m;
        }

        decimal committed = closed
            ? annual * share * (Math.Abs(line.Hash(fiscalYear)) % 4) * 0.05m   // 0% to 15% of the share carried over
            : Math.Max(0m, annual - toDate) * share;
        return Math.Round(committed, 0, MidpointRounding.AwayFromZero);
    }

    private static bool IsReceipt(string accountCode) => accountCode.StartsWith('4');
}
