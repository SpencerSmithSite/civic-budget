using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Application.Import;

namespace CivicBudget.Application.Tests.Import;

public class ImportFileParserTests
{
    [Fact]
    public void Maps_columns_by_name_in_any_order_and_numbers_rows_like_the_spreadsheet()
    {
        var file = new TabularFile(
            ["Amount", "account", "FUND", "Department", "Justification"],
            [["100", "5100", "1000", "PD", "Overtime"], ["", "", "", "", ""], ["7", "4100", "2011", null, null]]);

        Result<IReadOnlyList<ImportRowInput>> result = ImportFileParser.Parse(file);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count); // the blank row is skipped
        Assert.Equal(new ImportRowInput(2, "1000", "PD", "5100", "100", null, null, "Overtime"), result.Value[0]);
        Assert.Equal(4, result.Value[1].RowNumber); // still row 4 in the file the clerk is looking at
        Assert.Null(result.Value[1].DepartmentCode);
    }

    [Fact]
    public void Names_the_missing_required_columns()
    {
        Result<IReadOnlyList<ImportRowInput>> result = ImportFileParser.Parse(new TabularFile(["Fund", "Note"], [["1000", "x"]]));

        Assert.True(result.IsFailure);
        Assert.Contains("Account, Amount", result.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_empty_file_and_a_header_only_file()
    {
        Assert.True(ImportFileParser.Parse(new TabularFile([], [])).IsFailure);
        Assert.True(ImportFileParser.Parse(new TabularFile(["Fund", "Account", "Amount"], [])).IsFailure);
    }
}
