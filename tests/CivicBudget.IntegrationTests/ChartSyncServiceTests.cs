using System.Security.Claims;
using System.Text;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Erp;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The chart arrives from the ERP: preview against the seeded chart, commit through the entities
/// (so the audit interceptor sees every field), the sync log, the chart-source switch, and the guards.
/// </summary>
[Collection(SqlServerTests.Name)]
public class ChartSyncServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync("CivicBudget_ChartSync_" + Guid.NewGuid().ToString("N")[..8]);
        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Preview_compares_the_file_with_the_seeded_chart()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IChartSyncService sync = scope.ServiceProvider.GetRequiredService<IChartSyncService>();

        Result<ChartSyncPreviewDto> result = await sync.PreviewAsync("vip-chart.csv", Csv(
            "Kind,Code,Name,Type,Category,Description",
            "Fund,1000,General Fund,,General,\"Pays for day-to-day village services (police, administration, parks, and zoning), mostly from income and property taxes.\"",
            "Fund,2050,Fire Levy,,Special Revenue,Voted fire levy",
            "Department,110,Police Department,,,",
            "Object,5120,Overtime,Expenditure,Personal Services,"));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        ChartSyncPreviewDto preview = result.Value;
        Assert.Equal(ChartChangeKind.Unchanged, preview.Changes.Single(c => c.Code == "1000").Change);
        Assert.Equal(ChartChangeKind.Add, preview.Changes.Single(c => c.Code == "2050").Change);
        Assert.Equal(ChartChangeKind.Update, preview.Changes.Single(c => c.Code == "110").Change);
        Assert.Equal(ChartChangeKind.Deactivate, preview.Changes.Single(c => c.Code == "2011").Change); // the Street fund is not in this file
        Assert.True(preview.Count(ChartChangeKind.Deactivate) > 10);                                    // most of the seeded chart is not in this file
        Assert.Equal(ChartSource.Local, (await sync.StatusAsync()).Source);
    }

    [Fact]
    public async Task Commit_applies_the_changes_logs_the_sync_and_hands_the_chart_to_the_erp()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IChartSyncService sync = scope.ServiceProvider.GetRequiredService<IChartSyncService>();
        IFundService funds = scope.ServiceProvider.GetRequiredService<IFundService>();
        IReadOnlyList<FundDto> before = await funds.ListAsync(includeInactive: true);

        // The full seeded chart plus one new fund, one renamed department, and one fund left out.
        string[] lines = await FullChartAsync(scope, skipFund: "4901", extra: "Fund,2050,Fire Levy,,SpecialRevenue,Voted fire levy", renamePolice: true);

        Result<ChartSyncDto> committed = await sync.CommitAsync("vip-chart.csv", Csv(lines));

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal((1, 1, 1, 0), (committed.Value.Added, committed.Value.Updated, committed.Value.Deactivated, committed.Value.Reactivated));

        IReadOnlyList<FundDto> after = await funds.ListAsync(includeInactive: true);
        Assert.Contains(after, f => f.Code == "2050" && f.Name == "Fire Levy" && f.IsActive);
        Assert.False(after.Single(f => f.Code == "4901").IsActive);                                   // deactivated, still there
        Assert.Equal(before.Count + 1, after.Count);                                                  // nothing deleted
        Assert.Equal("Police Department", (await scope.ServiceProvider.GetRequiredService<IDepartmentService>().ListAsync(false)).Single(d => d.Code == "110").Name);

        ChartSourceStatusDto status = await sync.StatusAsync();
        Assert.Equal(ChartSource.Erp, status.Source);
        Assert.Equal("Test FinanceDirector", status.LastSyncedBy);
        ChartSyncDto logged = Assert.Single(await sync.HistoryAsync());
        Assert.Equal(3, logged.Changes.Count); // only the real changes are kept in the log

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Kind == AuditKind.Event && a.Description!.StartsWith("Synced the chart")));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.PropertyName == "Name" && a.NewValue == "Police Department")); // the interceptor recorded the rename
    }

    [Fact]
    public async Task A_file_that_matches_the_chart_is_refused_and_a_department_head_may_not_sync()
    {
        await using AsyncServiceScope head = As(Roles.DepartmentHead);
        Result<ChartSyncPreviewDto> denied = await head.ServiceProvider.GetRequiredService<IChartSyncService>().PreviewAsync("c.csv", Csv("Kind,Code,Name", "Fund,1,x"));
        Assert.Contains("Administrator or the Fiscal Officer", denied.Errors.Single().Message, StringComparison.Ordinal);

        await using AsyncServiceScope admin = As(Roles.Admin);
        IChartSyncService sync = admin.ServiceProvider.GetRequiredService<IChartSyncService>();
        string[] chart = await FullChartAsync(admin);
        Assert.True((await sync.CommitAsync("same.csv", Csv(chart))).IsFailure);      // identical to what is seeded: nothing to sync
        Assert.Empty(await sync.HistoryAsync());
    }

    [Fact]
    public async Task A_file_that_changes_the_type_of_an_account_in_use_is_refused_before_anything_changes()
    {
        await using AsyncServiceScope admin = As(Roles.Admin);
        IChartSyncService sync = admin.ServiceProvider.GetRequiredService<IChartSyncService>();
        // 5110 Salaries & Wages carries lines in every seeded budget, adopted ones included.
        string[] chart = (await FullChartAsync(admin, renamePolice: true))
            .Select(l => l.StartsWith("Object,5110,", StringComparison.Ordinal) ? "Object,5110,\"Salaries & Wages\",Revenue,Taxes," : l)
            .ToArray();

        Result<ChartSyncPreviewDto> preview = await sync.PreviewAsync("retyped.csv", Csv(chart));
        Result<ChartSyncDto> committed = await sync.CommitAsync("retyped.csv", Csv(chart));

        Assert.Contains("5110", preview.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains("5110", committed.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Empty(await sync.HistoryAsync());
        Assert.Equal(AccountType.Expenditure, (await admin.ServiceProvider.GetRequiredService<IAccountService>().ListAsync(includeInactive: true)).Single(a => a.Code == "5110").Type);
    }

    /// <summary>The seeded chart as an export file, optionally with one fund left out, one row added, and Police renamed.</summary>
    private static async Task<string[]> FullChartAsync(AsyncServiceScope scope, string? skipFund = null, string? extra = null, bool renamePolice = false)
    {
        List<string> lines = ["Kind,Code,Name,Type,Category,Description"];
        foreach (FundDto f in (await scope.ServiceProvider.GetRequiredService<IFundService>().ListAsync(includeInactive: true)).Where(f => f.Code != skipFund))
        {
            lines.Add($"Fund,{f.Code},\"{f.Name}\",,{f.Category},\"{f.Description}\"");
        }

        if (extra is not null)
        {
            lines.Add(extra);
        }

        foreach (DepartmentDto d in await scope.ServiceProvider.GetRequiredService<IDepartmentService>().ListAsync(includeInactive: true))
        {
            lines.Add($"Department,{d.Code},\"{(renamePolice && d.Code == "110" ? "Police Department" : d.Name)}\",,,\"{d.Description}\"");
        }

        foreach (AccountDto a in await scope.ServiceProvider.GetRequiredService<IAccountService>().ListAsync(includeInactive: true))
        {
            lines.Add($"Object,{a.Code},\"{a.Name}\",{a.Type},{a.Category},");
        }

        return lines.ToArray();
    }

    [Fact]
    public async Task Only_an_administrator_switches_the_chart_source()
    {
        await using AsyncServiceScope director = As(Roles.FinanceDirector);
        Assert.True((await director.ServiceProvider.GetRequiredService<IChartSyncService>().SetChartSourceAsync(ChartSource.Erp)).IsFailure);

        await using AsyncServiceScope admin = As(Roles.Admin);
        IChartSyncService sync = admin.ServiceProvider.GetRequiredService<IChartSyncService>();
        Assert.True((await sync.SetChartSourceAsync(ChartSource.Erp)).IsSuccess);
        Assert.Equal(ChartSource.Erp, (await sync.StatusAsync()).Source);
        Assert.True((await sync.SetChartSourceAsync(ChartSource.Local)).IsSuccess);
        Assert.Equal(ChartSource.Local, (await sync.StatusAsync()).Source);
    }

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    private AsyncServiceScope As(string role)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
