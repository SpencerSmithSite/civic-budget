using Bunit.TestDoubles;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Web.Components.Admin.Chart;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Chart;

/// <summary>
/// The actuals page offers the ERP's API only where one is connected, previews before it writes,
/// and says which prior-year actuals a sync would change.
/// </summary>
public class ActualsSyncPageTests : BunitContext
{
    private readonly FakeActualsSync _sync = new();

    public ActualsSyncPageTests()
    {
        Services.AddSingleton<IActualsSyncService>(_sync);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Without_a_connection_only_the_upload_is_offered()
    {
        _sync.ApiName = null;

        IRenderedComponent<ActualsSync> page = Render<ActualsSync>();

        page.WaitForAssertion(() => Assert.Contains("Upload an export", page.Markup));
        Assert.DoesNotContain("Fetch from", page.Markup);
        Assert.Contains("None yet", page.Find(".cb-kpis").TextContent);
    }

    [Fact]
    public async Task Fetching_previews_the_year_and_the_prior_year_actuals_it_would_change()
    {
        IRenderedComponent<ActualsSync> page = Render<ActualsSync>();
        page.WaitForAssertion(() => Assert.Contains("Fetch from ERP (simulated)", page.Markup));
        Assert.Contains("To Aug 31, 2026", page.Find(".cb-kpis").TextContent);           // what is here already, before anything else

        page.Find("#fetchYear").Change("2025");                                              // the newest year is picked by default
        await page.Find(".cb-sync-sources .btn-primary").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Equal(2025, _sync.FetchedYear);
        Assert.Contains("Full year", page.Find(".cb-toolbar .cb-pill").TextContent);
        Assert.Contains("Prior-year actuals this sync updates (1)", page.Markup);
        Assert.Contains("1000-110-5420", page.Markup);
        Assert.Null(_sync.CommittedYear);                                                     // nothing written until Apply is confirmed
    }

    private sealed class FakeActualsSync : IActualsSyncService
    {
        public string? ApiName { get; set; } = "ERP (simulated)";
        public int? FetchedYear { get; private set; }
        public int? CommittedYear { get; private set; }

        public Task<ActualsStatusDto> StatusAsync(CancellationToken ct = default) => Task.FromResult(new ActualsStatusDto(
            ApiName, ApiName is null ? [] : [2026, 2025],
            ApiName is null ? [] : [new ActualsYearDto(2026, 8, new DateOnly(2026, 8, 31), DateTimeOffset.UtcNow, "system", "ERP (simulated)")]));

        public Task<Result<ActualsPreviewDto>> PreviewFromErpAsync(int fiscalYear, CancellationToken ct = default)
        {
            FetchedYear = fiscalYear;
            return Task.FromResult(Result.Success(new ActualsPreviewDto(
                "ERP (simulated)", null, fiscalYear, 12, new DateOnly(fiscalYear, 12, 31), 1_200, 5_000_000m, 4_700_000m, 81_000m, 1_900_000m,
                [new ActualsFundDto("1000", "General Fund", 2_000_000m, 1_900_000m, 40_000m, 700_000m)],
                [new PriorActualChangeDto("FY2027 Original", "1000-110-5420", "Fuel", 1m, 27_310m)],
                [], null)));
        }

        public Task<Result<ActualsSyncDto>> CommitFromErpAsync(int fiscalYear, CancellationToken ct = default)
        {
            CommittedYear = fiscalYear;
            return Task.FromResult(Result.Failure<ActualsSyncDto>("not in this test"));
        }

        public Task<IReadOnlyList<ActualsSyncDto>> HistoryAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ActualsSyncDto>>([]);
        public Task<Result<ActualsPreviewDto>> PreviewFileAsync(string fileName, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<ActualsSyncDto>> CommitFileAsync(string fileName, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
