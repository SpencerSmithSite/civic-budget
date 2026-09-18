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

    /// <summary>"$1,234.50". Budgets are kept to the cent, so screens show the cent.</summary>
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

    /// <summary>"2 min ago", "Yesterday", or a date for anything older than a week.</summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset? now = null)
    {
        TimeSpan age = (now ?? DateTimeOffset.UtcNow) - when;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalHours < 24 ? $"{(int)age.TotalHours} h ago"
            : age.TotalDays < 2 ? "Yesterday"
            : age.TotalDays < 7 ? $"{(int)age.TotalDays} days ago"
            : when.ToString("MMM d, yyyy", UsCulture);
    }

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
