using Bunit.TestDoubles;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Erp;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;
using ChartSyncPage = CivicBudget.Web.Components.Admin.Chart.ChartSync;

namespace CivicBudget.Web.Tests.Chart;

/// <summary>
/// The chart sync page offers the ERP's API only where the government is connected, and a fetched
/// chart goes through the same preview and confirmation as an uploaded one.
/// </summary>
public class ChartSyncPageTests : BunitContext
{
    private readonly FakeSync _sync = new();

    public ChartSyncPageTests()
    {
        Services.AddSingleton<IChartSyncService>(_sync);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Without_a_connection_only_the_upload_is_offered()
    {
        _sync.ApiName = null;

        IRenderedComponent<ChartSyncPage> page = Render<ChartSyncPage>();

        page.WaitForAssertion(() => Assert.Contains("ERP chart export", page.Markup));
        Assert.DoesNotContain("Fetch from", page.Markup);
    }

    [Fact]
    public void A_fetched_chart_is_previewed_then_applied_only_when_confirmed()
    {
        IRenderedComponent<ChartSyncPage> page = Render<ChartSyncPage>();
        page.WaitForAssertion(() => Assert.Contains("Fetch from ERP", page.Markup));

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Fetch").Click();

        Assert.True(_sync.Fetched);
        Assert.Contains("Fetched by API", page.Markup);
        Assert.Contains("Fire Levy", page.Find("table[aria-label='Chart changes']").TextContent);
        Assert.False(_sync.Committed);

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Apply sync").Click();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        Assert.True(_sync.Committed);
    }

    private sealed class FakeSync : IChartSyncService
    {
        public string? ApiName { get; set; } = "ERP";
        public bool Fetched { get; private set; }
        public bool Committed { get; private set; }

        private static readonly ChartSyncPreviewDto Preview = new("ERP", "Fetched by API",
            [new ChartChange("Fund", "2050", ChartChangeKind.Add, null, "Fire Levy"), new ChartChange("Fund", "1000", ChartChangeKind.Unchanged, "General Fund", "General Fund")], null);

        public Task<ChartSourceStatusDto> StatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new ChartSourceStatusDto(ChartSource.Local, null, null, null, ApiName));

        public Task<Result<ChartSyncPreviewDto>> PreviewFromErpAsync(CancellationToken ct = default)
        {
            Fetched = true;
            return Task.FromResult(Result.Success(Preview));
        }

        public Task<Result<ChartSyncDto>> CommitFromErpAsync(CancellationToken ct = default)
        {
            Committed = true;
            return Task.FromResult(Result.Success(new ChartSyncDto(Guid.CreateVersion7(), "ERP", "Fetched by API", DateTimeOffset.UnixEpoch, "Dana", 1, 0, 0, 0, 1, Preview.Changes)));
        }

        public Task<Result<ChartSyncPreviewDto>> PreviewAsync(string fileName, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<ChartSyncDto>> CommitAsync(string fileName, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChartSyncDto>> HistoryAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChartSyncDto>>([]);
        public Task<Result> SetChartSourceAsync(ChartSource source, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
