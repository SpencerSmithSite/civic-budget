using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// Development-time startup helper: apply pending migrations, then seed. Production deployments run
/// migrations as an explicit deploy step instead (see Phase 7), never implicitly on app start.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>();
        await using (CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct))
        {
            int pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
            logger.LogInformation("Applying {Count} pending migration(s).", pending);
            await db.Database.MigrateAsync(ct);
        }

        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(ct);
    }
}
