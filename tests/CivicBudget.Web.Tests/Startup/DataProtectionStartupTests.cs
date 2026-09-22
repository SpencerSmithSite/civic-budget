using CivicBudget.Web.Startup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CivicBudget.Web.Tests.Startup;

/// <summary>
/// Data Protection's startup key-ring read is what kept Kestrel from listening while the demo's
/// database resumed. Removing it means matching an internal framework type by name, so the first
/// test fails loudly if a future .NET renames or drops it rather than letting the delay return.
/// </summary>
public class DataProtectionStartupTests
{
    [Fact]
    public void Removes_the_startup_key_ring_read()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));

        bool removed = services.DeferKeyRingLoad();

        Assert.True(removed, "Data Protection no longer registers the hosted service this looks for; check what loads the key ring now.");
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Leaves_other_hosted_services_alone()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddHostedService<OtherService>();

        services.DeferKeyRingLoad();

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(services, d => d.ImplementationType == typeof(OtherService));
    }

    private sealed class OtherService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
