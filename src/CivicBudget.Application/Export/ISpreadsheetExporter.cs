namespace CivicBudget.Application.Export;

/// <summary>A table to export: a sheet name, column headers, and rows of cells (string, decimal, int, DateOnly, or null).</summary>
public sealed record ExportTable(string Name, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);

/// <summary>
/// Produces XLSX bytes server-side. Implemented in Infrastructure with ClosedXML (no Office, no COM).
/// CSV needs no library and lives in <see cref="CsvWriter"/>.
/// </summary>
public interface ISpreadsheetExporter
{
    byte[] ToXlsx(ExportTable table);
}
