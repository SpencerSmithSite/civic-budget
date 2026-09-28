namespace CivicBudget.Domain.Personnel;

/// <summary>
/// Longevity pay: what a schedule pays for a number of years of service, and the schedule in plain
/// English. The settings page shows the sentences as the administrator builds the schedule, so a
/// contract clause can be checked against what the budget will do without reading any numbers twice.
/// </summary>
public static class Longevity
{
    /// <summary>The yearly longevity for someone with <paramref name="years"/> of service; zero before the first step.</summary>
    public static decimal Amount(LongevityRule schedule, int years, decimal basePay)
    {
        LongevityStepRule? step = schedule.Steps.Where(s => s.MinYears <= years).MaxBy(s => s.MinYears);
        if (step is null)
        {
            return 0m;
        }

        return schedule.Method switch
        {
            LongevityMethod.FlatAmount => step.Value,
            LongevityMethod.PercentOfPay => basePay * step.Value / 100m,
            LongevityMethod.AmountPerYear => step.Value * Math.Min(years, schedule.MaxYears ?? years),
            _ => 0m,
        };
    }

    /// <summary>One sentence per step, in order, starting with who gets nothing.</summary>
    public static IReadOnlyList<string> Describe(LongevityRule schedule)
    {
        List<LongevityStepRule> steps = schedule.Steps.OrderBy(s => s.MinYears).ToList();
        if (steps.Count == 0)
        {
            return ["No steps yet, so nobody earns longevity."];
        }

        var lines = new List<string>();
        if (steps[0].MinYears > 0)
        {
            lines.Add($"Fewer than {Years(steps[0].MinYears)} of service: no longevity.");
        }

        for (int i = 0; i < steps.Count; i++)
        {
            int from = steps[i].MinYears;
            string range = i + 1 < steps.Count
                ? (steps[i + 1].MinYears - 1 == from ? $"{Years(from)}" : $"{from} to {Years(steps[i + 1].MinYears - 1)}")
                : $"{from} or more years";
            lines.Add($"{Capitalize(range)} of service: {Pays(schedule, steps[i].Value)}.");
        }

        if (schedule.Method == LongevityMethod.AmountPerYear && schedule.MaxYears is { } max)
        {
            lines.Add($"Years after {max} do not add more.");
        }

        lines.Add(schedule.CountedOn == ServiceCountedOn.FirstDayOfYear
            ? "Years of service are counted on the first day of the budget year."
            : "Years of service are counted on the last day of the budget year.");
        return lines;
    }

    private static string Pays(LongevityRule schedule, decimal value) => schedule.Method switch
    {
        LongevityMethod.FlatAmount => $"{PositionCostCalculator.Dollars(value)} a year",
        LongevityMethod.PercentOfPay => $"{PositionCostCalculator.Number(value)}% of base pay",
        LongevityMethod.AmountPerYear => $"{PositionCostCalculator.Dollars(value)} for each year of service",
        _ => "",
    };

    private static string Years(int years) => years == 1 ? "1 year" : $"{years} years";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
