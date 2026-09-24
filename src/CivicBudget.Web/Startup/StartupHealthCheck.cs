using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CivicBudget.Web.Startup;

/// <summary>
/// Healthy once migrations and seeding have finished (<c>/health/startup</c>). It answers from memory,
/// so the waiting page can poll it every couple of seconds without opening a database connection,
/// and nobody can use it to keep the free database awake.
/// </summary>
public sealed class StartupHealthCheck(StartupState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("Database ready.")
            : HealthCheckResult.Unhealthy($"Preparing the database ({state.ElapsedSeconds}s)."));
}
