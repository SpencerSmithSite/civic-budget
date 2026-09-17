using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

/// <summary>
/// Snapshot tables are mapped by two contexts: the admin context (which writes them and owns the
/// migration) and the read-only portal context. The shared shape lives here so the two can never
/// disagree; the admin context adds the foreign key to Governments, which the portal does not map.
/// </summary>
public static class PublishedSnapshotModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<PublishedBudgetSnapshot>(snapshot =>
        {
            snapshot.ToTable("PublishedBudgetSnapshots");
            snapshot.Property(s => s.GovernmentSlug).HasMaxLength(Government.SlugMaxLength);
            snapshot.Property(s => s.GovernmentName).HasMaxLength(Government.NameMaxLength);
            snapshot.Property(s => s.VersionLabel).HasMaxLength(50);
            snapshot.Property(s => s.AmendmentReason).HasMaxLength(Domain.Budgets.BudgetVersion.ReasonMaxLength);
            snapshot.Property(s => s.ResolutionNumber).HasMaxLength(Domain.Budgets.BudgetVersion.ResolutionNumberMaxLength);
            snapshot.Property(s => s.PublishedByUserId).HasMaxLength(450);
            snapshot.Property(s => s.PublishedByUserName).HasMaxLength(256);
            snapshot.Property(s => s.StatusChangedByUserId).HasMaxLength(450);
            snapshot.Ignore(s => s.IsActive);

            // The portal's main lookup: "the active snapshot for this slug and year".
            snapshot.HasIndex(s => new { s.GovernmentSlug, s.FiscalYear, s.Status });

            snapshot.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            snapshot.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
            snapshot.HasMany(s => s.Funds).WithOne().HasForeignKey(f => f.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            snapshot.Navigation(s => s.Funds).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_funds");
        });

        builder.Entity<PublishedBudgetSnapshotLine>(line =>
        {
            line.ToTable("PublishedBudgetSnapshotLines");
            line.Property(l => l.FundCode).HasMaxLength(20);
            line.Property(l => l.FundName).HasMaxLength(150);
            line.Property(l => l.DepartmentCode).HasMaxLength(20);
            line.Property(l => l.DepartmentName).HasMaxLength(150);
            line.Property(l => l.AccountCode).HasMaxLength(20);
            line.Property(l => l.AccountName).HasMaxLength(150);
            line.HasIndex(l => new { l.SnapshotId, l.FundCode, l.DepartmentCode, l.AccountCode });
        });

        builder.Entity<PublishedBudgetSnapshotFund>(fund =>
        {
            fund.ToTable("PublishedBudgetSnapshotFunds");
            fund.Property(f => f.Code).HasMaxLength(20);
            fund.Property(f => f.Name).HasMaxLength(150);
            fund.HasIndex(f => new { f.SnapshotId, f.Code }).IsUnique();
        });
    }
}

/// <summary>Admin-context additions: the tenant foreign key. Picked up by the assembly scan.</summary>
internal sealed class PublishedSnapshotAdminConfiguration : IEntityTypeConfiguration<PublishedBudgetSnapshot>
{
    public void Configure(EntityTypeBuilder<PublishedBudgetSnapshot> builder) =>
        builder.HasOne<Government>().WithMany().HasForeignKey(s => s.GovernmentId).OnDelete(DeleteBehavior.Cascade);
}

internal sealed class PublishedSnapshotLineAdminConfiguration : IEntityTypeConfiguration<PublishedBudgetSnapshotLine>
{
    public void Configure(EntityTypeBuilder<PublishedBudgetSnapshotLine> builder) =>
        builder.HasOne<Government>().WithMany().HasForeignKey(l => l.GovernmentId).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class PublishedSnapshotFundAdminConfiguration : IEntityTypeConfiguration<PublishedBudgetSnapshotFund>
{
    public void Configure(EntityTypeBuilder<PublishedBudgetSnapshotFund> builder) =>
        builder.HasOne<Government>().WithMany().HasForeignKey(f => f.GovernmentId).OnDelete(DeleteBehavior.Restrict);
}
