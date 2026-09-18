using System.Globalization;

namespace CivicBudget.Web.Components.Portal.Common;

/// <summary>"$3.66M", "$588K", "$950": the KPI card format citizens scan. Full precision lives in the tables.</summary>
public static class MoneyShort
{
    public static string Format(decimal amount)
    {
        decimal abs = Math.Abs(amount);
        string sign = amount < 0 ? "-" : "";
        return abs >= 1_000_000m ? $"{sign}${(abs / 1_000_000m).ToString("0.00", CultureInfo.InvariantCulture)}M"
            : abs >= 10_000m ? $"{sign}${(abs / 1_000m).ToString("0", CultureInfo.InvariantCulture)}K"
            : $"{sign}${abs.ToString("N0", CultureInfo.InvariantCulture)}";
    }
}
