using CivicBudget.Web.Components.Portal.Common;

namespace CivicBudget.Web.Tests.Portal;

public class PortalHelpersTests
{
    [Theory]
    [InlineData(3_664_500, "$3.66M")]
    [InlineData(588_000, "$588K")]
    [InlineData(12_345.67, "$12K")]
    [InlineData(9_999, "$9,999")]
    [InlineData(950, "$950")]
    [InlineData(0, "$0")]
    [InlineData(-84_678, "-$85K")]
    public void MoneyShort_rounds_to_what_fits_on_a_kpi_card(decimal amount, string expected) =>
        Assert.Equal(expected, MoneyShort.Format(amount));

    [Theory]
    [InlineData("https://x/transparency/maple-ridge-oh", "maple-ridge-oh", null, "/transparency/maple-ridge-oh")]
    [InlineData("https://x/transparency/Maple-Ridge-OH/2026/funds/1000?show=pct", "maple-ridge-oh", 2026, "/transparency/maple-ridge-oh/2026")]
    [InlineData("https://x/transparency/maple-ridge-oh/search", "maple-ridge-oh", null, "/transparency/maple-ridge-oh")]
    public void PortalRoutes_reads_the_government_and_year_from_the_url(string url, string slug, int? year, string root)
    {
        PortalRoute route = PortalRoutes.Parse(new Uri(url))!;

        Assert.Equal(slug, route.Slug);
        Assert.Equal(year, route.Year);
        Assert.Equal(root, route.Root);
    }

    [Theory]
    [InlineData("https://x/")]
    [InlineData("https://x/transparency")]
    [InlineData("https://x/admin/budgets")]
    public void PortalRoutes_is_null_outside_a_government_page(string url) =>
        Assert.Null(PortalRoutes.Parse(new Uri(url)));
}
