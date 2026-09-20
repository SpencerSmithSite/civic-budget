using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The nightly demo reset (`--reseed`): everything a visitor changed is gone, the seed is back, and
/// the database object itself survives (on Azure it carries the free offer).
/// </summary>
[Collection(SqlServerTests.Name)]
public class DatabaseResetTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Reset_drops_every_table_and_rebuilds_the_seed()
    {
        TestDatabase database = await fixture.CreateDatabaseAsync("CivicBudget_Reset_" + Guid.NewGuid().ToString("N")[..8]);
        await using (AsyncServiceScope scope = database.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
        }

        // A visitor's day: a draft line changed and a user renamed.
        Guid mapleRidge;
        await using (CivicBudgetDbContext db = database.CreateContext(tenant: null))
        {
            mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        }

        await using (CivicBudgetDbContext db = database.CreateContext(mapleRidge))
        {
            BudgetVersion draft = await db.BudgetVersions.Include(v => v.Lines).SingleAsync(v => v.Status == BudgetStatus.Draft);
            draft.UpdateLineAmount(draft.Lines.First().Id, 999_999m);
            await db.SaveChangesAsync();
        }

        await using (CivicBudgetDbContext db = database.CreateContext(tenant: null))
        {
            var user = await db.Users.SingleAsync(u => u.Email == "viewer@mapleridge.example");
            user.DisplayName = "Vandal";
            await db.SaveChangesAsync();
        }

        await DatabaseInitializer.ResetAsync(database.Services);

        await using (CivicBudgetDbContext db = database.CreateContext(tenant: null))
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(2, await db.Governments.CountAsync());
            Assert.Equal("Council Member Lee", (await db.Users.SingleAsync(u => u.Email == "viewer@mapleridge.example")).DisplayName);
            Assert.False(await db.BudgetLines.AnyAsync(l => l.Amount == 999_999m));
            Assert.Equal(6, await db.Users.CountAsync()); // five Maple Ridge logins and one for Pine Hollow, none from before
        }
    }
}
