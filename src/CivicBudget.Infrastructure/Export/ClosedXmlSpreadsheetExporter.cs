using CivicBudget.Application.Export;
using ClosedXML.Excel;

namespace CivicBudget.Infrastructure.Export;

/// <summary>XLSX via ClosedXML: a header row, typed cells (numbers stay numbers), a frozen header, and auto-fit columns.</summary>
public sealed class ClosedXmlSpreadsheetExporter : ISpreadsheetExporter
{
    public byte[] ToXlsx(ExportTable table)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add(table.Name.Length > 31 ? table.Name[..31] : table.Name);

        for (int c = 0; c < table.Headers.Count; c++)
        {
            IXLCell header = sheet.Cell(1, c + 1);
            header.Value = table.Headers[c];
            header.Style.Font.Bold = true;
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            object?[] row = table.Rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                IXLCell cell = sheet.Cell(r + 2, c + 1);
                switch (row[c])
                {
                    case null:
                        break;
                    case decimal d:
                        cell.Value = d;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case int i:
                        cell.Value = i;
                        break;
                    case DateOnly date:
                        cell.Value = date.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        break;
                    default:
                        cell.Value = row[c]!.ToString();
                        break;
                }
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
