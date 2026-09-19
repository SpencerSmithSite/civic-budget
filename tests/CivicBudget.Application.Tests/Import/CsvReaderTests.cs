using System.Text;
using CivicBudget.Application.Export;

namespace CivicBudget.Application.Tests.Import;

public class CsvReaderTests
{
    [Fact]
    public void Reads_headers_and_rows_with_quotes_commas_and_crlf()
    {
        TabularFile file = Read("Fund,Department,Account,Amount\r\n1000,110,\"5100\",\"1,250.00\"\r\n2011,,4100,\"Say \"\"hi\"\"\"\r\n");

        Assert.Equal(["Fund", "Department", "Account", "Amount"], file.Headers);
        Assert.Equal(2, file.Rows.Count);
        Assert.Equal<string?[]>(["1000", "110", "5100", "1,250.00"], file.Rows[0]);
        Assert.Equal<string?[]>(["2011", "", "4100", "Say \"hi\""], file.Rows[1]);
    }

    [Fact]
    public void Tolerates_a_bom_lf_line_ends_a_missing_final_newline_and_blank_lines()
    {
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Fund,Amount\n\n1000,5\n,\n2011,6")).ToArray();

        TabularFile file = CsvReader.Read(new MemoryStream(bytes));

        Assert.Equal(["Fund", "Amount"], file.Headers);
        Assert.Equal([["1000", "5"], ["2011", "6"]], file.Rows);
    }

    [Fact]
    public void Keeps_newlines_inside_quoted_cells()
    {
        TabularFile file = Read("Fund,Justification\r\n1000,\"two\r\nlines\"\r\n");

        Assert.Equal("two\r\nlines", file.Rows[0][1]);
    }

    [Fact]
    public void An_empty_file_has_no_headers()
    {
        Assert.Empty(Read("").Headers);
        Assert.Empty(Read("\r\n\r\n").Headers);
    }

    [Fact]
    public void Header_lookup_ignores_case_spaces_and_underscores()
    {
        TabularFile file = Read("fund,prior_year_actual,Current Year Budget\r\n");

        Assert.Equal(0, file.IndexOf("Fund"));
        Assert.Equal(1, file.IndexOf("Prior Year Actual"));
        Assert.Equal(2, file.IndexOf("CURRENTYEARBUDGET"));
        Assert.Equal(-1, file.IndexOf("Amount"));
    }

    private static TabularFile Read(string text) => CsvReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));
}
