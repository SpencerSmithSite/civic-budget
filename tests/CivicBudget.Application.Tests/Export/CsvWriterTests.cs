using System.Text;
using CivicBudget.Application.Export;

namespace CivicBudget.Application.Tests.Export;

/// <summary>
/// The CSV download is the format most likely to be opened in Excel by a clerk or a reporter, so
/// the rules that make that work (BOM, CRLF, quoting, invariant numbers) each get a test.
/// </summary>
public class CsvWriterTests
{
    [Fact]
    public void Writes_a_header_row_then_one_line_per_row_with_crlf_and_a_bom()
    {
        var table = new ExportTable("Lines", ["Fund", "Amount"], [["1000", 1_234.5m], ["2011", 0m]]);

        byte[] bytes = CsvWriter.ToCsv(table);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal("Fund,Amount\r\n1000,1234.50\r\n2011,0.00\r\n", text);
    }

    [Fact]
    public void Quotes_cells_that_contain_commas_quotes_or_newlines()
    {
        var table = new ExportTable("Lines", ["Name"], [["Street Construction, Maintenance & Repair"], ["Say \"hi\""], ["two\nlines"], ["plain"]]);

        string text = Text(CsvWriter.ToCsv(table));

        Assert.Equal("Name\r\n\"Street Construction, Maintenance & Repair\"\r\n\"Say \"\"hi\"\"\"\r\n\"two\nlines\"\r\nplain\r\n", text);
    }

    [Fact]
    public void Text_that_a_spreadsheet_would_run_as_a_formula_is_written_as_text()
    {
        var table = new ExportTable("Lines", ["Note", "Amount"], [["=HYPERLINK(\"http://x\",\"Click\")", -250m], ["+1 overtime", 0m], ["-see memo", 0m], ["@SUM(A1)", 0m], ["plain", 0m]]);

        string text = Text(CsvWriter.ToCsv(table));

        Assert.Equal("Note,Amount\r\n\"'=HYPERLINK(\"\"http://x\"\",\"\"Click\"\")\",-250.00\r\n'+1 overtime,0.00\r\n'-see memo,0.00\r\n'@SUM(A1),0.00\r\nplain,0.00\r\n", text);
    }

    [Fact]
    public void Formats_money_dates_and_nulls_the_same_on_every_machine()
    {
        var table = new ExportTable("Lines", ["Amount", "Adopted", "Note", "Count"], [[-5m, new DateOnly(2026, 6, 15), null, 7]]);

        string text = Text(CsvWriter.ToCsv(table));

        Assert.Equal("Amount,Adopted,Note,Count\r\n-5.00,2026-06-15,,7\r\n", text);
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
}
