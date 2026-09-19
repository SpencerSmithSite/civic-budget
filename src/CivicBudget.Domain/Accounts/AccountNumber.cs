namespace CivicBudget.Domain.Accounts;

/// <summary>
/// Composes and parses full account numbers under a government's <see cref="AccountNumberFormat"/>.
/// Pure functions over strings: the codes themselves stay on Fund, Department, and Account.
/// </summary>
public static class AccountNumber
{
    /// <summary>"1000-725-121", or "1000-110" when there is no department (a revenue line budgeted at fund level).</summary>
    public static string Compose(AccountNumberFormat format, string fundCode, string? departmentCode, string objectCode) =>
        departmentCode is null
            ? $"{Pad(fundCode, format.FundWidth)}{format.Separator}{Pad(objectCode, format.ObjectWidth)}"
            : $"{Pad(fundCode, format.FundWidth)}{format.Separator}{Pad(departmentCode, format.DepartmentWidth)}{format.Separator}{Pad(objectCode, format.ObjectWidth)}";

    /// <summary>
    /// Reads "1000-725-121", "1000.725.121", "1000 725 121", or "1000725121" (widths decide) into its
    /// codes. Two segments mean fund and object with no department. Returns false for anything else,
    /// so callers can fall back to matching the text against names.
    /// </summary>
    public static bool TryParse(AccountNumberFormat format, string? text, out string fundCode, out string? departmentCode, out string objectCode)
    {
        fundCode = "";
        departmentCode = null;
        objectCode = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split([format.Separator[0], '-', '.', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            // No separators: only an all-digit string of exactly the full or the two-segment width is unambiguous.
            string digits = parts[0];
            if (!digits.All(char.IsAsciiDigit))
            {
                return false;
            }

            if (digits.Length == format.FundWidth + format.DepartmentWidth + format.ObjectWidth)
            {
                parts = [digits[..format.FundWidth], digits.Substring(format.FundWidth, format.DepartmentWidth), digits[(format.FundWidth + format.DepartmentWidth)..]];
            }
            else if (digits.Length == format.FundWidth + format.ObjectWidth)
            {
                parts = [digits[..format.FundWidth], digits[format.FundWidth..]];
            }
            else
            {
                return false;
            }
        }

        // Fund numbers are numeric in every Ohio chart, and no segment carries punctuation; that is
        // enough to tell "1000-725-121" from a search phrase like "street-lights".
        if (!parts[0].All(char.IsAsciiDigit) || parts.Any(p => !p.All(char.IsAsciiLetterOrDigit)))
        {
            return false;
        }

        switch (parts.Length)
        {
            case 2:
                (fundCode, objectCode) = (parts[0], parts[1]);
                return true;
            case 3:
                (fundCode, departmentCode, objectCode) = (parts[0], parts[1], parts[2]);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Codes shorter than the width are left-padded with zeros only when they are all digits; "PD" stays "PD".</summary>
    private static string Pad(string code, int width) =>
        code.Length < width && code.All(char.IsAsciiDigit) ? code.PadLeft(width, '0') : code;
}
