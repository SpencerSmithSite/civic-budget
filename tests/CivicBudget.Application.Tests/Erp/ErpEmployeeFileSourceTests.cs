using System.Text;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Personnel;
using CivicBudget.Infrastructure.Export;

namespace CivicBudget.Application.Tests.Erp;

/// <summary>The payroll export: one row per employee, several values to a cell where a register prints them that way.</summary>
public class ErpEmployeeFileSourceTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);
    private readonly ErpEmployeeFileSource _source = new(new ClosedXmlSpreadsheetReader());

    private Result<ErpEmployees> Read(Stream file) => _source.Read("payroll.csv", file, Today);

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\n", lines)));

    private const string Header = "Employee ID,Name,Title,Department,Pay Type,Rate,Annual Hours,Hire Date,Grade,Step,Retirement,Pick Up,Funds,Benefits";

    [Fact]
    public void Reads_pay_plans_and_funds_for_each_employee()
    {
        Result<ErpEmployees> result = Read(Csv(
            Header,
            "E1033,Casey Lin,Payroll clerk,725,Hourly,$23.60,1560,2019-06-10,,,OPERS,No,1000,",
            "E1011,Sam Okafor,Service Director,620,Salary,\"71,500.00\",,5/3/2010,,,OPERS,,\"1000 50%; 2011 50%\",\"Medical (PPO): Family; Life ($25,000): Single\"",
            "E1017,Riley Chen,Patrol officer,110,,,,2013-06-03,PO,5,OP&F police,yes,1000=100,Dental: Employee + Spouse"));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        IReadOnlyList<ErpEmployee> employees = result.Value.Employees;
        Assert.Equal(Today, result.Value.AsOf);
        Assert.Equal((PayBasis.Hourly, 23.60m, 1_560m), (employees[0].Basis, employees[0].Rate, employees[0].AnnualHours!.Value));
        Assert.Equal(new DateOnly(2010, 5, 3), employees[1].HireDate);
        Assert.Equal([new ErpFundShare("1000", 50m), new ErpFundShare("2011", 50m)], employees[1].Funds);
        Assert.Equal([new ErpBenefit("Medical (PPO)", CoverageTier.Family), new ErpBenefit("Life ($25,000)", CoverageTier.EmployeeOnly)], employees[1].Benefits);
        Assert.Equal(("PO", 5, "OP&F police", true), (employees[2].Grade, employees[2].Step!.Value, employees[2].Retirement, employees[2].PicksUpEmployeeShare));
        Assert.Equal([new ErpFundShare("1000", 100m)], employees[2].Funds);
    }

    [Fact]
    public void Reports_every_bad_row_at_once()
    {
        Result<ErpEmployees> result = Read(Csv(
            Header,
            "E1,Ann,Clerk,725,Weekly,20,,2019-06-10,,,,,1000,",
            "E2,Bo,Clerk,725,Hourly,,,June 10,,,,,1000 half,",
            "E2,Cy,Clerk,725,Hourly,20,,,,,,,1000,Medical: Everyone"));

        Assert.True(result.IsFailure);
        List<string> messages = result.Errors.Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.StartsWith("Row 2 (E1): Pay Type must be Salary or Hourly", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("Row 3 (E2): Rate", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Hire Date \"June 10\"", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("\"1000 half\"", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Employee E2 appears more than once", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("\"Medical: Everyone\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Names_the_columns_it_needs()
    {
        Result<ErpEmployees> result = Read(Csv("Name,Salary", "Ann,50000"));

        Assert.StartsWith("The file needs Employee ID, Name, Title, Department", result.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("single", CoverageTier.EmployeeOnly)]
    [InlineData("Employee Only", CoverageTier.EmployeeOnly)]
    [InlineData("Employee & Spouse", CoverageTier.EmployeeSpouse)]
    [InlineData("employee and spouse", CoverageTier.EmployeeSpouse)]
    [InlineData("FAMILY", CoverageTier.Family)]
    public void Reads_coverage_tiers_the_ways_payrolls_write_them(string text, CoverageTier expected)
    {
        Assert.True(ErpEmployeeFileSource.TryTier(text, out CoverageTier tier));
        Assert.Equal(expected, tier);
    }
}
