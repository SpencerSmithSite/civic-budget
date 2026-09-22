using CivicBudget.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace CivicBudget.Web.Startup;

/// <summary>
/// Migrates and seeds in the background after the host starts listening, then flips
/// <see cref="StartupState"/> to ready. Development always does both; elsewhere each is opt-in
/// (<see cref="DatabaseOptions"/>). A failure is fatal on purpose: the process exits, the platform
/// restarts the container, and the next attempt starts from scratch, which is exactly what the
/// old inline startup did by throwing before the host ran.
/// </summary>
public sealed class DatabaseStartupService(
    IServiceProvider services,
    IOptions<DatabaseOptions> options,
    IHostEnvironment environment,
    IHostApplicationLifetime lifetime,
    StartupState state,
    ILogger<DatabaseStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            bool development = environment.IsDevelopment();
            if (development || options.Value.MigrateOnStartup)
            {
                await DatabaseInitializer.MigrateAsync(services, stoppingToken);
            }

            if (development || options.Value.SeedDemoData)
            {
                await DatabaseInitializer.SeedAsync(services, stoppingToken);
            }

            state.MarkReady();
            logger.LogInformation("Database ready after {Seconds}s; serving pages.", state.ElapsedSeconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down before the database came up: nothing to report.
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The database could not be prepared; stopping so the container restarts.");
            lifetime.StopApplication();
        }
    }
}
