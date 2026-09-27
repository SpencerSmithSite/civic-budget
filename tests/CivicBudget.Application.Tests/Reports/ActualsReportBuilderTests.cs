using CivicBudget.Application.Erp;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Tests.Reports;

/// <summary>
/// The reports that put the ERP's books beside the budget: the seasonal projection, budget against
/// actual, revenue against last year's pace, projected fund balances, and the appropriation measure.
/// </summary>
public class ActualsReportBuilderTests
{
    private static readonly ReportHeaderDto Header = new(Guid.NewGuid(), "Village of Maple Ridge", 2026, "Amendment 1", BudgetStatus.Adopted, "2026-11", "Dana", DateTimeOffset.UtcNow);
    private static readonly ActualsPeriodDto August = new(2026, 8, new DateOnly(2026, 8, 31));

    private static ActualsLine Line(string number, AccountType type, decimal budget, decimal actual, decimal encumbered = 0m, decimal? priorAt = null, decimal? priorFull = null,
        string fund = "1000", string? department = "110") =>
        new(new LineKey(Guid.NewGuid(), null, Guid.NewGuid()), number, "Account " + number, fund, fund == "1000" ? "General Fund" : "Street", department,
            department is null ? null : "Police", type, budget, actual, encumbered, priorAt, priorFull);

    [Fact]
    public void A_seasonal_line_is_projected_from_last_years_pattern_not_a_straight_line()
    {
        // Real estate taxes: both settlements are in by August. Last year 95% had arrived by now.
        ActualsLine taxes = Line("1000-4110", AccountType.Revenue, 422_300m, 405_000m, priorAt: 380_000m, priorFull: 400_000m, department: null);

        (decimal projected, bool fromBudget) = ActualsReportBuilder.Project(taxes, August);

        Assert.Equal(426_315.79m, projected);                    // 405,000 × 400,000 / 380,000, not 405,000 × 12 / 8
        Assert.False(fromBudget);
        Assert.Equal((405_000m, false), ActualsReportBuilder.Project(taxes, August with { ThroughPeriod = 12 }));        // a closed year is its actual
        Assert.Equal((422_300m, true), ActualsReportBuilder.Project(taxes with { PriorAtPeriod = null }, August));      // no history: the budget
        Assert.Equal((450_000m, true), ActualsReportBuilder.Project(taxes with { PriorFull = null, Actual = 450_000m }, August)); // never less than what happened
    }

    [Fact]
    public void Budget_against_actual_groups_by_fund_and_department_and_flags_a_line_over_its_budget()
    {
        BudgetActualReportDto report = ActualsReportBuilder.BudgetVsActual(Header, August,
        [
            Line("1000-110-5110", AccountType.Expenditure, 520_000m, 340_000m, priorAt: 330_000m),
            Line("1000-110-5510", AccountType.Expenditure, 45_000m, 40_000m, encumbered: 8_000m),
            Line("1000-5910", AccountType.TransferOut, 100_000m, 100_000m, department: null),
            Line("1000-4110", AccountType.Revenue, 422_300m, 405_000m, department: null),   // revenue is another report
        ]);

        BudgetActualFundDto general = report.Funds.Single();
        Assert.Equal(["110 Police", "Other financing uses"], general.Departments.Select(d => d.Label));
        BudgetActualRowDto cruiser = general.Departments[0].Lines.Single(l => l.AccountNumber == "1000-110-5510");
        Assert.Equal((-3_000m, true), (cruiser.Remaining, cruiser.IsOver));
        Assert.Equal(0.654m, Math.Round(general.Departments[0].Lines[0].Used!.Value, 3));
        Assert.Equal((665_000m, 480_000m, 8_000m), (report.Total.Budget, report.Total.Actual, report.Total.Encumbered));
        Assert.Equal(330_000m, general.Departments[0].Subtotal.LastYearAtThisPoint);
    }

    [Fact]
    public void Revenue_is_behind_only_when_it_trails_last_years_pace_by_more_than_ten_points()
    {
        RevenueReceiptReportDto report = ActualsReportBuilder.RevenueVsReceipts(Header, August,
        [
            Line("1000-4110", AccountType.Revenue, 400_000m, 380_000m, priorAt: 380_000m, priorFull: 400_000m, department: null),  // 95% in, 95% normal
            Line("1000-4130", AccountType.Revenue, 1_200_000m, 600_000m, priorAt: 800_000m, priorFull: 1_200_000m, department: null), // 50% in, 67% normal
            Line("1000-4610", AccountType.Revenue, 10_000m, 1_000m, department: null),                                          // no history: never "behind"
        ]);

        RevenueReceiptRowDto[] rows = [.. report.Funds.Single().Lines];
        Assert.Equal([false, true, false], rows.Select(r => r.IsBehind));
        Assert.Equal(0.95m, rows[0].NormallyByNow);
        Assert.Null(rows[2].NormallyByNow);
        Assert.Equal((1_610_000m, 629_000m), (report.Total.Estimate, report.Total.StillToCollect));
    }

    [Fact]
    public void A_fund_projection_counts_committed_money_as_spent_and_says_how_many_lines_had_no_history()
    {
        FundProjectionReportDto report = ActualsReportBuilder.FundProjection(Header, August,
        [
            Line("1000-4110", AccountType.Revenue, 400_000m, 380_000m, priorAt: 380_000m, priorFull: 400_000m, department: null),
            Line("1000-110-5110", AccountType.Expenditure, 520_000m, 346_000m, priorAt: 346_000m, priorFull: 519_000m),
            Line("1000-110-5510", AccountType.Expenditure, 45_000m, 10_000m, encumbered: 40_000m),                          // no history, but 50,000 committed
        ],
        new Dictionary<string, decimal> { ["1000"] = 620_000m }, [("1000", "General Fund")]);

        FundProjectionRowDto general = report.Funds.Single();
        Assert.Equal(400_000m, general.ProjectedReceipts);
        Assert.Equal(519_000m + 50_000m, general.ProjectedSpending);
        Assert.Equal(620_000m + 400_000m - 565_000m, general.BudgetedEnding);
        Assert.Equal(620_000m + 400_000m - 569_000m, general.ProjectedEnding);
        Assert.Equal(1, report.LinesProjectedFromBudget);
    }

    [Fact]
    public void The_appropriation_measure_sets_out_personal_services_by_department_with_transfers_beside_them()
    {
        Guid salaries = Guid.NewGuid(), pension = Guid.NewGuid(), supplies = Guid.NewGuid(), transfer = Guid.NewGuid();
        AppropriationMeasureDto measure = AppropriationMeasureBuilder.Build(Header, [("Personal services", [salaries, pension])], usingDefault: true,
        [
            ("1000", "General Fund", FundCategory.General, "110", "Police", salaries, AccountType.Expenditure, 520_000m),
            ("1000", "General Fund", FundCategory.General, "110", "Police", pension, AccountType.Expenditure, 100_000m),
            ("1000", "General Fund", FundCategory.General, "110", "Police", supplies, AccountType.Expenditure, 15_000m),
            ("1000", "General Fund", FundCategory.General, "620", "Streets", supplies, AccountType.Expenditure, 9_000m),
            ("1000", "General Fund", FundCategory.General, null, null, transfer, AccountType.TransferOut, 100_000m),
            ("1000", "General Fund", FundCategory.General, null, null, Guid.NewGuid(), AccountType.Revenue, 410_000m),       // not an appropriation
        ]);

        MeasureFundDto general = measure.Funds.Single();
        Assert.Equal([("110 Police", 620_000m, 15_000m), ("620 Streets", 0m, 9_000m)], general.Departments.Select(d => (d.Label, d.Columns[0], d.Other)));
        Assert.Equal((100_000m, 744_000m), (general.TransfersOut, general.Total));
        Assert.Equal(744_000m, measure.Total);
    }
}
