using System.Security.Claims;
using CivicBudget.Application.Common;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The certificate from the seeded data: FY2026's balances come from the ERP's closed FY2025 and
/// reconcile to the budget; FY2027's are the budget's estimate and the Street fund is over; the
/// settings and the reserves change what it certifies; and who may see and change what.
/// </summary>
[Collection(SqlServerTests.Name)]
public class CertificateServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge, _pineHollow, _amendment2026, _draft2027, _pineHollow2026;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Certificate");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
        var versions = await db.BudgetVersions.IgnoreQueryFilters()
            .Join(db.FiscalYears.IgnoreQueryFilters(), v => v.FiscalYearId, f => f.Id, (v, f) => new { v.Id, v.GovernmentId, f.Year, v.VersionNumber })
            .ToListAsync();
        _amendment2026 = versions.Single(v => v.GovernmentId == _mapleRidge && v.Year == 2026 && v.VersionNumber == 2).Id;
        _draft2027 = versions.Single(v => v.GovernmentId == _mapleRidge && v.Year == 2027).Id;
        _pineHollow2026 = versions.Single(v => v.GovernmentId == _pineHollow && v.Year == 2026).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Fy2026s_balances_come_from_the_erps_closed_year_and_reconcile_with_the_budget()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);

        CertificateReportDto c = (await scope.ServiceProvider.GetRequiredService<ICertificateService>().GetAsync(_amendment2026))!;

        Assert.Equal(("Amended Certificate of Estimated Resources No. 1", "Harmon", "Dana Whitfield"), (c.Header.Title, c.Header.County, c.Header.FiscalOfficerName));
        Assert.True(c.CarryoverFromErp);
        CertificateRowDto general = c.Funds.Single(f => f.FundCode == "1000");
        Assert.Equal(620_000m, general.Carryover);                                  // the seeded FY2026 beginning balance
        Assert.True(general.Cash > general.Carryover);                              // cash less carried encumbrances
        Assert.Equal(["Taxes"], c.RevenueColumnLabels);
        Assert.Equal(422_300m + 1_287_500m, general.RevenueColumns[0]);             // real estate and income tax
        Assert.All(c.Checks, check => Assert.True(check.Passed, check.Detail));
        Assert.Equal("FY2026 Original", c.PriorLabel);
        Assert.Empty(c.Changes);                                                    // the amendment moved appropriations only
    }

    [Fact]
    public async Task Fy2027_uses_the_budgets_estimate_and_flags_the_street_fund()
    {
        await using AsyncServiceScope scope = As(Roles.Viewer, _mapleRidge);

        CertificateReportDto c = (await scope.ServiceProvider.GetRequiredService<ICertificateService>().GetAsync(_draft2027))!;

        Assert.False(c.CarryoverFromErp);
        Assert.Equal(new DateOnly(2026, 12, 31), c.CarryoverAsOf);
        Assert.False(c.Checks.Single(k => k.Label.StartsWith("Appropriations stay within", StringComparison.Ordinal)).Passed);
        Assert.False(c.Funds.Single(f => f.FundCode == "2011").IsWithinLimit);
        Assert.Null(c.PriorLabel);
    }

    [Fact]
    public async Task A_government_with_no_settings_gets_the_ohio_default_column()
    {
        await using AsyncServiceScope scope = As(Roles.Admin, _pineHollow);
        ICertificateService certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();

        CertificateReportDto c = (await certificates.GetAsync(_pineHollow2026))!;
        CertificateSettingsDto settings = await certificates.GetSettingsAsync();

        Assert.Null(c.Header.County);
        Assert.Equal(["Taxes"], c.RevenueColumnLabels);
        Assert.Contains(c.Notes, n => n.Contains("categorized as taxes", StringComparison.Ordinal));
        Assert.True(settings.UsingDefaultColumns);
        Assert.Single(settings.Columns[0].AccountIds);                               // Pine Hollow's one tax account
    }

    [Fact]
    public async Task Saved_columns_and_headings_change_what_the_certificate_prints()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);
        ICertificateService certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
        CertificateSettingsDto settings = await certificates.GetSettingsAsync();
        Guid Account(string code) => settings.RevenueAccounts.Single(a => a.Code == code).Id;

        Result saved = await certificates.SaveSettingsAsync(new SaveCertificateSettingsRequest("Harmon", "Dana Whitfield", "Fiscal Officer", "Unencumbered Balance January 1",
            "Rollbacks & Other Sources", [new ReportColumnDto("Real Estate", [Account("4110")]), new ReportColumnDto("Local Taxes", [Account("4130")])]));

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        CertificateReportDto c = (await certificates.GetAsync(_amendment2026))!;
        Assert.Equal(("Unencumbered Balance January 1", "Rollbacks & Other Sources"), (c.BalanceLabel, c.OtherSourcesLabel));
        Assert.Equal(["Real Estate", "Local Taxes"], c.RevenueColumnLabels);
        Assert.Equal([422_300m, 1_287_500m], c.Funds.Single(f => f.FundCode == "1000").RevenueColumns);

        Result twice = await certificates.SaveSettingsAsync(new SaveCertificateSettingsRequest(null, null, "FO", "B", "O",
            [new ReportColumnDto("A", [Account("4110")]), new ReportColumnDto("B2", [Account("4110")])]));
        Assert.Contains("count the money twice", twice.Errors.Single().Message, StringComparison.Ordinal);

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.Contains(await db.AuditEntries.Select(a => a.Description).ToListAsync(),
            d => d != null && d.StartsWith("Updated the certificate settings: Real Estate (1 account), Local Taxes (1 account)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reserves_and_advances_lower_the_carryover_and_the_reconciliation_says_the_budget_now_differs()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);
        ICertificateService certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
        FundAdjustmentDto general = (await certificates.GetAdjustmentsAsync(_amendment2026))!.Single(f => f.FundCode == "1000");

        Result saved = await certificates.SaveAdjustmentsAsync(new SaveFundAdjustmentsRequest(_amendment2026, [general with { Reserves = 20_000m, UnpaidAdvances = 5_000m }]));

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        CertificateReportDto c = (await certificates.GetAsync(_amendment2026))!;
        Assert.Equal(620_000m - 20_000m + 5_000m, c.Funds.Single(f => f.FundCode == "1000").Carryover);
        CertificateCheckDto balance = c.Checks.Single(k => k.Label.StartsWith("The budget starts", StringComparison.Ordinal));
        Assert.False(balance.Passed);
        Assert.Contains("1000 General Fund: the budget starts with $620,000.00, the certificate certifies $605,000.00", balance.Detail, StringComparison.Ordinal);

        Result negative = await certificates.SaveAdjustmentsAsync(new SaveFundAdjustmentsRequest(_amendment2026, [general with { Nonspendable = -1m }]));
        Assert.StartsWith("1000: Nonspendable and reserve balances are amounts set aside", negative.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Department_users_get_no_certificate_and_only_the_fiscal_office_changes_its_settings()
    {
        await using AsyncServiceScope head = As(Roles.DepartmentHead, _mapleRidge);
        Assert.Null(await head.ServiceProvider.GetRequiredService<ICertificateService>().GetAsync(_amendment2026));

        await using AsyncServiceScope viewer = As(Roles.Viewer, _mapleRidge);
        ICertificateService certificates = viewer.ServiceProvider.GetRequiredService<ICertificateService>();
        Assert.True((await certificates.SaveSettingsAsync(new SaveCertificateSettingsRequest(null, null, "FO", "B", "O", []))).IsFailure);
        Assert.True((await certificates.SaveAdjustmentsAsync(new SaveFundAdjustmentsRequest(_amendment2026, []))).IsFailure);
        Assert.Null(await certificates.GetAdjustmentsAsync(_amendment2026));
    }

    private AsyncServiceScope As(string role, Guid government)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, government.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
