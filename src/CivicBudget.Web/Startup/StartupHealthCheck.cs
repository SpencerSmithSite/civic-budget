using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CivicBudget.Web.Startup;

/// <summary>
/// Healthy once migrations and seeding have finished. It answers from memory, so the waiting page
/// can poll it every couple of seconds without opening a database connection each time; the
/// readiness endpoint pairs it with the real database round trip.
/// </summary>
public sealed class StartupHealthCheck(StartupState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("Database ready.")
            : HealthCheckResult.Unhealthy($"Preparing the database ({state.ElapsedSeconds}s)."));
}
