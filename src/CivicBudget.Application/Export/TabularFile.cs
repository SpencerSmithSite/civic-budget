namespace CivicBudget.Application.Export;

/// <summary>
/// A file read as a grid of text: one header row and the data rows under it, every cell a string
/// or null. CSV and XLSX both reduce to this, so the import rules are written once against it.
/// </summary>
public sealed record TabularFile(IReadOnlyList<string> Headers, IReadOnlyList<string?[]> Rows)
{
    /// <summary>Column index for a header, matched case-insensitively and ignoring spaces and underscores, or -1.</summary>
    public int IndexOf(string header)
    {
        string wanted = Normalize(header);
        for (int i = 0; i < Headers.Count; i++)
        {
            if (Normalize(Headers[i]) == wanted)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Normalize(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Trim().ToUpperInvariant();
}

/// <summary>
/// Reads an XLSX file's first worksheet into a <see cref="TabularFile"/>. Implemented in
/// Infrastructure with ClosedXML; CSV needs no library and is parsed by <see cref="CsvReader"/>.
/// </summary>
public interface ISpreadsheetReader
{
    TabularFile Read(Stream content);
}
