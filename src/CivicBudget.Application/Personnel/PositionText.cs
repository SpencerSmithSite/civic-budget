using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Personnel;

/// <summary>How a position's pay reads in a sync preview or a report, in one place so they say it the same way.</summary>
public static class PositionText
{
    /// <summary>"$88,500.00 a year", "$23.10 an hour × 1,560 hours", or "PO step 3 · $28.10 an hour".</summary>
    public static string Pay(PositionDetails d, PayrollRules rules)
    {
        if (d.PayScaleId is { } scaleId && rules.PayScale(scaleId) is { } scale)
        {
            decimal? rate = scale.Rate(d.Grade ?? "", d.Step ?? 0);
            string amount = rate is null ? "" : scale.Basis == PayBasis.Salary
                ? $" · {PositionCostCalculator.Dollars(rate.Value)} a year"
                : $" · {PositionCostCalculator.Dollars(rate.Value)} an hour";
            return $"{d.Grade} step {d.Step}{amount}";
        }

        return d.Basis == PayBasis.Salary
            ? $"{PositionCostCalculator.Dollars(d.Rate)} a year"
            : $"{PositionCostCalculator.Dollars(d.Rate)} an hour × {PositionCostCalculator.Number(d.AnnualHours)} hours";
    }
}
