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

    /// <summary>Where FY2026 and FY2027 end up; the simulated ERP reports them a month at a time.</summary>
    public decimal Actual2026 => Whole(Budget2026 * Factor(4));
    public decimal Actual2027 => Whole(Budget2027 * Factor(5));

    /// <summary>The whole year's actual, or null for a year the fictional books do not reach.</summary>
    public decimal? ActualFor(int year) => year switch
    {
        2023 => Actual2023,
        2024 => Actual2024,
        2025 => Actual2025,
        2026 => Actual2026,
        2027 => Actual2027,
        _ => null,
    };

    /// <summary>
    /// Actuals land between 94% and 101% of budget, chosen by a stable hash of the line's identity.
    /// <c>string.GetHashCode</c> is randomized per process, so a hand-rolled hash keeps seeds reproducible.
    /// </summary>
    private decimal Factor(int salt)
    {
        int bucket = Math.Abs(Hash(salt)) % 8; // 0..7
        return 0.94m + (bucket * 0.01m);
    }

    /// <summary>A stable hash of the line's identity and a salt.</summary>
    public int Hash(int salt)
    {
        int hash = salt;
        foreach (char c in $"{FundCode}|{DepartmentCode}|{AccountCode}")
        {
            hash = unchecked(hash * 31 + c);
        }

        return hash;
    }

    private static decimal Whole(decimal amount) => Money.Round(Math.Round(amount, 0, MidpointRounding.AwayFromZero));
}
