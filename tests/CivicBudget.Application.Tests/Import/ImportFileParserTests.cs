using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Application.Import;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Tests.Import;

public class ImportFileParserTests
{
    [Fact]
    public void Maps_columns_by_name_in_any_order_and_numbers_rows_like_the_spreadsheet()
    {
        var file = new TabularFile(
            ["Amount", "account", "FUND", "Department", "Justification"],
            [["100", "5100", "1000", "110", "Overtime"], ["", "", "", "", ""], ["7", "4100", "2011", null, null]]);

        Result<IReadOnlyList<ImportRowInput>> result = ImportFileParser.Parse(file, AccountNumberFormat.UanVillage);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count); // the blank row is skipped
        Assert.Equal(new ImportRowInput(2, "1000", "110", "5100", "100", null, null, "Overtime"), result.Value[0]);
        Assert.Equal(4, result.Value[1].RowNumber); // still row 4 in the file the clerk is looking at
        Assert.Null(result.Value[1].DepartmentCode);
    }

    [Fact]
    public void A_full_account_number_column_replaces_the_three_code_columns()
    {
        var file = new TabularFile(
            ["Account Number", "Amount", "Justification"],
            [["1000-110-5120", "58000", "Overtime"], ["1000-4110", "437081", null], ["1000.110.5120", "1", null], ["1000 110 5120", "2", null], ["10001105120", "3", null], ["not-a-number", "4", null]]);

        Result<IReadOnlyList<ImportRowInput>> result = ImportFileParser.Parse(file, AccountNumberFormat.UanVillage);

        Assert.True(result.IsSuccess);
        Assert.Equal(("1000", "110", "5120"), (result.Value[0].FundCode, result.Value[0].DepartmentCode, result.Value[0].AccountCode));
        Assert.Equal(("1000", null, "4110"), (result.Value[1].FundCode, result.Value[1].DepartmentCode, result.Value[1].AccountCode)); // fund-level revenue
        Assert.All(result.Value.Skip(2).Take(3), r => Assert.Equal(("1000", "110", "5120"), (r.FundCode, r.DepartmentCode, r.AccountCode))); // any separator, or none
        Assert.Equal(("not-a-number", null, null), (result.Value[5].FundCode, result.Value[5].DepartmentCode, result.Value[5].AccountCode)); // left for the analyser to report
    }

    [Fact]
    public void A_full_number_in_the_row_wins_over_the_code_columns()
    {
        var file = new TabularFile(["Account Number", "Fund", "Department", "Account", "Amount"], [["2011-620-5420", "1000", "110", "5120", "5"]]);

        ImportRowInput row = ImportFileParser.Parse(file, AccountNumberFormat.UanVillage).Value.Single();

        Assert.Equal(("2011", "620", "5420"), (row.FundCode, row.DepartmentCode, row.AccountCode));
    }

    [Fact]
    public void Names_the_missing_required_columns()
    {
        Result<IReadOnlyList<ImportRowInput>> result = ImportFileParser.Parse(new TabularFile(["Fund", "Note"], [["1000", "x"]]), AccountNumberFormat.UanVillage);

        Assert.True(result.IsFailure);
        Assert.Contains("Account Number column or Fund, Department, and Account", result.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_empty_file_and_a_header_only_file()
    {
        Assert.True(ImportFileParser.Parse(new TabularFile([], []), AccountNumberFormat.UanVillage).IsFailure);
        Assert.True(ImportFileParser.Parse(new TabularFile(["Fund", "Account", "Amount"], []), AccountNumberFormat.UanVillage).IsFailure);
    }
}
