using System.Text;

namespace CivicBudget.Application.Export;

/// <summary>
/// RFC 4180 parser for the import: commas, optional quotes with doubled quotes inside, CRLF or LF
/// line ends, an optional UTF-8 BOM (Excel writes one). Small enough to read in one sitting, which
/// is why it is not a package. Cells come back as raw strings; the import decides what they mean.
/// </summary>
public static class CsvReader
{
    public static TabularFile Read(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();

        List<string?[]> records = [];
        List<string> fields = [];
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(cell.ToString());
                cell.Clear();
                records.Add(fields.ToArray());
                fields.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        // A file that does not end with a line break still has a last record in flight.
        if (cell.Length > 0 || fields.Count > 0)
        {
            fields.Add(cell.ToString());
            records.Add(fields.ToArray());
        }

        // Drop blank lines (Excel often leaves trailing ones).
        records.RemoveAll(r => r.All(string.IsNullOrWhiteSpace));

        if (records.Count == 0)
        {
            return new TabularFile([], []);
        }

        List<string> headers = records[0].Select(h => h?.Trim() ?? "").ToList();
        return new TabularFile(headers, records.Skip(1).ToList());
    }
}
