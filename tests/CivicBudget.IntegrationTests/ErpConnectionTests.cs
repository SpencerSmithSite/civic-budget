using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Funds;
using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Erp.Http;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.ReferenceErp;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// A configured connection end to end: the chart sync service, over the HTTP adapter, against the
/// reference ERP, on the seeded database. Maple Ridge is connected and Pine Hollow is not, the way one
/// CivicBudget serves governments whose ERPs are connected and governments still sending files.
/// </summary>
[Collection(SqlServerTests.Name)]
public sealed class ErpConnectionTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string Key = "test-key-for-the-reference-erp";

    private TestDatabase _database = null!;
    private WebApplication _erp = null!;
    private ServiceProvider _connections = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_ErpConnection");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;

        _erp = ReferenceErpServer.Build(["--urls", "http://127.0.0.1:0", $"--{ReferenceErpServer.KeySetting}={Key}"]);
        await _erp.StartAsync();
        _connections = new ServiceCollection().AddLogging()
            .AddErpConnections(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Erp:Connections:{_mapleRidge}:BaseUrl"] = _erp.Urls.First(),
                [$"Erp:Connections:{_mapleRidge}:ApiKey"] = Key,
            }).Build())
            .BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _connections.DisposeAsync();
        await _erp.StopAsync();
        await _erp.DisposeAsync();
    }

    // The fixture's host registers the simulated ERP; this builds the service with the HTTP adapter instead.
    private ChartSyncService Sync(AsyncServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<ICivicBudgetDbContextFactory>(),
        scope.ServiceProvider.GetRequiredService<ICurrentUser>(),
        scope.ServiceProvider.GetRequiredService<IErpChartFileSource>(),
        [_connections.GetRequiredService<HttpErpAdapter>()],
        TimeProvider.System);

    [Fact]
    public async Task A_connected_government_fetches_its_chart_from_the_ERP_and_sees_what_differs()
    {
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            Fund general = await db.Funds.SingleAsync(f => f.Code == "1000");
            general.Update(general.Code, "General Fund (renamed here)", general.Category, general.Description);
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        ChartSyncService sync = Sync(scope);

        Assert.Equal("ERP", (await sync.StatusAsync()).ApiName);
        Result<ChartSyncPreviewDto> preview = await sync.PreviewFromErpAsync();
        Assert.True(preview.IsSuccess, string.Join("; ", preview.Errors.Select(e => e.Message)));
        ChartChange renamed = Assert.Single(preview.Value.Changes, c => c.Change != ChartChangeKind.Unchanged);
        Assert.Equal(("1000", ChartChangeKind.Update), (renamed.Code, renamed.Change));

        Result<ChartSyncDto> committed = await sync.CommitFromErpAsync();
        Assert.True(committed.IsSuccess);
        Assert.Equal(("ERP", "Fetched by API", 1), (committed.Value.SourceName, committed.Value.FileName, committed.Value.Updated));
    }

    [Fact]
    public async Task A_government_without_a_connection_is_offered_files_alone()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _pineHollow);
        ChartSyncService sync = Sync(scope);

        Assert.Null((await sync.StatusAsync()).ApiName);
        Assert.StartsWith("No ERP connection is set up", (await sync.PreviewFromErpAsync()).Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_fiscal_authority_fetches_the_chart()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.Viewer, _mapleRidge);

        Assert.True((await Sync(scope).PreviewFromErpAsync()).IsFailure);
        Assert.True((await Sync(scope).CommitFromErpAsync()).IsFailure);
    }
}
