using CivicBudget.Application.Export;
using CivicBudget.Infrastructure.Export;
using ClosedXML.Excel;

namespace CivicBudget.Application.Tests.Export;

/// <summary>Round-trips the XLSX through ClosedXML to prove numbers stay numbers (so Excel can SUM them) and the header is frozen.</summary>
public class ClosedXmlSpreadsheetExporterTests
{
    [Fact]
    public void Writes_typed_cells_under_a_bold_frozen_header()
    {
        var exporter = new ClosedXmlSpreadsheetExporter();
        var table = new ExportTable(
            "A sheet name that is far too long for Excel's limit",
            ["Fund", "Amount", "Adopted", "Note", "Year"],
            [["1000", 1_234.5m, new DateOnly(2026, 6, 15), null, 2026]]);

        byte[] bytes = exporter.ToXlsx(table);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        IXLWorksheet sheet = workbook.Worksheets.Single();
        Assert.Equal(31, sheet.Name.Length);                       // Excel refuses longer sheet names
        Assert.True(sheet.Cell("A1").Style.Font.Bold);
        Assert.Equal("Amount", sheet.Cell("B1").GetString());
        Assert.Equal(1, sheet.SheetView.SplitRow);                 // frozen header row
        Assert.Equal(XLDataType.Text, sheet.Cell("A2").DataType);  // fund codes keep their leading zeros
        Assert.Equal(XLDataType.Number, sheet.Cell("B2").DataType);
        Assert.Equal(1_234.5, sheet.Cell("B2").GetDouble());
        Assert.Equal("#,##0.00", sheet.Cell("B2").Style.NumberFormat.Format);
        Assert.Equal(XLDataType.DateTime, sheet.Cell("C2").DataType);
        Assert.Equal(new DateTime(2026, 6, 15), sheet.Cell("C2").GetDateTime());
        Assert.True(sheet.Cell("D2").IsEmpty());
        Assert.Equal(2026, sheet.Cell("E2").GetValue<int>());
    }
}
