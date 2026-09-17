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

    public static string Money(decimal amount) => amount.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
}
