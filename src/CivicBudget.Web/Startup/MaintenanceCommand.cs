using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;

namespace CivicBudget.Web.Startup;

/// <summary>
/// <c>dotnet CivicBudget.Web.dll --maintenance</c>: the daily retention job. It removes security
/// events and finished emails older than a year (<see cref="RetentionService"/>), then exits. It is
/// scheduled by the platform like the demo's nightly reset, so the app itself never wakes the
/// database on a timer.
/// </summary>
public static class MaintenanceCommand
{
    public const string Flag = "--maintenance";

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await DatabaseInitializer.MigrateAsync(services);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        (int events, int emails) = await scope.ServiceProvider.GetRequiredService<RetentionService>().PurgeAsync();
        Console.WriteLine($"Retention: removed {events} security events and {emails} emails older than {RetentionService.KeepFor.TotalDays:0} days.");
        return 0;
    }
}
