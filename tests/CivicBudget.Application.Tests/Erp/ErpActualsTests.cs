using System.Text;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Infrastructure.Erp;
using CivicBudget.Infrastructure.Export;

namespace CivicBudget.Application.Tests.Erp;

/// <summary>
/// Reading the ERP's actuals: the export file, matching its codes against the chart, and adding
/// the amounts up per budget line. All pure, so every rule is checked without a database.
/// </summary>
public class ErpActualsTests
{
    private static readonly AccountNumberFormat Uan = AccountNumberFormat.UanVillage;
    private readonly ErpActualsFileSource _file = new(new ClosedXmlSpreadsheetReader());

    // ---- the export file ------------------------------------------------------------------------

    [Fact]
    public void Reads_activity_encumbrances_and_cash_from_one_file()
    {
        Result<ErpActuals> result = _file.Read("fy2025.csv", Csv(
            "Type,Fiscal Year,Account,Period,Amount",
            "Actual,2025,1000-110-5110,1,\"43,210.55\"",
            "Actual,2025,1000-4110,3,\"$184,500.00\"",
            "actual,2025,1000.110.5110,2,(125.00)",       // a correcting entry; dotted numbers read too
            "Encumbrance,2025,1000-110-5310,,\"4,000\"",
            "Cash,2025,1000,,\"655,012.10\""), Uan);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        ErpActuals actuals = result.Value;
        Assert.Equal((2025, 3), (actuals.FiscalYear, actuals.ThroughPeriod));
        Assert.Equal(new ErpActivity("1000", "110", "5110", 1, 43_210.55m), actuals.Activity[0]);
        Assert.Equal(new ErpActivity("1000", null, "4110", 3, 184_500m), actuals.Activity[1]);   // fund-level revenue: two segments
        Assert.Equal(-125m, actuals.Activity[2].Amount);                                          // months can go negative
        Assert.Equal(new ErpOpenEncumbrance("1000", "110", "5310", 4_000m), actuals.Encumbrances.Single());
        Assert.Equal(new ErpCash("1000", 655_012.10m), actuals.Cash.Single());
    }

    [Fact]
    public void Names_every_bad_row_with_its_number()
    {
        Result<ErpActuals> result = _file.Read("bad.csv", Csv(
            "Fiscal Year,Type,Account,Period,Amount",
            "2025,Actual,1000-110-5110,13,10",
            "2025,Actual,Police overtime,1,10",
            "2025,Budget,1000-110-5110,1,10",
            "20x5,Actual,1000-110-5110,1,10",
            "2025,Actual,1000-110-5110,1,\"1,23\""), Uan);

        Assert.True(result.IsFailure);
        string[] messages = result.Errors.Select(e => e.Message).ToArray();
        Assert.Contains(messages, m => m.StartsWith("Row 2:", StringComparison.Ordinal) && m.Contains("Period", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("Row 3:", StringComparison.Ordinal) && m.Contains("full account number like 1000-110-5110", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("Row 4:", StringComparison.Ordinal) && m.Contains("Type must be", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("Row 5:", StringComparison.Ordinal) && m.Contains("Fiscal Year", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("Row 6:", StringComparison.Ordinal) && m.Contains("comma", StringComparison.Ordinal));
    }

    [Fact]
    public void Refuses_a_file_that_mixes_years_or_lacks_columns()
    {
        Result<ErpActuals> mixed = _file.Read("two.csv", Csv(
            "Fiscal Year,Type,Account,Period,Amount",
            "2025,Actual,1000-110-5110,1,10",
            "2026,Actual,1000-110-5110,1,10"), Uan);
        Result<ErpActuals> columns = _file.Read("cols.csv", Csv("Account,Amount", "1000-110-5110,10"), Uan);

        Assert.Contains("mixes fiscal years (2025, 2026)", mixed.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains("needs Fiscal Year, Type, Account", columns.Errors.Single().Message, StringComparison.Ordinal);
    }

    // ---- matching against the chart -------------------------------------------------------------

    private static readonly ChartCode General = new(Guid.NewGuid(), "1000", "General Fund");
    private static readonly ChartCode Police = new(Guid.NewGuid(), "110", "Police");
    private static readonly ChartCode Salaries = new(Guid.NewGuid(), "5110", "Salaries & Wages", AccountType.Expenditure);
    private static readonly ChartCode RealEstate = new(Guid.NewGuid(), "4110", "Real Estate Taxes", AccountType.Revenue);
    private static readonly ChartCode TransfersIn = new(Guid.NewGuid(), "4910", "Transfers In", AccountType.TransferIn);

    private static Result<MatchedActuals> Match(ErpActuals actuals) =>
        ActualsMatcher.Match(actuals, [General], [Police], [Salaries, RealEstate, TransfersIn]);

    [Fact]
    public void Matches_codes_padded_or_not_and_adds_repeated_rows_together()
    {
        Result<MatchedActuals> result = Match(new ErpActuals(2025, 2,
            [
                new("1000", "0110", "5110", 1, 40_000m),   // an ERP report padding the department to four digits
                new("1000", "110", "5110", 1, 2_500m),     // a second posting in the same month
                new("1000", "110", "5110", 2, 41_000m),
                new("1000", null, "4110", 2, 180_000m),
                new("1000", null, "4910", 2, 20_000m),
            ],
            [new("1000", "110", "5110", 1_000m)],
            [new("01000", 500_000m)]));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        MatchedActuals m = result.Value;
        Assert.Equal(42_500m, m.Activity.Single(a => a.AccountId == Salaries.Id && a.Period == 1).Amount);
        Assert.Equal(200_000m, m.Receipts);                       // revenue and transfers in
        Assert.Equal(83_500m, m.Disbursements);
        Assert.Equal(1_000m, m.Encumbered);
        Assert.Equal(500_000m, m.Cash[General.Id]);
        Assert.False(m.IsYearClosed);
    }

    [Fact]
    public void Refuses_the_whole_year_when_any_code_is_unknown_and_lists_each_once()
    {
        Result<MatchedActuals> result = Match(new ErpActuals(2025, 1,
            [
                new("1000", "999", "5110", 1, 10m),
                new("1000", "999", "5110", 1, 20m),
                new("2050", null, "4110", 1, 10m),
                new("1000", "110", "5999", 1, 10m),
            ],
            [], [new("7777", 1m)]));

        Assert.True(result.IsFailure);
        Assert.Equal(
            [
                "1000-110-5999: no account has the code 5999.",
                "1000-999-5110: no department or program has the code 999.",
                "2050-4110: no fund has the number 2050.",
                "Cash for fund 7777: no fund has that number.",
            ],
            result.Errors.Select(e => e.Message));
    }

    [Fact]
    public void Refuses_a_year_with_no_activity()
    {
        Result<MatchedActuals> result = Match(new ErpActuals(2027, 0, [], [], []));

        Assert.Contains("no activity yet", result.Errors.Single().Message, StringComparison.Ordinal);
    }

    // ---- per budget line ------------------------------------------------------------------------

    [Fact]
    public void A_fund_level_line_collects_department_receipts_that_no_department_line_claims()
    {
        Guid fund = Guid.NewGuid(), parks = Guid.NewGuid(), water = Guid.NewGuid(), fees = Guid.NewGuid(), salaries = Guid.NewGuid();
        var fundLevelFees = new LineKey(fund, null, fees);
        var parksFees = new LineKey(fund, parks, fees);
        var parksSalaries = new LineKey(fund, parks, salaries);

        Dictionary<LineKey, decimal> totals = ActualsByLine.Sum(
            [fundLevelFees, parksFees, parksSalaries],
            [
                (parksFees, 100m),                              // Parks has its own fees line: stays there
                (new LineKey(fund, water, fees), 250m),         // Water does not: goes to the fund-level line
                (new LineKey(fund, null, fees), 50m),
                (parksSalaries, 900m),
                (new LineKey(fund, water, salaries), 700m),     // no line anywhere for this: ignored, never guessed
            ]);

        Assert.Equal(300m, totals[fundLevelFees]);
        Assert.Equal(100m, totals[parksFees]);
        Assert.Equal(900m, totals[parksSalaries]);
    }

    // ---- the simulated ERP ----------------------------------------------------------------------

    private static readonly ErpEntity MapleRidge = new(Guid.NewGuid(), "maple-ridge-oh", "Village of Maple Ridge", 1, Uan);
    private static readonly ErpEntity PineHollow = new(Guid.NewGuid(), "pine-hollow-twp-oh", "Pine Hollow Township", 7, new AccountNumberFormat(4, 3, 4, ".", "Department"));

    [Fact]
    public async Task A_month_counts_once_its_books_close_ten_days_after_it_ends()
    {
        var vip = new SimulatedErpActualsApi(new FixedClock(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(7, (await vip.FetchAsync(MapleRidge, 2026)).Value.ThroughPeriod);           // August closes on the 10th

        var later = new SimulatedErpActualsApi(new FixedClock(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(8, (await later.FetchAsync(MapleRidge, 2026)).Value.ThroughPeriod);
        Assert.Equal(2, (await later.FetchAsync(PineHollow, 2027)).Value.ThroughPeriod);         // July start: July and August
        Assert.Contains("not closed a month of FY2027", (await later.FetchAsync(MapleRidge, 2027)).Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_closed_year_is_twelve_months_that_add_up_and_cash_agrees_with_next_years_balance()
    {
        var vip = new SimulatedErpActualsApi(new FixedClock(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero)));

        ErpActuals fy2025 = (await vip.FetchAsync(MapleRidge, 2025)).Value;

        Assert.Equal(12, fy2025.ThroughPeriod);
        Assert.All(fy2025.Activity.GroupBy(a => (a.FundCode, a.DepartmentCode, a.ObjectCode)), g => Assert.Equal(12, g.Count()));
        // Real estate taxes arrive in the two settlements, nowhere else.
        Assert.Equal([3, 4, 8, 9], fy2025.Activity.Where(a => a.ObjectCode == "4110" && a.Amount != 0m).Select(a => a.Period).Order());
        // Ohio's unencumbered balance: year-end cash less carried encumbrances is the next budget's beginning balance.
        decimal generalCash = fy2025.Cash.Single(c => c.FundCode == "1000").Amount;
        decimal generalEncumbered = fy2025.Encumbrances.Where(e => e.FundCode == "1000").Sum(e => e.Amount);
        Assert.Equal(620_000m, generalCash - generalEncumbered);
    }

    [Fact]
    public async Task Knows_only_the_demo_governments_and_their_years()
    {
        var vip = new SimulatedErpActualsApi(TimeProvider.System);

        Assert.Contains("no entity set up for Elsewhere", (await vip.FetchAsync(MapleRidge with { Slug = "elsewhere", Name = "Elsewhere" }, 2025)).Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains("no books for FY2019", (await vip.FetchAsync(MapleRidge, 2019)).Errors.Single().Message, StringComparison.Ordinal);
    }

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
