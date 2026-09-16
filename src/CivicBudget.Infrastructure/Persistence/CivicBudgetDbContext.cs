using System.Reflection;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// The admin-side model. Owns the migrations.
/// <para>
/// Always obtained through <c>IDbContextFactory&lt;CivicBudgetDbContext&gt;</c>. In Blazor Server
/// the DI scope is the circuit (the browser tab), so a scoped DbContext would live for hours and be
/// shared by concurrent event handlers. One context per unit of work avoids both problems.
/// </para>
/// </summary>
public sealed class CivicBudgetDbContext(DbContextOptions<CivicBudgetDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<Government> Governments => Set<Government>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<FundBeginningBalance> FundBeginningBalances => Set<FundBeginningBalance>();

    /// <summary>
    /// Read by the query filters. Must be an instance member so EF Core treats it as a parameter
    /// evaluated per query, not a constant baked into the compiled model.
    /// </summary>
    private Guid? CurrentGovernmentId => tenantContext.GovernmentId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is decimal(18,2) everywhere. A convention means a new decimal property can't be
        // accidentally mapped as decimal(18,2)-by-default-with-a-warning or, worse, float.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Adds <c>WHERE GovernmentId = @currentTenant</c> to every query of every entity that implements
    /// <see cref="ITenantOwned"/>. Done by reflection over the model so a new tenant-owned entity is
    /// covered automatically; an integration test asserts that no such entity is left unfiltered.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        MethodInfo applyFilter = typeof(CivicBudgetDbContext)
            .GetMethod(nameof(ApplyTenantQueryFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (Type entityType in modelBuilder.Model.GetEntityTypes()
                     .Select(e => e.ClrType)
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t)))
        {
            applyFilter.MakeGenericMethod(entityType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        // Guid == Guid? : when CurrentGovernmentId is null the comparison is false for every row,
        // so "no tenant" means "no data" rather than "all data".
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.GovernmentId == CurrentGovernmentId);
    }
}
