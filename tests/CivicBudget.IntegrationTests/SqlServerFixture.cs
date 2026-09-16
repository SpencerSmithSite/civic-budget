using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Persistence.Interceptors;
using CivicBudget.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

/// <summary>Creates DbContexts against one database with an explicit tenant, mirroring production wiring.</summary>
public sealed class TestDatabase(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public CivicBudgetDbContext CreateContext(Guid? tenant)
    {
        var tenantContext = new AmbientTenantContext();
        if (tenant is { } id)
        {
            tenantContext.Set(id);
        }

        return CreateContext(tenantContext);
    }

    public CivicBudgetDbContext CreateContext(AmbientTenantContext tenantContext)
    {
        DbContextOptions<CivicBudgetDbContext> options = new DbContextOptionsBuilder<CivicBudgetDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantSaveChangesInterceptor(tenantContext))
            .Options;

        return new CivicBudgetDbContext(options, tenantContext);
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTests : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
