using CivicBudget.Application.Common;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Two people working on one budget at the same moment. Each service call loads the budget, changes
/// it, and saves; these tests hold two of those in flight at once, which a single call cannot.
/// </summary>
[Collection(SqlServerTests.Name)]
public class ConcurrencyTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Concurrency_" + Guid.NewGuid().ToString("N")[..8]);
        await using (AsyncServiceScope scope = _database.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
        }

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _draft2027 = (await db.BudgetVersions.IgnoreQueryFilters().SingleAsync(v => v.GovernmentId == _mapleRidge && v.Status == BudgetStatus.Draft)).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<BudgetVersion> LoadAsync(CivicBudgetDbContext db) =>
        await db.BudgetVersions.Include(v => v.Lines).ThenInclude(l => l.Account).Include(v => v.BeginningBalances).SingleAsync(v => v.Id == _draft2027);

    [Fact]
    public async Task An_edit_based_on_a_stale_read_is_refused_instead_of_overwriting()
    {
        await using CivicBudgetDbContext first = _database.CreateContext(_mapleRidge);
        await using CivicBudgetDbContext second = _database.CreateContext(_mapleRidge);
        BudgetVersion mine = await LoadAsync(first);
        BudgetVersion theirs = await LoadAsync(second);
        Guid lineId = mine.Lines.First().Id;

        mine.UpdateLineAmount(lineId, 1_111m);
        Assert.Null(await first.TrySaveAsync(CancellationToken.None));

        theirs.UpdateLineAmount(lineId, 2_222m);
        Result? conflict = await second.TrySaveAsync(CancellationToken.None);

        Assert.Equal(SaveConflicts.Message, conflict!.Errors.Single().Message);
        await using CivicBudgetDbContext check = _database.CreateContext(_mapleRidge);
        Assert.Equal(1_111m, (await check.BudgetLines.SingleAsync(l => l.Id == lineId)).Amount); // the first save stands
    }

    [Fact]
    public async Task An_amount_edit_cannot_land_on_a_budget_adopted_a_moment_earlier()
    {
        await using CivicBudgetDbContext editor = _database.CreateContext(_mapleRidge);
        await using CivicBudgetDbContext officer = _database.CreateContext(_mapleRidge);
        BudgetVersion editing = await LoadAsync(editor);   // read while still Draft
        BudgetVersion adopting = await LoadAsync(officer);

        adopting.Propose();
        adopting.Adopt("2027-60", "user-fd", DateTimeOffset.UtcNow);
        Assert.Null(await officer.TrySaveAsync(CancellationToken.None));

        editing.UpdateLineAmount(editing.Lines.First().Id, 9_999m); // allowed by the stale in-memory Draft status
        Assert.NotNull(await editor.TrySaveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Two_adoptions_cannot_both_succeed()
    {
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            (await LoadAsync(db)).Propose();
            await db.SaveChangesAsync();
        }

        await using CivicBudgetDbContext a = _database.CreateContext(_mapleRidge);
        await using CivicBudgetDbContext b = _database.CreateContext(_mapleRidge);
        BudgetVersion first = await LoadAsync(a);
        BudgetVersion second = await LoadAsync(b);
        first.Adopt("2027-61", "user-a", DateTimeOffset.UtcNow);
        second.Adopt("2027-62", "user-b", DateTimeOffset.UtcNow);

        Assert.Null(await a.TrySaveAsync(CancellationToken.None));
        Assert.NotNull(await b.TrySaveAsync(CancellationToken.None));
        await using CivicBudgetDbContext check = _database.CreateContext(_mapleRidge);
        Assert.Equal("2027-61", (await check.BudgetVersions.SingleAsync(v => v.Id == _draft2027)).ResolutionNumber);
    }
}
