using System.Globalization;
using System.Text;

namespace CivicBudget.Application.Export;

/// <summary>
/// RFC 4180 CSV: comma separated, CRLF lines, quotes doubled, a UTF-8 BOM so Excel opens it correctly.
/// Text that a spreadsheet would read as a formula (it starts with =, +, -, or @) gets a leading
/// apostrophe: account names and justifications are typed by people, and the public portal's CSV
/// is opened by anyone. Numbers are written as numbers, so a negative change ("-1500.00") is left alone.
/// </summary>
public static class CsvWriter
{
    public static byte[] ToCsv(ExportTable table)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", table.Headers.Select(Quote))).Append("\r\n");
        foreach (object?[] row in table.Rows)
        {
            sb.Append(string.Join(",", row.Select(cell => Quote(Format(cell))))).Append("\r\n");
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Format(object? cell) => cell switch
    {
        null => "",
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => DefuseFormula(cell.ToString() ?? ""),
    };

    private static string DefuseFormula(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Quote(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
