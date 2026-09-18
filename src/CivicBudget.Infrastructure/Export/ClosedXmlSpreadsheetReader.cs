using CivicBudget.Application.Export;
using ClosedXML.Excel;

namespace CivicBudget.Infrastructure.Export;

/// <summary>
/// Reads the first worksheet of an XLSX into text cells. Numbers and dates come back as
/// invariant-culture strings so the import parses them the same way it parses a CSV; a clerk
/// who typed "1,250" and one whose Excel stored 1250 get the same result.
/// </summary>
public sealed class ClosedXmlSpreadsheetReader : ISpreadsheetReader
{
    public TabularFile Read(Stream content)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(content);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // ClosedXML throws a variety of types for a corrupt or non-XLSX stream; one message serves the clerk.
            throw new InvalidDataException("The file could not be read as an Excel workbook (.xlsx).", ex);
        }

        using (workbook)
        {
            IXLWorksheet? sheet = workbook.Worksheets.FirstOrDefault();
            IXLRange? used = sheet?.RangeUsed();
            if (sheet is null || used is null)
            {
                return new TabularFile([], []);
            }

            int firstColumn = used.RangeAddress.FirstAddress.ColumnNumber;
            int lastColumn = used.RangeAddress.LastAddress.ColumnNumber;
            int firstRow = used.RangeAddress.FirstAddress.RowNumber;
            int lastRow = used.RangeAddress.LastAddress.RowNumber;

            List<string> headers = [];
            for (int c = firstColumn; c <= lastColumn; c++)
            {
                headers.Add(Text(sheet.Cell(firstRow, c)) ?? "");
            }

            List<string?[]> rows = [];
            for (int r = firstRow + 1; r <= lastRow; r++)
            {
                var cells = new string?[headers.Count];
                for (int c = firstColumn; c <= lastColumn; c++)
                {
                    cells[c - firstColumn] = Text(sheet.Cell(r, c));
                }

                if (cells.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    rows.Add(cells);
                }
            }

            return new TabularFile(headers, rows);
        }
    }

    private static string? Text(IXLCell cell) => cell.DataType switch
    {
        XLDataType.Blank => null,
        XLDataType.Number => cell.GetDouble().ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
        XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
        _ => string.IsNullOrWhiteSpace(cell.GetString()) ? null : cell.GetString().Trim(),
    };
}
