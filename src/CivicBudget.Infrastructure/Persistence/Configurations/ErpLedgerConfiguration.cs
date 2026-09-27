using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

// The ERP's figures reference the chart like budget lines do, and for the same reason with Restrict:
// a code with history is retired, never deleted. Each table is read by government and fiscal year
// (a sync replaces a year; screens sum a year), so that pair leads every index.

internal sealed class ErpActualConfiguration : IEntityTypeConfiguration<ErpActual>
{
    public void Configure(EntityTypeBuilder<ErpActual> builder)
    {
        builder.HasIndex(a => new { a.GovernmentId, a.FiscalYear, a.FundId, a.DepartmentId, a.AccountId, a.Period });
        builder.HasOne<Fund>().WithMany().HasForeignKey(a => a.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(a => a.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(a => a.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ErpEncumbranceConfiguration : IEntityTypeConfiguration<ErpEncumbrance>
{
    public void Configure(EntityTypeBuilder<ErpEncumbrance> builder)
    {
        builder.HasIndex(e => new { e.GovernmentId, e.FiscalYear, e.FundId, e.DepartmentId, e.AccountId });
        builder.HasOne<Fund>().WithMany().HasForeignKey(e => e.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(e => e.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ErpFundCashConfiguration : IEntityTypeConfiguration<ErpFundCash>
{
    public void Configure(EntityTypeBuilder<ErpFundCash> builder)
    {
        builder.ToTable("ErpFundCash");
        builder.HasIndex(c => new { c.GovernmentId, c.FiscalYear, c.FundId }).IsUnique();
        builder.HasOne<Fund>().WithMany().HasForeignKey(c => c.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(c => c.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActualsSyncConfiguration : IEntityTypeConfiguration<ActualsSync>
{
    public void Configure(EntityTypeBuilder<ActualsSync> builder)
    {
        builder.Property(s => s.SourceName).HasMaxLength(ActualsSync.NameMaxLength);
        builder.Property(s => s.FileName).HasMaxLength(ActualsSync.NameMaxLength);
        builder.Property(s => s.UserId).HasMaxLength(450);
        builder.Property(s => s.UserName).HasMaxLength(ActualsSync.NameMaxLength);
        builder.Ignore(s => s.IsYearClosed);
        builder.HasIndex(s => new { s.GovernmentId, s.FiscalYear, s.SyncedAtUtc });
    }
}
