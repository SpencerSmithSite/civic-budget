using Bunit.TestDoubles;
using CivicBudget.Application.Common;
using CivicBudget.Application.Personnel;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Personnel;
using CivicBudget.Web.Components.Admin.Personnel;
using CivicBudget.Web.Components.Admin.Reports;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Personnel;

/// <summary>
/// The employee sync page shows each employee's step and holds Apply back while anything is
/// unmatched; the personnel reports show the roster's vacancies and the benefits' split.
/// </summary>
public class PersonnelSyncAndReportsTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly ReportHeaderDto Header = new(VersionId, "Village of Maple Ridge", 2027, "Original", BudgetStatus.Draft, null, "Dana", DateTimeOffset.UtcNow);
    private readonly FakeSync _sync = new();
    private readonly FakeReports _reports = new();

    public PersonnelSyncAndReportsTests()
    {
        Services.AddSingleton<IPersonnelSyncService>(_sync);
        Services.AddSingleton<IPersonnelReportService>(_reports);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PersonnelSyncPreviewDto Preview(params string[] errors) => new(
        VersionId, "FY2027 Original", "ERP (simulated)", null, new DateOnly(2026, 9, 27), 14,
        [
            new SyncStepDto(SyncAction.Fill, "110", "Police", "Jamie Ortiz, Patrol officer (E1047)", ["Fills the vacant Patrol officer position"], 60_000m, 80_000m),
            new SyncStepDto(SyncAction.Update, "725", "Finance", "Casey Lin, Clerk (E1033)", ["Pay: $23.10 an hour → $23.60 an hour"], 40_000m, 40_900m),
            new SyncStepDto(SyncAction.Vacate, "620", "Streets & Service", "Rowan Ruiz, Maintenance worker (E1026)", ["Position left vacant: no longer on the ERP's payroll"], 70_000m, 70_000m),
            new SyncStepDto(SyncAction.Unchanged, "110", "Police", "Riley Chen, Patrol officer (E1017)", [], 90_000m, 90_000m),
        ],
        [new SyncLineChangeDto("1000-110-5110", "Salaries & Wages", 578_966.80m, 597_000m)],
        errors);

    [Fact]
    public void The_preview_lists_each_employees_step_and_the_lines_it_moves()
    {
        _sync.Preview = Preview();
        IRenderedComponent<PersonnelSync> page = Render<PersonnelSync>();
        page.WaitForAssertion(() => page.Find("button.btn-primary.btn-sm"));

        page.Find("button.btn-primary.btn-sm").Click();

        page.WaitForAssertion(() => Assert.Contains("Fills a vacancy", page.Markup));
        Assert.Contains("Pay: $23.10 an hour → $23.60 an hour", page.Markup);
        Assert.Contains("Left vacant", page.Markup);
        Assert.DoesNotContain("Riley Chen", page.Find("table[aria-label='What happens to each position']").TextContent); // unchanged rows are counted, not listed
        Assert.Contains("+18,033.20", page.Find("table[aria-label='Budget lines this changes']").TextContent);
        Assert.False(page.FindAll("button").Single(b => b.TextContent.Contains("Apply", StringComparison.Ordinal)).HasAttribute("disabled"));
    }

    [Fact]
    public void An_unmatched_employee_holds_apply_back_and_says_how_to_fix_it()
    {
        _sync.Preview = Preview("Morgan Fields, Clerk (E9001): FY2027's settings have no insurance plan named \"Vision\".");
        IRenderedComponent<PersonnelSync> page = Render<PersonnelSync>();
        page.WaitForAssertion(() => page.Find("button.btn-primary.btn-sm"));

        page.Find("button.btn-primary.btn-sm").Click();

        page.WaitForAssertion(() => Assert.Contains("cannot be matched, so nothing can be applied yet", page.Markup));
        Assert.True(page.FindAll("button").Single(b => b.TextContent.Contains("Apply", StringComparison.Ordinal)).HasAttribute("disabled"));
    }

    [Fact]
    public void A_budget_year_without_settings_points_to_setting_it_up()
    {
        _sync.Versions = [new SyncableVersionDto(VersionId, 2027, "Original", HasSettings: false)];

        IRenderedComponent<PersonnelSync> page = Render<PersonnelSync>();

        page.WaitForAssertion(() => Assert.Contains("Set up FY2027", page.Markup));
        Assert.True(page.Find("button.btn-primary.btn-sm").HasAttribute("disabled"));
    }

    [Fact]
    public void The_roster_marks_vacancies_and_totals_each_department()
    {
        _reports.Roster = new PositionRosterDto(Header,
        [
            new RosterDepartmentDto("110", "Police",
            [
                new RosterRowDto("Chief of Police", "Morgan Hale", "E1001", new DateOnly(2006, 4, 17), 20, "$88,500.00 a year", "All year", "1000", 93_000m, 52_000m),
                new RosterRowDto("Patrol officer", null, null, null, null, "PO step 1 · $25.10 an hour", "Apr–Dec", "1000", 40_000m, 20_000m),
            ]),
        ], HasSettings: true);

        IRenderedComponent<PositionRosterReport> page = Render<PositionRosterReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("2 positions, 1 vacant.", page.Markup));
        Assert.Contains("Vacant", page.Find("tbody").TextContent);
        Assert.Contains("205,000.00", page.Find("tfoot").TextContent);
        Assert.Contains("20 yr", page.Markup);
    }

    [Fact]
    public void The_benefits_summary_splits_premiums_between_employer_and_employees()
    {
        _reports.Benefits = new BenefitsSummaryDto(Header, 9,
            [new RetirementSummaryDto("OP&F police", 19.5m, 8, 600_000m, 117_000m, 11_000m)],
            [new InsuranceSummaryDto("Medical (PPO)", 15m, [new BenefitTierDto(CoverageTier.Family, 3, 1_760m, 53_856m, 9_504m)])],
            1.45m, 640_000m, 9_280m, 2.1m, 13_440m, HasSettings: true);

        IRenderedComponent<BenefitsSummaryReport> page = Render<BenefitsSummaryReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("OP&F police", page.Find("table[aria-label='Retirement systems']").TextContent));
        Assert.Contains("128,000.00", page.Find("table[aria-label='Retirement systems']").TextContent);
        Assert.Contains("9,504.00", page.Find("table[aria-label='Insurance enrollment']").TextContent);
        Assert.Contains("204,576.00", page.Find("table[aria-label='Payroll taxes'] tfoot").TextContent);
    }

    private sealed class FakeSync : IPersonnelSyncService
    {
        public PersonnelSyncPreviewDto? Preview { get; set; }
        public IReadOnlyList<SyncableVersionDto> Versions { get; set; } = [new SyncableVersionDto(VersionId, 2027, "Original", HasSettings: true)];

        public Task<PersonnelSyncStatusDto> StatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new PersonnelSyncStatusDto("ERP (simulated)", Versions, []));

        public Task<Result<PersonnelSyncPreviewDto>> PreviewFromErpAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Result.Success(Preview!));

        public Task<Result<PersonnelSyncPreviewDto>> PreviewFileAsync(Guid versionId, string fileName, Stream content, CancellationToken ct = default) => Task.FromResult(Result.Success(Preview!));

        public Task<Result<PersonnelSyncDto>> CommitFromErpAsync(Guid versionId, CancellationToken ct = default) =>
            Task.FromResult(Result.Failure<PersonnelSyncDto>("Not in this test."));

        public Task<Result<PersonnelSyncDto>> CommitFileAsync(Guid versionId, string fileName, Stream content, CancellationToken ct = default) =>
            Task.FromResult(Result.Failure<PersonnelSyncDto>("Not in this test."));
    }

    private sealed class FakeReports : IPersonnelReportService
    {
        public PositionRosterDto? Roster { get; set; }
        public BenefitsSummaryDto? Benefits { get; set; }

        public Task<PositionRosterDto?> RosterAsync(Guid budgetVersionId, CancellationToken ct = default) => Task.FromResult(Roster);
        public Task<PersonnelCostDto?> CostByFundAsync(Guid budgetVersionId, CancellationToken ct = default) => Task.FromResult<PersonnelCostDto?>(null);
        public Task<BenefitsSummaryDto?> BenefitsAsync(Guid budgetVersionId, CancellationToken ct = default) => Task.FromResult(Benefits);
    }
}
