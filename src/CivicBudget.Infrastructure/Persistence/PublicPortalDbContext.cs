using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// The public portal's only door to the database (ADR-0006). It maps the four snapshot tables and
/// nothing else: no budget lines, no users, no governments. A global query filter hides every
/// snapshot that is not <see cref="SnapshotStatus.Active"/>, and SaveChanges throws. So a bug in
/// the portal cannot show a draft, a withdrawn budget, or another tenant's data, and cannot write.
/// The admin context owns the migrations; this context just reads the same tables.
/// </summary>
public sealed class PublicPortalDbContext(DbContextOptions<PublicPortalDbContext> options) : DbContext(options)
{
    private const string ReadOnlyMessage = "PublicPortalDbContext is read-only. Publishing happens through the admin context.";

    public DbSet<PublishedBudgetSnapshot> Snapshots => Set<PublishedBudgetSnapshot>();
    public DbSet<PublishedBudgetSnapshotLine> SnapshotLines => Set<PublishedBudgetSnapshotLine>();
    public DbSet<PublishedBudgetSnapshotFund> SnapshotFunds => Set<PublishedBudgetSnapshotFund>();
    public DbSet<PublishedBudgetSnapshotDepartment> SnapshotDepartments => Set<PublishedBudgetSnapshotDepartment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        PublishedSnapshotModel.Configure(modelBuilder);

        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            modelBuilder.Entity(entityType.ClrType).Property("Id").ValueGeneratedNever();
        }

        // Only what citizens may see: active snapshots, and the lines and funds that belong to one.
        modelBuilder.Entity<PublishedBudgetSnapshot>().HasQueryFilter(s => s.Status == SnapshotStatus.Active);
        modelBuilder.Entity<PublishedBudgetSnapshotLine>().HasQueryFilter(l => Snapshots.Any(s => s.Id == l.SnapshotId && s.Status == SnapshotStatus.Active));
        modelBuilder.Entity<PublishedBudgetSnapshotFund>().HasQueryFilter(f => Snapshots.Any(s => s.Id == f.SnapshotId && s.Status == SnapshotStatus.Active));
        modelBuilder.Entity<PublishedBudgetSnapshotDepartment>().HasQueryFilter(d => Snapshots.Any(s => s.Id == d.SnapshotId && s.Status == SnapshotStatus.Active));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException(ReadOnlyMessage);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(ReadOnlyMessage);
}
