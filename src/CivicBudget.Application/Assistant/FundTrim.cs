namespace CivicBudget.Application.Assistant;

/// <summary>A line as the trim needs it: its id and amount.</summary>
public sealed record TrimLine(Guid LineId, decimal Amount);

/// <summary>
/// "Fix the Street fund": takes a dollar amount off a fund's spending lines in proportion to each
/// line's size, to the cent, so the fund ends exactly at its limit. Pure, so the arithmetic is tested
/// and the model never does it: rounding each share leaves a few cents over or under, and those go
/// to the largest line so the total cut is exact.
/// </summary>
public static class FundTrim
{
    /// <returns>The new amount for every line, in the order given; null when the lines together hold less than the cut.</returns>
    public static IReadOnlyList<TrimLine>? Trim(IReadOnlyList<TrimLine> lines, decimal cut)
    {
        decimal total = lines.Sum(l => l.Amount);
        if (cut <= 0m)
        {
            return lines;
        }

        if (total < cut)
        {
            return null;
        }

        decimal[] shares = [.. lines.Select(l => Domain.Common.Money.Round(cut * l.Amount / total))];
        int largest = lines.Select((l, i) => (l, i)).OrderByDescending(x => x.l.Amount).ThenBy(x => x.l.LineId).First().i;
        shares[largest] += cut - shares.Sum();

        List<TrimLine> trimmed = [.. lines.Select((l, i) => l with { Amount = l.Amount - shares[i] })];
        return trimmed.Any(l => l.Amount < 0m) ? null : trimmed;
    }
}
