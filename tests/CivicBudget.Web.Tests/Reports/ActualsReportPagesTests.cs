using Bunit.TestDoubles;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Web.Components.Admin.Reports;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Reports;

/// <summary>
/// The reports on the ERP's books show the pace on each bar and say plainly when there are no
/// actuals yet; the appropriation measure shows its columns and transfers out beside the departments.
/// </summary>
public class ActualsReportPagesTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly ReportHeaderDto Header = new(VersionId, "Village of Maple Ridge", 2026, "Amendment 1", BudgetStatus.Adopted, "2026-11", "Dana", DateTimeOffset.UtcNow);
    private readonly FakeReports _reports = new();

    public ActualsReportPagesTests()
    {
        Services.AddSingleton<IActualsReportService>(_reports);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Budget_vs_actual_marks_the_pace_and_a_line_over_its_budget()
    {
        BudgetActualRowDto salaries = new("1000-110-5110", "Salaries", 520_000m, 340_000m, 0m, 330_000m);
        BudgetActualRowDto cruiser = new("1000-110-5510", "Cruiser", 45_000m, 40_000m, 8_000m, null);
        BudgetActualRowDto subtotal = new("", "Total, 110 Police", 565_000m, 380_000m, 8_000m, 330_000m);
        _reports.BudgetVsActual = new BudgetActualReportDto(Header, new ActualsPeriodDto(2026, 8, new DateOnly(2026, 8, 31)),
            [new BudgetActualFundDto("1000", "General Fund", [new BudgetActualGroupDto("110 Police", [salaries, cruiser], subtotal)], subtotal with { Label = "Total, General Fund" })],
            subtotal with { Label = "All funds" });

        IRenderedComponent<BudgetVsActualReport> page = Render<BudgetVsActualReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("8 of 12 months (67% of the year)", page.Find(".cb-cert-doc").TextContent));
        Assert.Contains("over", page.FindAll(".cb-usedbar")[1].ClassList);                  // 48,000 of 45,000 spent or committed
        Assert.Equal("left: 66.7%", page.Find(".cb-usedbar-pace").GetAttribute("style"));
        Assert.Contains("-3,000.00", page.FindAll("td.cb-col-strong")[1].TextContent);
    }

    [Fact]
    public void Without_actuals_for_the_year_the_report_says_so()
    {
        _reports.BudgetVsActual = new BudgetActualReportDto(Header with { FiscalYear = 2027 }, null, [], new BudgetActualRowDto("", "All funds", 0m, 0m, 0m, null));

        IRenderedComponent<BudgetVsActualReport> page = Render<BudgetVsActualReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("No FY2027 actuals yet", page.Markup));
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void The_appropriation_measure_shows_personal_services_and_transfers_out()
    {
        MeasureRowDto police = new("110 Police", [620_000m], 15_000m);
        _reports.Measure = new AppropriationMeasureDto(Header, ["Personal services"],
            [new MeasureFundDto("1000", "General Fund", FundCategory.General, [police], 100_000m, police with { Label = "Total" })],
            police with { Label = "All departments" }, 100_000m, UsingDefaultColumns: true);

        IRenderedComponent<AppropriationMeasureReport> page = Render<AppropriationMeasureReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("Other financing uses: transfers out", page.Markup));
        Assert.Contains("categorized as personal services and fringe benefits", page.Find(".alert-info").TextContent);
        Assert.Contains("735,000.00", page.Find("tfoot").TextContent);                      // 620,000 + 15,000 + 100,000
    }

    private sealed class FakeReports : IActualsReportService
    {
        public BudgetActualReportDto? BudgetVsActual { get; set; }
        public AppropriationMeasureDto? Measure { get; set; }

        public Task<BudgetActualReportDto?> BudgetVsActualAsync(Guid budgetVersionId, CancellationToken ct = default) => Task.FromResult(BudgetVsActual);
        public Task<AppropriationMeasureDto?> AppropriationMeasureAsync(Guid budgetVersionId, CancellationToken ct = default) => Task.FromResult(Measure);
        public Task<RevenueReceiptReportDto?> RevenueVsReceiptsAsync(Guid budgetVersionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FundProjectionReportDto?> FundProjectionAsync(Guid budgetVersionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TrendReportDto?> TrendsAsync(Guid budgetVersionId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
