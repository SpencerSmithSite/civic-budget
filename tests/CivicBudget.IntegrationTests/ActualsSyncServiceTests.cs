using System.Security.Claims;
using System.Text;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Actuals from the ERP: what the seed brings, a sync from the simulated VIP and from a file,
/// the prior-year actuals a closed year fills, the figures the budget screens show beside each
/// line, and who may sync.
/// </summary>
[Collection(SqlServerTests.Name)]
public class ActualsSyncServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;
    private Guid _draft2027;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Actuals");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
        Guid fy2027 = (await db.FiscalYears.IgnoreQueryFilters().SingleAsync(f => f.GovernmentId == _mapleRidge && f.Year == 2027)).Id;
        _draft2027 = (await db.BudgetVersions.IgnoreQueryFilters().SingleAsync(v => v.FiscalYearId == fy2027)).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_seed_holds_last_years_closed_books_and_this_year_so_far()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);
        ActualsStatusDto status = await scope.ServiceProvider.GetRequiredService<IActualsSyncService>().StatusAsync();

        Assert.Equal("VIP (simulated)", status.ApiName);
        ActualsYearDto fy2025 = status.Years.Single(y => y.FiscalYear == 2025);
        Assert.True(fy2025.IsClosed);
        Assert.Equal(new DateOnly(2025, 12, 31), fy2025.AsOf);
        Assert.Contains(status.Years, y => y.FiscalYear == 2026);
        Assert.DoesNotContain(2027, status.FetchableYears);          // a year that has not begun has no books
    }

    [Fact]
    public async Task Fetching_a_closed_year_restores_prior_year_actuals_that_drifted_and_records_it()
    {
        // Someone typed over one prior-year actual in the draft.
        Guid lineId;
        decimal fromErp;
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            BudgetVersion draft = await db.BudgetVersions.Include(v => v.Lines).SingleAsync(v => v.Id == _draft2027);
            BudgetLine line = draft.Lines.First(l => l.PriorYearActual > 0m && l.DepartmentId is not null);
            (lineId, fromErp) = (line.Id, line.PriorYearActual);
            draft.UpdateLineComparatives(line.Id, 1.00m, line.CurrentYearBudget);
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);
        IActualsSyncService sync = scope.ServiceProvider.GetRequiredService<IActualsSyncService>();

        Result<ActualsPreviewDto> preview = await sync.PreviewFromErpAsync(2025);
        Assert.True(preview.IsSuccess, string.Join("; ", preview.Errors.Select(e => e.Message)));
        PriorActualChangeDto change = preview.Value.PriorActualChanges.Single(); // every other line already matches VIP
        Assert.Equal(("FY2027 Original", 1.00m, fromErp), (change.Version, change.Before, change.After));
        Assert.True(preview.Value.Receipts > 0m && preview.Value.Disbursements > 0m && preview.Value.Cash > 0m);

        Result<ActualsSyncDto> committed = await sync.CommitFromErpAsync(2025);

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal(1, committed.Value.PriorActualsUpdated);
        await using CivicBudgetDbContext check = _database.CreateContext(_mapleRidge);
        Assert.Equal(fromErp, (await check.BudgetLines.SingleAsync(l => l.Id == lineId)).PriorYearActual);
        Assert.Equal(2, await check.ActualsSyncs.CountAsync(s => s.FiscalYear == 2025));   // the seed's and this one
        Assert.Contains(await check.AuditEntries.Where(a => a.EntityName == "Government").Select(a => a.Description).ToListAsync(),
            d => d != null && d.StartsWith("Synced FY2025 actuals from VIP (simulated) through the full year; updated 1 prior-year actual", StringComparison.Ordinal));
        Assert.Contains(await check.AuditEntries.Where(a => a.EntityId == lineId).Select(a => a.PropertyName).ToListAsync(), p => p == nameof(BudgetLine.PriorYearActual));
    }

    [Fact]
    public async Task An_uploaded_file_replaces_the_whole_year()
    {
        await using AsyncServiceScope scope = As(Roles.Admin, _mapleRidge);
        IActualsSyncService sync = scope.ServiceProvider.GetRequiredService<IActualsSyncService>();

        Result<ActualsSyncDto> committed = await sync.CommitFileAsync("vip-fy2026.csv", Csv(
            "Fiscal Year,Type,Account,Period,Amount",
            "2026,Actual,1000-110-5110,1,\"40,000.00\"",
            "2026,Actual,1000-110-5110,2,\"41,000.00\"",
            "2026,Actual,1000-4110,2,\"12,500.00\"",
            "2026,Encumbrance,1000-110-5310,,\"3,000\"",
            "2026,Cash,1000,,\"700,000\""));

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal((2, "vip-fy2026.csv", 0), (committed.Value.ThroughPeriod, committed.Value.FileName, committed.Value.PriorActualsUpdated)); // an open year fills nothing
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.Equal(3, await db.ErpActuals.CountAsync(a => a.FiscalYear == 2026));                  // the seed's months are gone
        Assert.Equal(81_000m, await db.ErpActuals.Where(a => a.FiscalYear == 2026 && a.Amount != 12_500m).SumAsync(a => a.Amount));
        Assert.Equal(700_000m, (await db.ErpFundCash.SingleAsync(c => c.FiscalYear == 2026)).Amount);
        Assert.True(await db.ErpActuals.AnyAsync(a => a.FiscalYear == 2025));                        // other years untouched

        // The draft's screen now shows those figures beside the police salaries line.
        BudgetWorkspaceDto workspace = (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;
        Assert.Equal((2026, 2, new DateOnly(2026, 2, 28)), (workspace.CurrentYearActuals!.FiscalYear, workspace.CurrentYearActuals.ThroughPeriod, workspace.CurrentYearActuals.AsOf));
        Assert.Equal(81_000m, workspace.Lines.Single(l => l.AccountNumber == "1000-110-5110").YearToDate);
        Assert.Equal(3_000m, workspace.Lines.Single(l => l.AccountNumber == "1000-110-5310").Encumbered);
        Assert.Equal(0m, workspace.Lines.Single(l => l.AccountNumber == "1000-620-5110").YearToDate);
    }

    [Fact]
    public async Task A_file_with_a_code_the_chart_lacks_changes_nothing()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);
        IActualsSyncService sync = scope.ServiceProvider.GetRequiredService<IActualsSyncService>();
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        int before = await db.ErpActuals.CountAsync();

        Result<ActualsSyncDto> committed = await sync.CommitFileAsync("bad.csv", Csv(
            "Fiscal Year,Type,Account,Period,Amount",
            "2026,Actual,1000-110-5110,1,100",
            "2026,Actual,1000-999-5110,1,100"));

        Assert.Equal("1000-999-5110: no department or program has the code 999.", committed.Errors.Single().Message);
        Assert.Equal(before, await db.ErpActuals.CountAsync());
    }

    [Fact]
    public async Task A_line_added_back_takes_its_prior_year_actual_from_the_erp()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _mapleRidge);
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetLineDto fuel = (await entry.GetWorkspaceAsync(_draft2027))!.Lines.Single(l => l.AccountNumber == "1000-110-5420");
        Assert.True((await entry.RemoveLineAsync(_draft2027, fuel.Id)).IsSuccess);

        Result<Guid> added = await entry.AddLineAsync(new AddBudgetLineRequest(_draft2027, fuel.FundId, fuel.DepartmentId, fuel.AccountId, 30_000m, 0m, 0m, null));

        Assert.True(added.IsSuccess, string.Join("; ", added.Errors.Select(e => e.Message)));
        BudgetLineDto back = (await entry.GetWorkspaceAsync(_draft2027))!.Lines.Single(l => l.Id == added.Value);
        Assert.Equal(fuel.PriorYearActual, back.PriorYearActual);
        Assert.True(back.PriorYearActual > 0m);
    }

    [Theory]
    [InlineData(Roles.DepartmentHead)]
    [InlineData(Roles.Viewer)]
    public async Task Only_the_fiscal_officer_or_an_administrator_syncs(string role)
    {
        await using AsyncServiceScope scope = As(role, _mapleRidge);
        IActualsSyncService sync = scope.ServiceProvider.GetRequiredService<IActualsSyncService>();

        Assert.True((await sync.PreviewFromErpAsync(2025)).IsFailure);
        Assert.True((await sync.CommitFromErpAsync(2025)).IsFailure);
        Assert.True((await sync.CommitFileAsync("x.csv", Csv("Fiscal Year,Type,Account,Period,Amount", "2026,Actual,1000-110-5110,1,1"))).IsFailure);
    }

    [Fact]
    public async Task Each_government_sees_and_replaces_only_its_own_figures()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector, _pineHollow);
        IActualsSyncService sync = scope.ServiceProvider.GetRequiredService<IActualsSyncService>();
        await using CivicBudgetDbContext all = _database.CreateContext(tenant: null);
        int mapleRidgeRows = await all.ErpActuals.IgnoreQueryFilters().CountAsync(a => a.GovernmentId == _mapleRidge && a.FiscalYear == 2026);

        Result<ActualsSyncDto> committed = await sync.CommitFromErpAsync(2026);

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal(12, committed.Value.ThroughPeriod);                                   // July 2025 to June 2026, closed
        Assert.All((await sync.HistoryAsync()), h => Assert.Equal("VIP (simulated)", h.SourceName));
        Assert.Equal(mapleRidgeRows, await all.ErpActuals.IgnoreQueryFilters().CountAsync(a => a.GovernmentId == _mapleRidge && a.FiscalYear == 2026));
        Assert.Equal(new DateOnly(2026, 6, 30), committed.Value.AsOf);
    }

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

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
