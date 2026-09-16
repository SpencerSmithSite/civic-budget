using CivicBudget.Domain.Governments;

namespace CivicBudget.Domain.Budgets;

public enum AppropriationLimitSeverity
{
    Ok = 0,
    Warning = 1,
    Error = 2,
}

public sealed record AppropriationLimitResult(Guid FundId, AppropriationLimitSeverity Severity, decimal AmountOverLimit)
{
    public bool BlocksWorkflow => Severity == AppropriationLimitSeverity.Error;
}

/// <summary>
/// Applies the government's <see cref="AppropriationLimitMode"/> to a fund balance summary.
/// The application layer runs this before Draft → Proposed and Proposed → Adopted transitions.
/// </summary>
public static class AppropriationLimitCheck
{
    public static AppropriationLimitResult Evaluate(FundBalanceSummary summary, AppropriationLimitMode mode)
    {
        if (summary.IsWithinAppropriationLimit)
        {
            return new AppropriationLimitResult(summary.FundId, AppropriationLimitSeverity.Ok, 0m);
        }

        AppropriationLimitSeverity severity = mode switch
        {
            AppropriationLimitMode.Warn => AppropriationLimitSeverity.Warning,
            AppropriationLimitMode.Block => AppropriationLimitSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown appropriation limit mode."),
        };

        return new AppropriationLimitResult(summary.FundId, severity, summary.AmountOverLimit);
    }

    public static IReadOnlyList<AppropriationLimitResult> EvaluateAll(IEnumerable<FundBalanceSummary> summaries, AppropriationLimitMode mode) =>
        summaries.Select(s => Evaluate(s, mode)).ToList();
}
