using System.Text;

namespace CivicBudget.Web.Components.Common;

/// <summary>Small formatting helpers for screens. Presentation only; no business meaning.</summary>
public static class Display
{
    /// <summary>"SuppliesAndMaterials" becomes "Supplies &amp; Materials"; "TransferIn" becomes "Transfer In".</summary>
    public static string Enum(Enum value)
    {
        string name = value.ToString();
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
    public static string Money(decimal amount) => amount.ToString("C2", UsCulture);

    /// <summary>"+12.5%" / "-3.0%", or "new" when there is no baseline to compare against.</summary>
    public static string Percent(decimal? percent) => percent is { } p
        ? (p >= 0 ? "+" : "") + p.ToString("0.0", UsCulture) + "%"
        : "new";

    /// <summary>Signed money for change columns: "+$500.00" / "-$500.00".</summary>
    public static string SignedMoney(decimal amount) => (amount >= 0 ? "+" : "-") + Math.Abs(amount).ToString("C2", UsCulture);
}
