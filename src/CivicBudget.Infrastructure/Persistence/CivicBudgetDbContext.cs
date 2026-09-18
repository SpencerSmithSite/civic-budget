using System.Reflection;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// The admin-side model: domain tables plus ASP.NET Core Identity tables (the <c>IdentityDbContext</c>
/// base adds AspNetUsers, AspNetRoles, and friends). Owns the migrations. Implements
/// <see cref="ICivicBudgetDbContext"/> so Application services can query without referencing this project.
/// <para>
/// Always obtained through <c>IDbContextFactory&lt;CivicBudgetDbContext&gt;</c>. In Blazor Server
/// the DI scope is the circuit (the browser tab), so a scoped DbContext would live for hours and be
/// shared by concurrent event handlers. One context per unit of work avoids both problems.
/// </para>
/// </summary>
public sealed class CivicBudgetDbContext(DbContextOptions<CivicBudgetDbContext> options, ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser>(options), ICivicBudgetDbContext, IDataProtectionKeyContext
{
    public DbSet<Government> Governments => Set<Government>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<FundBeginningBalance> FundBeginningBalances => Set<FundBeginningBalance>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>
    /// ASP.NET Core Data Protection key ring (cookies, antiforgery tokens). Kept in the database so
    /// a container restart or a second instance does not sign everyone out; the default file
    /// store is ephemeral in a container. Not tenant-owned: one key ring serves the whole app.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<PublishedBudgetSnapshot> PublishedBudgetSnapshots => Set<PublishedBudgetSnapshot>();
    public DbSet<UserDepartment> UserDepartments => Set<UserDepartment>();

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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity's tables and keys; must run first
        Configurations.PublishedSnapshotModel.Configure(builder); // shared with PublicPortalDbContext
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        UseClientGeneratedKeys(builder);
        ApplyTenantQueryFilters(builder);
    }

    /// <summary>
    /// Every domain entity assigns its own Guid v7 id in its constructor. Telling EF Core the key is
    /// never store-generated matters for aggregates: when a new BudgetLine is discovered through
    /// BudgetVersion.Lines, EF decides Added vs Modified by asking "is a generated key already set?".
    /// With the default (generated) it would answer "set, so it must exist" and issue an UPDATE that
    /// affects zero rows. With ValueGeneratedNever it tracks the new child as Added.
    /// </summary>
    private static void UseClientGeneratedKeys(ModelBuilder builder)
    {
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in builder.Model.GetEntityTypes()
                     .Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
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
