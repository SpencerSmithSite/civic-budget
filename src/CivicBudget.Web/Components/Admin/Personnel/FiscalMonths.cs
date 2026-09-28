using System.Globalization;

namespace CivicBudget.Web.Components.Admin.Personnel;

/// <summary>
/// Positions count months of the fiscal year (1 is its first). People think in calendar months, so
/// screens name them: with a July start, month 1 is "July" and month 12 is "June".
/// </summary>
public static class FiscalMonths
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Name(int fiscalYearStartMonth, int month) =>
        Us.DateTimeFormat.GetMonthName(((fiscalYearStartMonth - 1 + month - 1) % 12) + 1);

    public static string Abbreviated(int fiscalYearStartMonth, int month) =>
        Us.DateTimeFormat.GetAbbreviatedMonthName(((fiscalYearStartMonth - 1 + month - 1) % 12) + 1);

    /// <summary>"All year", or "Apr–Dec" for a position paid part of it.</summary>
    public static string Span(int fiscalYearStartMonth, int first, int last) =>
        first == 1 && last == 12 ? "All year" : $"{Abbreviated(fiscalYearStartMonth, first)}–{Abbreviated(fiscalYearStartMonth, last)}";
}
