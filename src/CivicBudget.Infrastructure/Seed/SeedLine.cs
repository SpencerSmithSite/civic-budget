using CivicBudget.Domain.Common;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// One budget line across the seeded years, described by its FY2025 budget. The other years are
/// derived with small, deterministic variations so the demo shows realistic $ and % changes without
/// hand-maintaining seven numbers per line.
/// </summary>
internal sealed record SeedLine(string FundCode, string? DepartmentCode, string AccountCode, decimal Budget2025, decimal? Proposed2027 = null)
{
    private const decimal AnnualGrowth = 1.03m;

    public decimal Budget2024 => Whole(Budget2025 / AnnualGrowth);
    public decimal Actual2023 => Whole(Budget2024 * Factor(1));
    public decimal Actual2024 => Whole(Budget2024 * Factor(2));
    public decimal Actual2025 => Whole(Budget2025 * Factor(3));
    public decimal Budget2026 => Whole(Budget2025 * AnnualGrowth);
    public decimal Budget2027 => Proposed2027 ?? Whole(Budget2026 * 1.035m);

    /// <summary>
    /// Actuals land between 94% and 101% of budget, chosen by a stable hash of the line's identity.
    /// <c>string.GetHashCode</c> is randomized per process, so a hand-rolled hash keeps seeds reproducible.
    /// </summary>
    private decimal Factor(int salt)
    {
        int hash = salt;
        foreach (char c in $"{FundCode}|{DepartmentCode}|{AccountCode}")
        {
            hash = unchecked(hash * 31 + c);
        }

        int bucket = Math.Abs(hash) % 8; // 0..7
        return 0.94m + (bucket * 0.01m);
    }

    private static decimal Whole(decimal amount) => Money.Round(Math.Round(amount, 0, MidpointRounding.AwayFromZero));
}
