using CivicBudget.Application.Assistant;

namespace CivicBudget.Application.Tests.Assistant;

/// <summary>"Fix this fund": the cut is shared in proportion and adds up to the cent.</summary>
public class FundTrimTests
{
    private static TrimLine Line(decimal amount) => new(Guid.NewGuid(), amount);

    [Fact]
    public void The_cut_is_shared_in_proportion_and_totals_exactly()
    {
        TrimLine[] lines = [Line(300_000m), Line(100_000m), Line(29_440.65m)];

        IReadOnlyList<TrimLine> trimmed = FundTrim.Trim(lines, 21_908.65m)!;

        Assert.Equal(lines.Sum(l => l.Amount) - 21_908.65m, trimmed.Sum(l => l.Amount));
        Assert.Equal(lines.Select(l => l.LineId), trimmed.Select(l => l.LineId));
        Assert.All(trimmed.Zip(lines), p => Assert.InRange(p.Second.Amount - p.First.Amount, 0m, 21_908.65m));
        Assert.Equal(15_305.01m, lines[0].Amount - trimmed[0].Amount); // 300,000 / 429,440.65 of the cut
    }

    [Fact]
    public void Leftover_cents_go_to_the_largest_line()
    {
        TrimLine[] lines = [Line(1m), Line(1m), Line(2m)];

        IReadOnlyList<TrimLine> trimmed = FundTrim.Trim(lines, 1m)!; // shares 0.25, 0.25, 0.50

        Assert.Equal([0.75m, 0.75m, 1.50m], trimmed.Select(l => l.Amount));
        IReadOnlyList<TrimLine> thirds = FundTrim.Trim([Line(1m), Line(1m), Line(1m)], 0.10m)!; // 0.03 each, a cent left over
        Assert.Equal(2.90m, thirds.Sum(l => l.Amount));
    }

    [Fact]
    public void Lines_that_hold_less_than_the_cut_cannot_fix_the_fund()
    {
        Assert.Null(FundTrim.Trim([Line(500m)], 500.01m));
        Assert.Equal(0m, FundTrim.Trim([Line(500m)], 500m)!.Single().Amount);
    }
}
