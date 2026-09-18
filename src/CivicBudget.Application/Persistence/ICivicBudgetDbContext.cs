using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Publishing;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Persistence;

/// <summary>
/// What the Application layer is allowed to see of the database: the domain DbSets and SaveChanges.
/// Infrastructure's <c>CivicBudgetDbContext</c> implements it. Identity tables, migrations, and the
/// SQL Server provider are deliberately not part of this interface. See ADR-0014 for why this is
/// preferred over one repository class per entity.
/// </summary>
public interface ICivicBudgetDbContext : IAsyncDisposable, IDisposable
{
    DbSet<Government> Governments { get; }
    DbSet<Fund> Funds { get; }
    DbSet<Department> Departments { get; }
    DbSet<Account> Accounts { get; }
    DbSet<FiscalYear> FiscalYears { get; }
    DbSet<BudgetVersion> BudgetVersions { get; }
    DbSet<BudgetLine> BudgetLines { get; }
    DbSet<FundBeginningBalance> FundBeginningBalances { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<PublishedBudgetSnapshot> PublishedBudgetSnapshots { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Hands out short-lived contexts, one per unit of work. This is the Application-layer face of
/// <c>IDbContextFactory&lt;CivicBudgetDbContext&gt;</c>; see ADR-0003 for why a factory and not a
/// scoped context.
/// </summary>
public interface ICivicBudgetDbContextFactory
{
    Task<ICivicBudgetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
