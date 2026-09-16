namespace CivicBudget.Domain.FiscalYears;

/// <summary>
/// The calendar span of a fiscal year. A fiscal year is labelled by the calendar year in which it
/// <em>ends</em>: with a July start, "FY2027" runs 2026‑07‑01 through 2027‑06‑30. With a January
/// start the label and the calendar year coincide.
/// </summary>
public readonly record struct FiscalPeriod(DateOnly Start, DateOnly End)
{
    public static FiscalPeriod For(int year, int startMonth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startMonth, 12);

        DateOnly start = startMonth == 1
            ? new DateOnly(year, 1, 1)
            : new DateOnly(year - 1, startMonth, 1);
        DateOnly end = start.AddYears(1).AddDays(-1);
        return new FiscalPeriod(start, end);
    }

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
