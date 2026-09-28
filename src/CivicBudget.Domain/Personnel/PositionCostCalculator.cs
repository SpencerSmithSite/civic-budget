using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Personnel;

/// <summary>What a piece of a position's cost is, for grouping and for the breakdown a screen shows.</summary>
public enum CostKind
{
    BasePay = 1,
    Longevity = 2,
    ExtraPay = 3,
    Retirement = 4,
    Medicare = 5,
    WorkersComp = 6,
    Insurance = 7,
}

/// <summary>One piece of a position's yearly cost and the account it lands on.</summary>
public sealed record CostPart(CostKind Kind, string Label, Guid AccountId, decimal Amount, string Basis);

/// <summary>What one fund pays on one account for a position (or, summed, for a department).</summary>
public sealed record FundAccountCost(Guid FundId, Guid AccountId, decimal Amount);

/// <summary>A priced position: its rates, its years of service, every piece of its cost, and each fund's part.</summary>
public sealed record PositionCost(
    PayBasis Basis,
    decimal StartingRate,
    decimal EndingRate,
    decimal HourlyRate,
    int? YearsOfService,
    IReadOnlyList<CostPart> Parts,
    IReadOnlyList<FundAccountCost> ByFund)
{
    public decimal Total => Parts.Sum(p => p.Amount);
    public decimal Pay => Parts.Where(p => p.Kind is CostKind.BasePay or CostKind.Longevity or CostKind.ExtraPay).Sum(p => p.Amount);
    public decimal Benefits => Total - Pay;
}

/// <summary>
/// Prices a position for one fiscal year. Pure: the same details and rules always give the same cost,
/// so the screen that previews a position and the save that updates the budget cannot disagree.
/// <list type="number">
/// <item>Base pay: each month paid earns a twelfth of the year's pay at that month's rate, so a raise
/// or step increase in month 7 counts for six months and a vacancy filled in month 4 for nine.</item>
/// <item>Longevity, from completed years of service and the position's schedule.</item>
/// <item>Extra pay: amounts, hours at a multiple of the average hourly rate, or a percentage of base pay.</item>
/// <item>Retirement on pensionable pay (plus the employee's share when the employer picks it up);
/// Medicare and workers' compensation on taxable pay.</item>
/// <item>Insurance: each plan's monthly premium for the tier, less the employee's share, for each month paid.</item>
/// </list>
/// Each piece is rounded to cents, then divided among the funds so that the funds' parts add up to
/// the piece exactly.
/// </summary>
public static class PositionCostCalculator
{
    public static PositionCost Calculate(PositionDetails position, PayrollRules rules)
    {
        IReadOnlyList<(string Field, string Message)> problems = position.Problems(rules);
        Guard.Against(problems.Count > 0, problems.Count > 0 ? problems[0].Message : "");

        (PayBasis basis, decimal startRate, decimal endRate, int changeMonth) = Rates(position, rules);
        decimal hours = position.AnnualHours;
        Guid payAccount = position.PayAccountId ?? rules.PayAccountId;
        var parts = new List<CostPart>();

        // Base pay, month by month: months before the change at the starting rate, the rest at the new one.
        int monthsAtStart = Math.Clamp(changeMonth - position.FirstMonth, 0, position.MonthsPaid);
        int monthsAtEnd = position.MonthsPaid - monthsAtStart;
        decimal basePay = Money.Round((Annual(basis, startRate, hours) * monthsAtStart + Annual(basis, endRate, hours) * monthsAtEnd) / 12m);
        parts.Add(new CostPart(CostKind.BasePay, "Base pay", payAccount, basePay, BasePayBasis(position, basis, startRate, endRate, monthsAtStart, monthsAtEnd)));

        // The hourly rate extra hours are paid at: the average over the months paid.
        decimal hoursPaid = hours * position.MonthsPaid / 12m;
        decimal averageHourly = hoursPaid == 0m ? 0m : basePay / hoursPaid;

        decimal pensionable = basePay;
        decimal taxable = basePay;

        int? years = null;
        if (position.LongevityScheduleId is { } scheduleId && position.HireDate is { } hired)
        {
            LongevityRule schedule = rules.LongevitySchedule(scheduleId)!;
            DateOnly countedOn = schedule.CountedOn == ServiceCountedOn.FirstDayOfYear ? rules.YearStart : rules.YearEnd;
            years = CompletedYears(hired, countedOn);
            // A percentage of base pay already reflects the months paid; a yearly amount is prorated to them.
            decimal longevity = schedule.Method == LongevityMethod.PercentOfPay
                ? Money.Round(Longevity.Amount(schedule, years.Value, basePay))
                : Money.Round(Longevity.Amount(schedule, years.Value, basePay) * position.MonthsPaid / 12m);

            if (longevity != 0m)
            {
                parts.Add(new CostPart(CostKind.Longevity, $"Longevity ({schedule.Name})", schedule.AccountId ?? payAccount, longevity,
                    $"{years} year{(years == 1 ? "" : "s")} of service"));
                pensionable += longevity;
                taxable += longevity;
            }
        }

        foreach (ExtraPayAmount extra in position.ExtraPay)
        {
            ExtraPayRule item = rules.ExtraPayItem(extra.ExtraPayId)!;
            (decimal amount, string itemBasis) = item.Kind switch
            {
                ExtraPayKind.Hours => (Money.Round(extra.Value * averageHourly * item.Multiplier),
                    $"{Number(extra.Value)} hours at {Dollars(averageHourly)} × {Number(item.Multiplier)}"),
                ExtraPayKind.PercentOfBase => (Money.Round(basePay * extra.Value / 100m), $"{Number(extra.Value)}% of base pay"),
                _ => (Money.Round(extra.Value), "Amount for the year"),
            };
            if (amount == 0m)
            {
                continue;
            }

            parts.Add(new CostPart(CostKind.ExtraPay, item.Name, item.AccountId ?? payAccount, amount, itemBasis));
            pensionable += item.IsPensionable ? amount : 0m;
            taxable += item.IsTaxable ? amount : 0m;
        }

        if (position.RetirementPlanId is { } planId)
        {
            RetirementPlanRule plan = rules.RetirementPlan(planId)!;
            parts.Add(new CostPart(CostKind.Retirement, $"{plan.Name}, employer share", plan.AccountId,
                Money.Round(pensionable * plan.EmployerRate / 100m), $"{Number(plan.EmployerRate)}% of {Dollars(pensionable)}"));
            if (position.PicksUpEmployeeShare)
            {
                parts.Add(new CostPart(CostKind.Retirement, $"{plan.Name}, employee share picked up", plan.AccountId,
                    Money.Round(pensionable * plan.EmployeeRate / 100m), $"{Number(plan.EmployeeRate)}% of {Dollars(pensionable)}"));
            }
        }

        parts.Add(new CostPart(CostKind.Medicare, "Medicare", rules.MedicareAccountId,
            Money.Round(taxable * rules.MedicareRate / 100m), $"{Number(rules.MedicareRate)}% of {Dollars(taxable)}"));
        if (rules.WorkersCompRate != 0m)
        {
            parts.Add(new CostPart(CostKind.WorkersComp, "Workers' compensation", rules.WorkersCompAccountId,
                Money.Round(taxable * rules.WorkersCompRate / 100m), $"{Number(rules.WorkersCompRate)}% of {Dollars(taxable)}"));
        }

        foreach (Coverage coverage in position.Coverages)
        {
            InsurancePlanRule plan = rules.InsurancePlan(coverage.PlanId)!;
            decimal premium = plan.Premium(coverage.Tier)!.Value;
            decimal employerMonthly = premium * (100m - plan.EmployeeSharePercent) / 100m;
            parts.Add(new CostPart(CostKind.Insurance, $"{plan.Name}, {TierName(coverage.Tier)}", plan.AccountId,
                Money.Round(employerMonthly * position.MonthsPaid),
                $"{Dollars(premium)} a month less {Number(plan.EmployeeSharePercent)}% employee share, {position.MonthsPaid} months"));
        }

        return new PositionCost(basis, startRate, endRate, averageHourly, years, parts, SplitAmongFunds(parts, position.Funds));
    }

    /// <summary>
    /// A department's positions summed by fund and account, with how many positions cost into each:
    /// what the department's budget lines should say.
    /// </summary>
    public static IReadOnlyList<PersonnelLineCost> SumByLine(IEnumerable<PositionDetails> positions, PayrollRules rules)
    {
        var totals = new Dictionary<(Guid Fund, Guid Account), (decimal Amount, int Count)>();
        foreach (PositionDetails position in positions)
        {
            foreach (FundAccountCost cost in Calculate(position, rules).ByFund.Where(c => c.Amount != 0m))
            {
                (decimal amount, int count) = totals.GetValueOrDefault((cost.FundId, cost.AccountId));
                totals[(cost.FundId, cost.AccountId)] = (amount + cost.Amount, count + 1);
            }
        }

        return totals.Select(t => new PersonnelLineCost(t.Key.Fund, t.Key.Account, t.Value.Amount, t.Value.Count)).ToList();
    }

    public static string TierName(CoverageTier tier) => tier switch
    {
        CoverageTier.EmployeeOnly => "single",
        CoverageTier.EmployeeSpouse => "employee and spouse",
        CoverageTier.Family => "family",
        _ => tier.ToString(),
    };

    /// <summary>Whole years from the hire date to the given day; zero before the first anniversary.</summary>
    public static int CompletedYears(DateOnly hired, DateOnly on)
    {
        int years = on.Year - hired.Year;
        if (on < hired.AddYears(years))
        {
            years--;
        }

        return Math.Max(years, 0);
    }

    /// <summary>The starting rate, the rate after a raise or step increase, and the month the change takes effect (13 for none).</summary>
    private static (PayBasis Basis, decimal Start, decimal End, int ChangeMonth) Rates(PositionDetails position, PayrollRules rules)
    {
        if (position.PayScaleId is { } scaleId)
        {
            PayScaleRule scale = rules.PayScale(scaleId)!;
            decimal start = scale.Rate(position.Grade!, position.Step!.Value)!.Value;
            decimal? next = position.StepIncreaseMonth is null ? null : scale.Rate(position.Grade!, position.Step.Value + 1);
            return next is { } stepped
                ? (scale.Basis, start, stepped, position.StepIncreaseMonth!.Value)
                : (scale.Basis, start, start, 13);
        }

        if (position.RaisePercent == 0m)
        {
            return (position.Basis, position.Rate, position.Rate, 13);
        }

        decimal raised = Money.Round(position.Rate * (100m + position.RaisePercent) / 100m);
        return (position.Basis, position.Rate, raised, position.RaiseMonth);
    }

    /// <summary>Dollars the way the breakdown prints them, whatever the server's culture: $1,234.50.</summary>
    public static string Dollars(decimal amount) => amount.ToString("$#,0.00;-$#,0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A rate, percentage, or count without trailing zeros: 1.45, 2,080, 1.5.</summary>
    public static string Number(decimal value) => value.ToString("#,0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static decimal Annual(PayBasis basis, decimal rate, decimal hours) => basis == PayBasis.Salary ? rate : rate * hours;

    private static string BasePayBasis(PositionDetails position, PayBasis basis, decimal start, decimal end, int monthsAtStart, int monthsAtEnd)
    {
        string Rate(decimal rate) => basis == PayBasis.Salary ? $"{Dollars(rate)} a year" : $"{Dollars(rate)} an hour × {Number(position.AnnualHours)} hours";
        if (start == end || monthsAtEnd == 0)
        {
            return position.MonthsPaid == 12 ? Rate(start) : $"{Rate(start)}, {position.MonthsPaid} months";
        }

        return monthsAtStart == 0
            ? $"{Rate(end)}, {monthsAtEnd} months"
            : $"{Rate(start)} for {monthsAtStart} months, then {Rate(end)} for {monthsAtEnd}";
    }

    /// <summary>
    /// Divides each piece among the funds by their percentages, rounding each fund's part to cents.
    /// The fund with the largest share (the lowest id, on a tie) takes whatever cent is left, so no
    /// cent is lost or invented, and the same position always splits the same way however its funds
    /// happen to be listed; the database returns them in no particular order.
    /// </summary>
    private static List<FundAccountCost> SplitAmongFunds(IEnumerable<CostPart> parts, IReadOnlyList<FundShare> funds)
    {
        List<FundShare> ordered = funds.OrderByDescending(f => f.Percent).ThenBy(f => f.FundId).ToList();
        var totals = new Dictionary<(Guid Fund, Guid Account), decimal>();
        foreach (CostPart part in parts)
        {
            decimal others = 0m;
            for (int i = 1; i < ordered.Count; i++)
            {
                decimal share = Money.Round(part.Amount * ordered[i].Percent / 100m);
                others += share;
                Add(ordered[i].FundId, part.AccountId, share);
            }

            Add(ordered[0].FundId, part.AccountId, part.Amount - others);
        }

        return totals.Select(t => new FundAccountCost(t.Key.Fund, t.Key.Account, t.Value)).ToList();

        void Add(Guid fund, Guid account, decimal amount) => totals[(fund, account)] = totals.GetValueOrDefault((fund, account)) + amount;
    }
}

/// <summary>What a department's positions cost on one fund and account, and how many positions that is.</summary>
public sealed record PersonnelLineCost(Guid FundId, Guid AccountId, decimal Amount, int PositionCount);
