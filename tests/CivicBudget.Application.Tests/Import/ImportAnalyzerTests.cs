using CivicBudget.Application.Import;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Tests.Import;

/// <summary>Every import rule, against hand-built lookups. The service test proves the same rules against a real database.</summary>
public class ImportAnalyzerTests
{
    private static readonly ImportLookup General = new(Guid.NewGuid(), "1000", "General Fund", true);
    private static readonly ImportLookup Closed = new(Guid.NewGuid(), "9000", "Closed Fund", false);
    private static readonly ImportLookup Police = new(Guid.NewGuid(), "110", "Police", true);
    private static readonly ImportLookup Fire = new(Guid.NewGuid(), "FD", "Fire", true);
    private static readonly ImportLookup Salaries = new(Guid.NewGuid(), "5100", "Salaries", true, AccountType.Expenditure);
    private static readonly ImportLookup PropertyTax = new(Guid.NewGuid(), "4100", "Property tax", true, AccountType.Revenue);

    private static IReadOnlyList<ImportRowDto> Analyze(IReadOnlyList<ExistingLine> existing, params ImportRowInput[] rows) =>
        ImportAnalyzer.Analyze(rows, [General, Closed], [Police, Fire], [Salaries, PropertyTax], existing);

    private static ImportRowInput Row(int n, string? fund, string? dept, string? account, string? amount, string? prior = null, string? current = null, string? note = null) =>
        new(n, fund, dept, account, amount, prior, current, note);

    [Fact]
    public void A_new_clean_row_is_an_add_with_resolved_names_and_parsed_money()
    {
        ImportRowDto row = Analyze([], Row(2, "1000", "110", "5100", "$1,250.50", "(10)", "1000", "Overtime")).Single();

        Assert.Equal(ImportRowAction.Add, row.Action);
        Assert.Empty(row.Errors);
        Assert.Equal(("General Fund", "Police", "Salaries"), (row.FundName, row.DepartmentName, row.AccountName));
        Assert.Equal((1_250.50m, -10m, 1000m), (row.Amount, row.PriorYearActual, row.CurrentYearBudget));
        Assert.Null(row.ExistingAmount);
    }

    [Fact]
    public void An_amount_too_large_for_the_database_is_an_error_not_a_crash_at_commit()
    {
        ImportRowDto row = Analyze([], Row(2, "1000", "110", "5100", "99999999999999999999")).Single();

        Assert.Contains("That amount is too large.", row.Errors);
    }

    [Fact]
    public void Codes_padded_with_zeros_by_an_export_match_their_unpadded_codes()
    {
        var shortFund = new ImportLookup(Guid.NewGuid(), "101", "Short Fund", true);

        ImportRowDto row = ImportAnalyzer.Analyze([Row(2, "0101", "110", "05100", "10")], [shortFund], [Police], [Salaries], []).Single();

        Assert.Empty(row.Errors);
        Assert.Equal(("Short Fund", "Salaries"), (row.FundName, row.AccountName));
    }

    [Theory]
    [InlineData("1234,56")]
    [InlineData("1,23")]
    [InlineData("12,34,567")]
    public void A_comma_that_is_not_a_thousands_separator_is_an_error_not_a_bigger_number(string amount)
    {
        ImportRowDto row = Analyze([], Row(2, "1000", "110", "5100", amount)).Single();

        Assert.Contains(row.Errors, e => e.Contains("comma in the wrong place", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1,234.50", 1_234.50)]
    [InlineData("12,345,678", 12_345_678)]
    [InlineData("$1,000", 1_000)]
    public void Thousands_separators_in_groups_of_three_are_accepted(string amount, double expected)
    {
        ImportRowDto row = Analyze([], Row(2, "1000", "110", "5100", amount)).Single();

        Assert.Empty(row.Errors);
        Assert.Equal((decimal)expected, row.Amount);
    }

    [Fact]
    public void Matching_an_existing_line_with_the_same_values_is_unchanged_and_shows_the_current_amount()
    {
        var existing = new ExistingLine(Guid.NewGuid(), General.Id, Police.Id, Salaries.Id, 500m, 480m, 450m, null);

        ImportRowDto row = Analyze([existing], Row(2, "1000", "110", "5100", "500")).Single();

        Assert.Equal(ImportRowAction.Unchanged, row.Action);
        Assert.Equal(500m, row.ExistingAmount);
    }

    [Fact]
    public void Blank_optional_columns_leave_existing_comparatives_alone_so_only_a_different_amount_is_an_update()
    {
        var existing = new ExistingLine(Guid.NewGuid(), General.Id, Police.Id, Salaries.Id, 500m, 480m, 450m, "why");

        Assert.Equal(ImportRowAction.Update, Analyze([existing], Row(2, "1000", "110", "5100", "600")).Single().Action);
        Assert.Equal(ImportRowAction.Update, Analyze([existing], Row(2, "1000", "110", "5100", "500", prior: "0")).Single().Action);
        Assert.Equal(ImportRowAction.Update, Analyze([existing], Row(2, "1000", "110", "5100", "500", note: "new why")).Single().Action);
        Assert.Equal(ImportRowAction.Unchanged, Analyze([existing], Row(2, "1000", "110", "5100", "500", prior: "480", current: "450", note: "why")).Single().Action);
    }

    [Fact]
    public void Unknown_inactive_and_missing_codes_are_row_errors_that_name_the_code()
    {
        IReadOnlyList<ImportRowDto> rows = Analyze([],
            Row(2, "1234", "110", "5100", "1"),
            Row(3, "9000", "110", "5100", "1"),
            Row(4, null, "110", "5100", "1"),
            Row(5, "1000", "ZZ", "5100", "1"),
            Row(6, "1000", "110", "0000", "1"));

        Assert.All(rows, r => Assert.Equal(ImportRowAction.Error, r.Action));
        Assert.Contains("\"1234\"", rows[0].Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("inactive", rows[1].Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("fund code is missing", rows[2].Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("department", rows[3].Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("account", rows[4].Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Expenditures_need_a_department_but_revenues_do_not()
    {
        IReadOnlyList<ImportRowDto> rows = Analyze([], Row(2, "1000", null, "5100", "1"), Row(3, "1000", null, "4100", "1"));

        Assert.Equal("Expenditure lines need a department.", rows[0].Errors.Single());
        Assert.Equal(ImportRowAction.Add, rows[1].Action);
    }

    [Fact]
    public void Amounts_must_be_present_numeric_and_not_negative()
    {
        IReadOnlyList<ImportRowDto> rows = Analyze([],
            Row(2, "1000", "110", "5100", null),
            Row(3, "1000", "110", "5100", "ten"),
            Row(4, "1000", "110", "5100", "-5"),
            Row(5, "1000", "110", "5100", "5", prior: "abc"));

        Assert.Equal("Amount is missing.", rows[0].Errors.Single());
        Assert.Equal("Amount \"ten\" is not a number.", rows[1].Errors.Single());
        Assert.Equal("Amount cannot be negative.", rows[2].Errors.Single());
        Assert.Equal("Prior Year Actual \"abc\" is not a number.", rows[3].Errors.Single());
    }

    [Fact]
    public void A_row_collects_every_problem_it_has()
    {
        ImportRowDto row = Analyze([], Row(2, "9000", null, "5100", "x")).Single();

        Assert.Equal(3, row.Errors.Count); // inactive fund, expenditure without department, bad amount
    }

    [Fact]
    public void Duplicate_keys_within_the_file_flag_the_second_row()
    {
        IReadOnlyList<ImportRowDto> rows = Analyze([], Row(2, "1000", "FD", "5100", "1"), Row(3, "1000", "fd", "5100", "2"));

        Assert.Equal(ImportRowAction.Add, rows[0].Action);
        Assert.Contains("more than once", rows[1].Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_counts_and_can_commit_follow_from_the_rows()
    {
        var existing = new ExistingLine(Guid.NewGuid(), General.Id, Police.Id, Salaries.Id, 500m, 0m, 0m, null);
        IReadOnlyList<ImportRowDto> rows = Analyze([existing], Row(2, "1000", "110", "5100", "600"), Row(3, "1000", null, "4100", "70"));
        var preview = new ImportPreviewDto(Guid.NewGuid(), "lines.csv", rows, []);

        Assert.Equal((1, 1, 0, 0), (preview.AddCount, preview.UpdateCount, preview.UnchangedCount, preview.ErrorCount));
        Assert.True(preview.CanCommit);
        Assert.Equal(670m, preview.AmountToWrite);

        var withError = new ImportPreviewDto(Guid.NewGuid(), "lines.csv", Analyze([], Row(2, "1000", "110", "5100", "x")), []);
        Assert.False(withError.CanCommit);
        var nothingToDo = new ImportPreviewDto(Guid.NewGuid(), "lines.csv", Analyze([existing], Row(2, "1000", "110", "5100", "500")), []);
        Assert.False(nothingToDo.CanCommit);
    }
}
