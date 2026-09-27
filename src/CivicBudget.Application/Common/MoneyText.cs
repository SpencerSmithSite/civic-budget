using System.Globalization;
using System.Text.RegularExpressions;

namespace CivicBudget.Application.Common;

/// <summary>
/// Reads an amount typed into a spreadsheet by a person. Shared by the budget import and the ERP
/// actuals file so both accept the same spellings and refuse the same mistakes.
/// </summary>
public static partial class MoneyText
{
    /// <summary>Accepts "1,234.50", "$1,234.50", and plain numbers. "(500)" is read as -500 so the negative-amount rule can name it, rather than calling it "not a number".</summary>
    public static decimal? Parse(string? text, string column, bool required, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required)
            {
                errors.Add($"{column} is missing.");
            }

            return null;
        }

        string cleaned = text.Trim().Replace("$", "", StringComparison.Ordinal);
        bool negative = cleaned.StartsWith('(') && cleaned.EndsWith(')');
        if (negative)
        {
            cleaned = "-" + cleaned[1..^1];
        }

        // NumberStyles.Number accepts a comma anywhere, so "1234,56" (a decimal comma) would import as
        // 123,456.00. Commas are only accepted as thousands separators in groups of three.
        if (cleaned.Contains(',', StringComparison.Ordinal) && !ThousandsSeparated().IsMatch(cleaned))
        {
            errors.Add($"{column} \"{text}\" has a comma in the wrong place. Use a period for cents, as in 1,234.50.");
            return null;
        }

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return Domain.Common.Money.Round(value);
        }

        errors.Add($"{column} \"{text}\" is not a number.");
        return null;
    }

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+(\.\d+)?$")]
    private static partial Regex ThousandsSeparated();
}
