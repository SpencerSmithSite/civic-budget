using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.IntegrationTests;

[Collection(SqlServerTests.Name)]
public class MigrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migrations_apply_and_leave_nothing_pending()
    {
        TestDatabase database = await fixture.CreateDatabaseAsync("CivicBudget_Migrations");
        await using CivicBudgetDbContext db = database.CreateContext(tenant: null);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Model_snapshot_matches_the_current_model()
    {
        // Fails when someone changes an entity or configuration and forgets `dotnet ef migrations add`.
        TestDatabase database = await fixture.CreateDatabaseAsync("CivicBudget_Migrations");
        await using CivicBudgetDbContext db = database.CreateContext(tenant: null);

        Assert.False(db.Database.HasPendingModelChanges(), "Model has changes not captured in a migration.");
    }

    [Fact]
    public async Task Every_decimal_column_is_decimal_18_2()
    {
        TestDatabase database = await fixture.CreateDatabaseAsync("CivicBudget_Migrations");
        await using CivicBudgetDbContext db = database.CreateContext(tenant: null);

        List<string> offenders = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT TABLE_NAME + '.' + COLUMN_NAME AS [Value]
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE DATA_TYPE IN ('decimal','numeric','float','real','money','smallmoney')
                  AND NOT (DATA_TYPE = 'decimal' AND NUMERIC_PRECISION = 18 AND NUMERIC_SCALE = 2)
                """)
            .ToListAsync();

        Assert.Empty(offenders);
    }
}
