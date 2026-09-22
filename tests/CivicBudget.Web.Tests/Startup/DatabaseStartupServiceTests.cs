using System.Diagnostics;
using CivicBudget.Web;
using CivicBudget.Web.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CivicBudget.Web.Tests.Startup;

/// <summary>
/// The point of the hosted service is that the host gets to start Kestrel while the database is
/// still waking, so starting it must never hold up host startup however long the database takes.
/// The host satisfies this today by running ExecuteAsync off the startup path; the test states the
/// requirement so a future rewrite into a plain IHostedService cannot quietly undo it.
/// </summary>
public class DatabaseStartupServiceTests
{
    [Fact]
    public async Task Starting_it_does_not_wait_for_the_database()
    {
        using var reachedDatabase = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // Stands in for SqlConnection.Open against a database that is resuming: it blocks its thread.
        var blockingServices = new BlockingServiceProvider(() =>
        {
            reachedDatabase.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        var lifetime = new RecordingLifetime();
        DatabaseStartupService service = Create(blockingServices, lifetime);

        var stopwatch = Stopwatch.StartNew();
        await service.StartAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(reachedDatabase.Wait(TimeSpan.FromSeconds(5)), "the service never got as far as the database");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"StartAsync blocked the host for {stopwatch.Elapsed}");
        release.Set();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failure_stops_the_host_so_the_container_restarts()
    {
        var lifetime = new RecordingLifetime();
        DatabaseStartupService service = Create(new BlockingServiceProvider(() => throw new InvalidOperationException("no database")), lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.True(lifetime.Stopped);
    }

    private static DatabaseStartupService Create(IServiceProvider services, RecordingLifetime lifetime) =>
        new(services,
            Options.Create(new DatabaseOptions { MigrateOnStartup = true }),
            new StubEnvironment(),
            lifetime,
            new StartupState(new FakeTimeProvider()),
            NullLogger<DatabaseStartupService>.Instance);

    /// <summary>Runs <paramref name="onResolve"/> when the initializer asks for its first service.</summary>
    private sealed class BlockingServiceProvider(Action onResolve) : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            onResolve();
            return null;
        }

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "CivicBudget.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public bool Stopped { get; private set; }

        public void StopApplication() => Stopped = true;
    }
}
