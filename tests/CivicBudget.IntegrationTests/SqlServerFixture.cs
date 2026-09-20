using System.Security.Claims;
using CivicBudget.Application;
using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Persistence.Interceptors;
using CivicBudget.Infrastructure.Security;
using CivicBudget.Infrastructure.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// One SQL Server 2022 container for the whole test run (same image as docker-compose). Each test
/// class gets its own database on that server so classes can't see each other's rows.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates (or migrates) a database and returns a helper bound to it.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync(string name)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name };
        var database = new TestDatabase(builder.ConnectionString);

        await using CivicBudgetDbContext db = database.CreateContext(tenant: null);
        await db.Database.MigrateAsync();
        return database;
    }
}

/// <summary>
/// Two ways to talk to one test database:
/// <list type="bullet">
/// <item><see cref="CreateContext(Guid?)"/> builds a DbContext by hand with an explicit tenant, for tests about the context itself.</item>
/// <item><see cref="CreateScope"/> resolves from a real service provider wired with <c>AddApplication</c> and
/// <c>AddInfrastructure</c>, for tests about services, Identity, and seeding. Same registrations as Program.cs.</item>
/// </list>
/// </summary>
public sealed class TestDatabase
{
    public const string DemoPassword = "Demo-Password-2027!";

    private readonly ServiceProvider _provider;

    public TestDatabase(string connectionString)
    {
        ConnectionString = connectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection(); // Identity's token providers need it; the web host registers it automatically
        services.AddApplication();
        services.AddInfrastructure(connectionString);
        services.Configure<SeedOptions>(o => o.DemoPassword = DemoPassword);
        _provider = services.BuildServiceProvider();
    }

    public string ConnectionString { get; }

    /// <summary>The root provider, for helpers that take an <see cref="IServiceProvider"/> such as <c>DatabaseInitializer</c>.</summary>
    public IServiceProvider Services => _provider;

    public CivicBudgetDbContext CreateContext(Guid? tenant)
    {
        var currentUser = new CurrentUserContext();
        if (tenant is { } id)
        {
            currentUser.SetTenant(id);
        }

        DbContextOptions<CivicBudgetDbContext> options = new DbContextOptionsBuilder<CivicBudgetDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantSaveChangesInterceptor(currentUser))
            .Options;

        return new CivicBudgetDbContext(options, currentUser);
    }

    /// <summary>A DI scope acting for the given tenant and/or user, exactly as a request or circuit scope would.</summary>
    public AsyncServiceScope CreateScope(Guid? tenant = null, ClaimsPrincipal? user = null)
    {
        AsyncServiceScope scope = _provider.CreateAsyncScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
        if (user is not null)
        {
            currentUser.SetPrincipal(user);
        }

        if (tenant is { } id)
        {
            currentUser.SetTenant(id);
        }

        return scope;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTests : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
