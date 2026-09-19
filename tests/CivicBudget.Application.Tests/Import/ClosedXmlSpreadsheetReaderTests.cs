using CivicBudget.Application.Export;
using CivicBudget.Infrastructure.Export;

namespace CivicBudget.Application.Tests.Import;

/// <summary>The round trip the clerk will do: export lines, edit in Excel, import. Also the failure mode for a wrong file.</summary>
public class ClosedXmlSpreadsheetReaderTests
{
    [Fact]
    public void Reads_back_what_the_exporter_wrote_as_text_cells()
    {
        var table = new ExportTable("Lines", ["Fund", "Department", "Amount", "Note"], [["1000", "110", 1_250.5m, null], ["2011", null, 7m, "x"]]);
        byte[] xlsx = new ClosedXmlSpreadsheetExporter().ToXlsx(table);

        TabularFile file = new ClosedXmlSpreadsheetReader().Read(new MemoryStream(xlsx));

        Assert.Equal(["Fund", "Department", "Amount", "Note"], file.Headers);
        Assert.Equal<string?[]>(["1000", "110", "1250.5", null], file.Rows[0]);
        Assert.Equal<string?[]>(["2011", null, "7", "x"], file.Rows[1]);
    }

    [Fact]
    public void A_file_that_is_not_a_workbook_is_reported_in_plain_words()
    {
        var reader = new ClosedXmlSpreadsheetReader();

        var ex = Assert.Throws<InvalidDataException>(() => reader.Read(new MemoryStream("Fund,Amount\r\n1000,5\r\n"u8.ToArray())));

        Assert.Contains(".xlsx", ex.Message, StringComparison.Ordinal);
    }
}
