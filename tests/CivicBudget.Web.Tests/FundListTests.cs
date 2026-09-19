using CivicBudget.Application.Common;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Funds;
using CivicBudget.Web.Components.Admin.Funds;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests;

/// <summary>
/// bUnit renders the real component against a fake service. The page is Interactive Server in the
/// app; here it is just a component tree, which is the point of keeping data access behind interfaces.
/// </summary>
public class FundListTests : BunitContext
{
    [Fact]
    public void Lists_funds_and_filters_by_search_text()
    {
        Services.AddSingleton<IFundService>(new FakeFundService());
        Services.AddSingleton<CivicBudget.Application.Erp.IChartSyncService>(new Chart.FakeChartSyncService());
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("finance");
        // QuickGrid loads a small JS module for column resizing; bUnit has no browser, so stub it.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/Microsoft.AspNetCore.Components.QuickGrid/QuickGrid.razor.js");

        IRenderedComponent<FundList> page = Render<FundList>();

        // A paginated QuickGrid pads the page with empty placeholder rows, so count rows with content.
        page.WaitForAssertion(() => Assert.Equal(2, PopulatedRows(page)));
        Assert.Contains("Street Construction", page.Markup);

        page.Find("input[placeholder='Search code or name']").Input("2011");
        page.WaitForAssertion(() => Assert.Equal(1, PopulatedRows(page)));
        Assert.DoesNotContain("General Fund", page.Markup);
    }

    [Fact]
    public void When_the_erp_owns_the_chart_the_list_is_read_only_with_a_banner()
    {
        Services.AddSingleton<IFundService>(new FakeFundService());
        Services.AddSingleton<CivicBudget.Application.Erp.IChartSyncService>(new Chart.FakeChartSyncService(Domain.Erp.ChartSource.Erp, new DateTimeOffset(2026, 9, 19, 14, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("finance");
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/Microsoft.AspNetCore.Components.QuickGrid/QuickGrid.razor.js");

        IRenderedComponent<FundList> page = Render<FundList>();

        page.WaitForAssertion(() => Assert.Equal(2, PopulatedRows(page)));
        Assert.Contains("Managed by the ERP", page.Find(".cb-erp-banner").TextContent);
        Assert.Contains("Dana Whitfield", page.Find(".cb-erp-banner").TextContent);
        Assert.Empty(page.FindAll("a[href='admin/funds/new']"));
        Assert.Empty(page.FindAll(".cb-kebab"));
    }

    private static int PopulatedRows(IRenderedComponent<FundList> page) =>
        page.FindAll("tbody tr").Count(row => !string.IsNullOrWhiteSpace(row.TextContent));

    private sealed class FakeFundService : IFundService
    {
        private readonly List<FundDto> _funds =
        [
            new(Guid.CreateVersion7(), "1000", "General Fund", FundCategory.General, FundGroup.Governmental, null, true),
            new(Guid.CreateVersion7(), "2011", "Street Construction, Maintenance & Repair", FundCategory.SpecialRevenue, FundGroup.Governmental, null, true),
        ];

        public Task<IReadOnlyList<FundDto>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FundDto>>(_funds);

        public Task<FundDto?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_funds.FirstOrDefault(f => f.Id == id));

        public Task<Result<Guid>> SaveAsync(SaveFundRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
