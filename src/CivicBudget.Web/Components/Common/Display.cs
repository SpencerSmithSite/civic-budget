using System.Text;

namespace CivicBudget.Web.Components.Common;

/// <summary>Small formatting helpers for screens. Presentation only; no business meaning.</summary>
public static class Display
{
    /// <summary>"SuppliesAndMaterials" becomes "Supplies &amp; Materials"; "TransferIn" becomes "Transfer In".</summary>
    public static string Enum(Enum value) => Enum(value.ToString());

    private static string Enum(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append(' ');
            }

            sb.Append(name[i]);
        }

        return sb.ToString().Replace(" And ", " & ", StringComparison.Ordinal);
    }

    private static readonly System.Globalization.CultureInfo UsCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    /// <summary>"Amount" stays "Amount"; "PriorYearActual" becomes "Prior year actual".</summary>
    public static string PropertyName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        string spaced = Enum(name);
        return spaced[..1] + spaced[1..].ToLowerInvariant();
    }

    /// <summary>"BudgetLine" becomes "budget line" for sentences.</summary>
    public static string EntityName(string name) => Enum(name).ToLowerInvariant();

    /// <summary>"AR" for "Alex Rivera (Admin)": first letters of the first and last word that start with a letter.</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetter(p[0])).ToArray();
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant() : name.Trim()[..1].ToUpperInvariant();
    }

    // Every government in the product is in Ohio, which is entirely Eastern time, so times are shown
    // there rather than in the server's zone (UTC in a container, so a 2 PM sync read "6:00 PM"). A
    // multi-state product would keep a time zone per government. If the zone data is missing from
    // the host, times stay in UTC and say so.
    private static readonly TimeZoneInfo? Eastern = FindEastern();

    private static TimeZoneInfo? FindEastern()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static DateTimeOffset InEastern(DateTimeOffset when) => Eastern is null ? when.ToUniversalTime() : TimeZoneInfo.ConvertTime(when, Eastern);

    /// <summary>"Sep 23, 2026 2:05 PM ET": a moment, with its zone, for logs of who did what.</summary>
    public static string Timestamp(DateTimeOffset when) =>
        InEastern(when).ToString("MMM d, yyyy h:mm tt", UsCulture) + (Eastern is null ? " UTC" : " ET");

    /// <summary>"Jul 1, 2027": a calendar date such as a fiscal year's start, in US format whatever the server culture.</summary>
    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", UsCulture);

    /// <summary>"Sep 23, 2026", on the Eastern calendar.</summary>
    public static string ShortDate(DateTimeOffset when) => InEastern(when).ToString("MMM d, yyyy", UsCulture);

    /// <summary>"September 23, 2026": the Eastern calendar date, so an evening event is not dated tomorrow.</summary>
    public static string LongDate(DateTimeOffset when) => InEastern(when).ToString("MMMM d, yyyy", UsCulture);

    /// <summary>"2 min ago", "Yesterday", or a date for anything older than a week.</summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset? now = null)
    {
        TimeSpan age = (now ?? DateTimeOffset.UtcNow) - when;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalHours < 24 ? $"{(int)age.TotalHours} h ago"
            : age.TotalDays < 2 ? "Yesterday"
            : age.TotalDays < 7 ? $"{(int)age.TotalDays} days ago"
            : InEastern(when).ToString("MMM d, yyyy", UsCulture);
    }

    /// <summary>"1,234.50": a money column whose header says it is dollars. Always US formatting, whatever the server's culture.</summary>
    public static string Amount(decimal amount) => amount.ToString("N2", UsCulture);

    /// <summary>"1,235": a summary figure rounded to the dollar.</summary>
    public static string WholeAmount(decimal amount) => amount.ToString("N0", UsCulture);

    /// <summary>"$1,234.50". Budgets are kept to the cent, so screens show the cent.</summary>
    public static string Money(decimal amount) => amount.ToString("C2", UsCulture);

    /// <summary>"+12.5%" / "-3.0%", or "new" when there is no baseline to compare against.</summary>
    public static string Percent(decimal? percent) => percent is { } p
        ? (p >= 0 ? "+" : "") + p.ToString("0.0", UsCulture) + "%"
        : "new";

    /// <summary>"$1,234.50" or "-$1,234.50": a plain sign for values that may be negative (balances).</summary>
    public static string SignedOrPlain(decimal amount) => (amount < 0 ? "-" : "") + Math.Abs(amount).ToString("C2", UsCulture);

    /// <summary>Change columns in worksheet style: "+14,781.00", "(12,490.00)", or "0.00".</summary>
    public static string SignedChange(decimal amount) => amount < 0
        ? "(" + Math.Abs(amount).ToString("N2", UsCulture) + ")"
        : (amount > 0 ? "+" : "") + amount.ToString("N2", UsCulture);

    /// <summary>Signed money for change columns: "+$500.00" / "-$500.00".</summary>
    public static string SignedMoney(decimal amount) => (amount >= 0 ? "+" : "-") + Math.Abs(amount).ToString("C2", UsCulture);
}
