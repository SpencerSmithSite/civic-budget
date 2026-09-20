using System.Security.Claims;
using CivicBudget.Application.Portal;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The portal's read model against the seeded snapshots. These tests pin the arithmetic citizens
/// see (totals, shares, fund balances) to the published lines, and prove the service only ever
/// sees Active snapshots because it reads through <see cref="PublicPortalDbContext"/>.
/// </summary>
[Collection(SqlServerTests.Name)]
public class SnapshotQueryServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string Maple = "maple-ridge-oh";
    private const string Pine = "pine-hollow-twp-oh";

    private TestDatabase _database = null!;

    public async Task InitializeAsync()
    {
        // Read-only against Maple Ridge, so one seeded database serves every test in the class.
        // The one test that unpublishes touches Pine Hollow only.
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Portal_" + Guid.NewGuid().ToString("N")[..8]);
        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_governments_that_have_a_published_budget()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        IReadOnlyList<(string Slug, string Name)> governments = await portal.ListGovernmentsAsync();

        Assert.Contains(governments, g => g.Slug == Maple && g.Name == "Village of Maple Ridge");
        Assert.Equal(governments.OrderBy(g => g.Name, StringComparer.Ordinal), governments); // alphabetical for the index page
    }

    [Fact]
    public async Task Budget_header_defaults_to_the_latest_year_and_lists_every_published_year()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        PortalBudgetDto latest = (await portal.GetBudgetAsync(Maple, fiscalYear: null))!;
        PortalBudgetDto fy2025 = (await portal.GetBudgetAsync(Maple, 2025))!;

        Assert.Equal(2026, latest.FiscalYear);
        Assert.Equal("Amendment 1", latest.VersionLabel);
        Assert.NotNull(latest.AmendmentReason);
        Assert.Equal([2026, 2025], latest.AvailableYears.Select(y => y.FiscalYear)); // newest first for the year pills
        Assert.Equal("Original", fy2025.VersionLabel);
        Assert.Null(await portal.GetBudgetAsync(Maple, 1999));
        Assert.Null(await portal.GetBudgetAsync("nowhere-oh", null));
    }

    [Fact]
    public async Task Totals_are_the_sums_of_the_published_lines()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        PortalBudgetDto budget = (await portal.GetBudgetAsync(Maple, 2026))!;
        IReadOnlyList<PortalLineDto> lines = await portal.GetLinesAsync(Maple, 2026);

        Assert.Equal(95, lines.Count);
        Assert.Equal(lines.Where(l => l.AccountType == AccountType.Revenue).Sum(l => l.Amount), budget.TotalRevenues);
        Assert.Equal(lines.Where(l => l.AccountType == AccountType.Expenditure).Sum(l => l.Amount), budget.TotalExpenditures);
        Assert.Equal(lines.Where(l => l.AccountType == AccountType.TransferIn).Sum(l => l.Amount), budget.TotalTransfersIn);
        Assert.Equal(lines.Where(l => l.AccountType == AccountType.TransferOut).Sum(l => l.Amount), budget.TotalTransfersOut);
        // Transfers move money between this government's own funds, so they net to zero across all funds.
        Assert.Equal(budget.TotalTransfersIn, budget.TotalTransfersOut);
        Assert.Equal(
            budget.TotalBeginningBalance + budget.TotalRevenues - budget.TotalExpenditures,
            budget.ProjectedEndingBalance);
        // The seed's published figures; a change here means the seed or the snapshot changed.
        Assert.Equal(3_704_910m, budget.TotalRevenues);
        Assert.Equal(3_256_604m, budget.TotalExpenditures);
    }

    [Fact]
    public async Task Breakdowns_sort_by_amount_and_their_shares_add_up()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();
        PortalBudgetDto budget = (await portal.GetBudgetAsync(Maple, 2026))!;

        BreakdownDto byFund = (await portal.ExpendituresByFundAsync(Maple, 2026))!;
        BreakdownDto byCategory = (await portal.ExpendituresByCategoryAsync(Maple, 2026))!;
        BreakdownDto byDepartment = (await portal.ExpendituresByDepartmentAsync(Maple, 2026))!;
        BreakdownDto revenues = (await portal.RevenuesByCategoryAsync(Maple, 2026))!;

        foreach (BreakdownDto breakdown in new[] { byFund, byCategory, byDepartment })
        {
            Assert.Equal(budget.TotalExpenditures, breakdown.Total);
            Assert.Equal(breakdown.Items.OrderByDescending(i => i.Amount), breakdown.Items);
            Assert.InRange(breakdown.Items.Sum(breakdown.ShareOf), 99.5m, 100.5m); // one-decimal rounding per row
        }

        Assert.Equal(budget.TotalRevenues, revenues.Total);
        Assert.Equal(5, byFund.Items.Count);
        Assert.Equal("1000", byFund.Items[0].Key); // the General Fund is the largest spender
        Assert.Contains("gasoline tax", byFund.Items.Single(i => i.Key == "2011").Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Personal services", byCategory.Items.Single(i => i.Key == nameof(ReportingCategory.PersonalServices)).Label);
        Assert.All(byDepartment.Items, i => Assert.NotEqual(0m, i.PriorYearAmount)); // prior-year column has data to compare against
    }

    [Fact]
    public async Task Fund_page_carries_the_Ohio_balance_arithmetic()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        PortalFundDto street = (await portal.GetFundAsync(Maple, 2026, "2011"))!;
        PortalFundDto general = (await portal.GetFundAsync(Maple, 2026, "1000"))!;

        Assert.Equal(FundCategory.SpecialRevenue, street.Category);
        Assert.Equal(street.BeginningBalance + street.Revenues + street.TransfersIn, street.EstimatedResources);
        Assert.Equal(street.Expenditures + street.TransfersOut, street.Appropriations);
        Assert.Equal(street.EstimatedResources - street.Appropriations, street.ProjectedEndingBalance);
        Assert.Equal(street.Expenditures, street.ExpendituresByDepartment.Total);
        Assert.Equal(street.Expenditures, street.ExpendituresByCategory.Total);
        Assert.Equal(street.Revenues + street.TransfersIn, street.RevenuesByCategory.Total);
        Assert.Equal(620_000m, general.BeginningBalance);
        Assert.True(general.Expenditures > street.Expenditures);
        Assert.Null(await portal.GetFundAsync(Maple, 2026, "9999"));
    }

    [Fact]
    public async Task Department_page_lists_its_lines_in_reading_order()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        PortalDepartmentDto police = (await portal.GetDepartmentAsync(Maple, 2026, "1000", "110"))!;

        Assert.Equal("Police", police.Name);
        Assert.Equal("1000", police.FundCode);
        Assert.Equal("1000-110-5120", police.Lines.Single(l => l.AccountCode == "5120").AccountNumber); // the UAN-style full number, frozen at publish
        Assert.Equal(53_000m, police.Lines.Single(l => l.AccountCode == "5120").Amount); // the amended overtime line
        Assert.Equal(police.Lines.Where(l => l.AccountType == AccountType.Expenditure).Sum(l => l.Amount), police.Expenditures);
        Assert.Equal(police.Expenditures, police.ByCategory.Total);
        Assert.Equal(
            police.Lines.OrderBy(l => l.AccountType).ThenBy(l => l.Category).ThenBy(l => l.AccountCode, StringComparer.Ordinal),
            police.Lines);
        Assert.Null(await portal.GetDepartmentAsync(Maple, 2026, "2011", "110")); // no police lines in the Street fund

        // The narrative written on the FY2026 original travelled through the amendment into the published snapshot (Phase 9d).
        Assert.StartsWith("The department requests funding for eight sworn officers", police.Narrative, StringComparison.Ordinal);
        Assert.Null((await portal.GetDepartmentAsync(Maple, 2026, "1000", "725"))!.Narrative); // Finance wrote none
    }

    [Fact]
    public async Task Year_over_year_covers_every_published_year_in_order()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        IReadOnlyList<YearTotalsDto> years = await portal.YearOverYearAsync(Maple);
        PortalBudgetDto fy2026 = (await portal.GetBudgetAsync(Maple, 2026))!;

        Assert.Equal([2025, 2026], years.Select(y => y.FiscalYear));
        Assert.Equal(["Original", "Amendment 1"], years.Select(y => y.VersionLabel));
        Assert.Equal(fy2026.TotalRevenues, years[1].Revenues);
        Assert.Equal(fy2026.TotalExpenditures, years[1].Expenditures);
        Assert.Equal(fy2026.ProjectedEndingBalance, years[1].EndingBalance);
        Assert.Empty(await portal.YearOverYearAsync("nowhere-oh"));
    }

    [Fact]
    public async Task Search_finds_funds_departments_and_accounts_and_links_to_their_pages()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        IReadOnlyList<PortalSearchHitDto> police = await portal.SearchAsync(Maple, 2026, "police");
        IReadOnlyList<PortalSearchHitDto> street = await portal.SearchAsync(Maple, 2026, "STREET");
        IReadOnlyList<PortalSearchHitDto> overtime = await portal.SearchAsync(Maple, 2026, "5120");

        PortalSearchHitDto department = Assert.Single(police, h => h.Kind == "Department");
        Assert.Equal("/transparency/maple-ridge-oh/2026/funds/1000/departments/110", department.Url);
        Assert.Contains(street, h => h.Kind == "Fund" && h.Url == "/transparency/maple-ridge-oh/2026/funds/2011"); // case-insensitive
        Assert.Contains(overtime, h => h.Kind == "Account" && h.Amount == 53_000m);
        Assert.Contains(await portal.SearchAsync(Maple, 2026, "1000-110"), h => h.Kind == "Account" && h.Label.StartsWith("1000-110-", StringComparison.Ordinal)); // by full number
        Assert.Contains(await portal.SearchAsync(Maple, 2026, "1000110"), h => h.Kind == "Account");                                                                  // separators optional
        Assert.Empty(await portal.SearchAsync(Maple, 2026, "p"));   // too short to be useful
        Assert.Empty(await portal.SearchAsync(Maple, 2026, "zzz")); // nothing matches
    }

    [Fact]
    public async Task Unpublishing_removes_the_government_from_the_portal_immediately()
    {
        Guid pineHollow;
        await using (CivicBudgetDbContext db = _database.CreateContext(tenant: null))
        {
            pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == Pine)).Id;
        }

        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-fd"));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test Finance Director"));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.FinanceDirector));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, pineHollow.ToString()));
        await using AsyncServiceScope scope = _database.CreateScope(user: new ClaimsPrincipal(identity));
        ISnapshotQueryService portal = scope.ServiceProvider.GetRequiredService<ISnapshotQueryService>();
        IPublishingService publishing = scope.ServiceProvider.GetRequiredService<IPublishingService>();

        Assert.NotNull(await portal.GetBudgetAsync(Pine, null));
        SnapshotSummaryDto only = Assert.Single(await publishing.ListAsync());
        Assert.True((await publishing.UnpublishAsync(only.Id)).IsSuccess);

        // The row still exists for the auditors; the portal context filters it out, so the service never sees it.
        Assert.Null(await portal.GetBudgetAsync(Pine, null));
        Assert.Empty(await portal.YearOverYearAsync(Pine));
        Assert.Empty(await portal.GetLinesAsync(Pine, only.FiscalYear));
        Assert.DoesNotContain(await portal.ListGovernmentsAsync(), g => g.Slug == Pine);
    }
}
