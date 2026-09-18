using CivicBudget.Infrastructure.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// Development-time startup helper: wait for SQL Server, apply pending migrations, then seed.
/// Production deployments run migrations as an explicit deploy step instead (see Phase 7), never
/// implicitly on app start.
/// </summary>
public static class DatabaseInitializer
{
    // `docker compose up` returns before SQL Server can accept logins (it takes 15 to 30 seconds under
    // Rosetta), and EF's retry strategy does not cover the pre-login handshake error that produces.
    private const int MaxAttempts = 12;
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
