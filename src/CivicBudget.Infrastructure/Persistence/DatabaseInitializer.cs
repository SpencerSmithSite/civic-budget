using CivicBudget.Infrastructure.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// Startup helper: wait for SQL Server, apply pending migrations, then seed. Development does both
/// on every start; the demo deploys opt in (DatabaseOptions); a real pipeline would run migrations
/// as its own step. <see cref="ResetAsync"/> is the nightly demo reset: everything dropped and
/// rebuilt from the seed so visitors always find the two fictional governments as designed.
/// </summary>
public static class DatabaseInitializer
{
    // `docker compose up` returns before SQL Server can accept logins (15 to 30 seconds under Rosetta),
    // and a serverless Azure SQL database resumes from auto-pause in about a minute. EF's retry
    // strategy does not cover the pre-login handshake error either produces.
    private const int MaxAttempts = 24;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>();
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int pending = await WaitForSqlServerAsync(db, logger, ct);
        logger.LogInformation("Applying {Count} pending migration(s).", pending);
        await db.Database.MigrateAsync(ct);
    }

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(ct);
    }

    /// <summary>
    /// Drops every table (foreign keys first, then tables, so order does not matter), then migrates
    /// from nothing and seeds. The database itself is kept: on Azure it is the billable resource,
    /// and the free offer is tied to it. Every session ends with it, including the Data Protection
    /// keys, which is right for a demo that starts each day clean.
    /// </summary>
    public static async Task ResetAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>();

        await using (CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct))
        {
            await WaitForSqlServerAsync(db, logger, ct);
            logger.LogWarning("Resetting the database: dropping every table, then migrating and seeding.");
            await db.Database.ExecuteSqlRawAsync(DropEverythingSql, ct);
        }

        await MigrateAsync(services, ct);
        await SeedAsync(services, ct);
        logger.LogInformation("Database reset complete.");
    }

    // Two passes because a table cannot be dropped while another table's foreign key points at it.
    private const string DropEverythingSql = """
        DECLARE @sql NVARCHAR(MAX) = N'';
        SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON fk.parent_object_id = t.object_id
        JOIN sys.schemas s ON t.schema_id = s.schema_id;
        EXEC sp_executesql @sql;

        SET @sql = N'';
        SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
        FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id = s.schema_id;
        EXEC sp_executesql @sql;
        """;

    /// <summary>First real round trip to the server, retried while SQL Server is still starting. Returns the pending migration count.</summary>
    private static async Task<int> WaitForSqlServerAsync(CivicBudgetDbContext db, ILogger logger, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return (await db.Database.GetPendingMigrationsAsync(ct)).Count();
            }
            catch (SqlException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning("SQL Server is not ready yet (attempt {Attempt}/{Max}: {Message}). Retrying in {Delay}s.",
                    attempt, MaxAttempts, ex.Message.Split('\n')[0], RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }
}
