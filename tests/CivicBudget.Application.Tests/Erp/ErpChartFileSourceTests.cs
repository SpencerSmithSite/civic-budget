using System.Text;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Infrastructure.Export;

namespace CivicBudget.Application.Tests.Erp;

/// <summary>The stand-in for the ERP's export: forgiving about spelling and order, strict about what a code needs.</summary>
public class ErpChartFileSourceTests
{
    private readonly ErpChartFileSource _source = new(new ClosedXmlSpreadsheetReader());

    [Fact]
    public void Reads_funds_departments_and_objects_from_one_file_in_any_column_order()
    {
        Result<ErpChart> result = _source.Read("chart.csv", Csv(
            "Name,Kind,Code,Type,Category,Description,Active",
            "General Fund,Fund,1000,,General,Day to day,Yes",
            "Street Fund,fund,2011,,Special Revenue,,",
            "Police,Program,110,,,,",
            "Old Board,Department,900,,,,No",
            "Salaries & Wages,Object,5110,Expenditure,Personal Services,,Yes",
            "Real Estate Taxes,Account,4110,Revenue,taxes,,true"));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        ErpChart chart = result.Value;
        Assert.Equal([("1000", FundCategory.General, true), ("2011", FundCategory.SpecialRevenue, true)], chart.Funds.Select(f => (f.Code, f.Category, f.IsActive)));
        Assert.Equal([("110", true), ("900", false)], chart.Departments.Select(d => (d.Code, d.IsActive)));
        Assert.Equal([("5110", AccountType.Expenditure, ReportingCategory.PersonalServices), ("4110", AccountType.Revenue, ReportingCategory.Taxes)],
            chart.Objects.Select(o => (o.Code, o.Type, o.Category)));
        Assert.Equal("Day to day", chart.Funds[0].Description);
        Assert.Null(chart.NumberFormat);
    }

    [Fact]
    public void Reports_every_bad_row_at_once_with_its_row_number()
    {
        Result<ErpChart> result = _source.Read("chart.csv", Csv(
            "Kind,Code,Name,Type,Category",
            "Fund,1000,General Fund,,NotACategory",
            "Object,5110,Salaries,Expenditure,Taxes",
            "Object,4110,Taxes,,Taxes",
            "Widget,1,x,,",
            "Fund,,No code,,General"));

        Assert.True(result.IsFailure);
        Assert.Equal(5, result.Errors.Count);
        Assert.Contains("Row 2", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("valid for Expenditure", result.Errors[1].Message, StringComparison.Ordinal); // revenue category on an expenditure object
        Assert.Contains("needs a Type", result.Errors[2].Message, StringComparison.Ordinal);
        Assert.Contains("Kind must be", result.Errors[3].Message, StringComparison.Ordinal);
        Assert.Contains("Code and Name", result.Errors[4].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_listed_twice_names_both_rows()
    {
        Result<ErpChart> result = _source.Read("chart.csv", Csv(
            "Kind,Code,Name,Category",
            "Fund,1000,General Fund,General",
            "Fund,1000,General Fund again,General",
            "Department,110,Police,",
            "Program,110,Police again,"));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Message == "Row 3: fund 1000 is listed twice (first on row 2).");
        Assert.Contains(result.Errors, e => e.Message == "Row 5: department 110 is listed twice (first on row 4).");
    }

    [Fact]
    public void Rejects_files_that_are_not_a_chart_export()
    {
        Assert.Contains("Kind, Code, and Name", _source.Read("x.csv", Csv("Fund,Amount", "1000,5")).Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains(".csv or .xlsx", _source.Read("x.pdf", Csv("Kind")).Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains("no chart rows", _source.Read("x.csv", Csv("Kind,Code,Name")).Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_same_layout_from_xlsx()
    {
        byte[] xlsx = new ClosedXmlSpreadsheetExporter().ToXlsx(new ExportTable("Chart", ["Kind", "Code", "Name", "Type", "Category"],
            [["Fund", "1000", "General Fund", null, "General"], ["Object", "5110", "Salaries", "Expenditure", "PersonalServices"]]));

        Result<ErpChart> result = _source.Read("chart.xlsx", new MemoryStream(xlsx));

        Assert.True(result.IsSuccess);
        Assert.Equal("1000", result.Value.Funds.Single().Code);
        Assert.Equal("5110", result.Value.Objects.Single().Code);
    }

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));
}
