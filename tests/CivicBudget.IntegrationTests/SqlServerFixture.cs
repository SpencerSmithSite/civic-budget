using System.Security.Claims;
using CivicBudget.Application;
using CivicBudget.Application.Security;
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
public sealed class SqlServerFixture : IAsyncLifetime, IDisposable
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    void IDisposable.Dispose() => _templateGate.Dispose();

    public async Task DisposeAsync()
    {
        _templateGate.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Creates (or migrates) a database and returns a helper bound to it.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync(string name)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name };
        var database = new TestDatabase(builder.ConnectionString);

        await using CivicBudgetDbContext db = database.CreateContext(tenant: null);
        await db.Database.MigrateAsync();
        return database;
    }

    // Most tests want the seeded demo data. Migrating and seeding (six users' password hashes
    // included) for every test was most of the run; instead one template is built per run, backed up
    // inside the container, and restored under each test's own name, which takes a fraction of it.
    private const string TemplateName = "CivicBudget_Template";
    private const string TemplateBackup = "/var/opt/mssql/data/civicbudget_template.bak";
    private readonly SemaphoreSlim _templateGate = new(1, 1);
    private bool _templateReady;

    /// <summary>
    /// A fresh database holding the migrated, seeded demo data, as if the seeder had just run. The
    /// name gets a unique suffix: xUnit makes a new class instance per test, and restoring over a
    /// database the previous test's pooled connections still hold would fail.
    /// </summary>
    public async Task<TestDatabase> CreateSeededDatabaseAsync(string prefix)
    {
        string name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 100)];
        await EnsureTemplateAsync();
        await using SqlConnection master = await OpenMasterAsync();
        var files = new List<(string Logical, string Type)>();
        await using (SqlCommand list = new($"RESTORE FILELISTONLY FROM DISK = N'{TemplateBackup}'", master))
        await using (SqlDataReader reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                files.Add((reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
            }
        }

        string moves = string.Join(", ", files.Select(f => $"MOVE N'{f.Logical}' TO N'/var/opt/mssql/data/{name}{(f.Type == "L" ? "_log.ldf" : ".mdf")}'"));
        await using (SqlCommand restore = new($"RESTORE DATABASE [{name}] FROM DISK = N'{TemplateBackup}' WITH {moves}, REPLACE", master))
        {
            restore.CommandTimeout = 120;
            await restore.ExecuteNonQueryAsync();
        }

        return new TestDatabase(new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString);
    }

    private async Task EnsureTemplateAsync()
    {
        await _templateGate.WaitAsync();
        try
        {
            if (_templateReady)
            {
                return;
            }

            TestDatabase template = await CreateDatabaseAsync(TemplateName);
            await using (AsyncServiceScope scope = template.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
            }

            SqlConnection.ClearAllPools(); // nothing may hold the template open while it is backed up
            await using SqlConnection master = await OpenMasterAsync();
            await using SqlCommand backup = new($"BACKUP DATABASE [{TemplateName}] TO DISK = N'{TemplateBackup}' WITH INIT, COPY_ONLY", master);
            backup.CommandTimeout = 120;
            await backup.ExecuteNonQueryAsync();
            _templateReady = true;
        }
        finally
        {
            _templateGate.Release();
        }
    }

    private async Task<SqlConnection> OpenMasterAsync()
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        return connection;
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

    /// <summary>A scope signed in as a test user holding one role in one government.</summary>
    public AsyncServiceScope CreateScopeAs(string role, Guid governmentId)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, governmentId.ToString()));
        return CreateScope(user: new ClaimsPrincipal(identity));
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTests : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
