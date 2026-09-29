using System.Globalization;
using Microsoft.AspNetCore.Components;

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

    /// <summary>The same amount in words a screen reader says clearly: "$3.66 million", "minus $588 thousand".</summary>
    public static string Spoken(decimal amount)
    {
        decimal abs = Math.Abs(amount);
        string sign = amount < 0 ? "minus " : "";
        return abs >= 1_000_000m ? $"{sign}${(abs / 1_000_000m).ToString("0.00", CultureInfo.InvariantCulture)} million"
            : abs >= 10_000m ? $"{sign}${(abs / 1_000m).ToString("0", CultureInfo.InvariantCulture)} thousand"
            : $"{sign}${abs.ToString("N0", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The short form for the eye and the spoken form for a screen reader, which may otherwise read
    /// "$3.66M" as "three dollars sixty-six M".
    /// </summary>
    public static RenderFragment Speakable(decimal amount) => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "aria-hidden", "true");
        builder.AddContent(2, Format(amount));
        builder.CloseElement();
        builder.OpenElement(3, "span");
        builder.AddAttribute(4, "class", "visually-hidden");
        builder.AddContent(5, Spoken(amount));
        builder.CloseElement();
    };
}
