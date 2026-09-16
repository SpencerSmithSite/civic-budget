using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class BudgetVersionConfiguration : IEntityTypeConfiguration<BudgetVersion>
{
    public void Configure(EntityTypeBuilder<BudgetVersion> builder)
    {
        builder.Property(v => v.AmendmentReason).HasMaxLength(BudgetVersion.ReasonMaxLength);
        builder.Property(v => v.ResolutionNumber).HasMaxLength(BudgetVersion.ResolutionNumberMaxLength);
        builder.Property(v => v.AdoptedByUserId).HasMaxLength(450); // matches ASP.NET Core Identity's key length
        builder.Ignore(v => v.IsAmendment);
        builder.Ignore(v => v.IsEditable);
        builder.Ignore(v => v.Label);

        builder.HasIndex(v => new { v.FiscalYearId, v.VersionNumber }).IsUnique();

        builder.HasOne<FiscalYear>().WithMany().HasForeignKey(v => v.FiscalYearId).OnDelete(DeleteBehavior.Cascade);
        // Restrict here: the version already cascades from its fiscal year, and SQL Server rejects
        // a second cascade path to the same table.
        builder.HasOne<Government>().WithMany().HasForeignKey(v => v.GovernmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BudgetVersion>().WithMany().HasForeignKey(v => v.SupersededByVersionId).OnDelete(DeleteBehavior.Restrict);

        // The aggregate exposes read-only collections backed by private lists; EF writes to the fields.
        builder.HasMany(v => v.Lines).WithOne().HasForeignKey(l => l.BudgetVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");

        builder.HasMany(v => v.BeginningBalances).WithOne().HasForeignKey(b => b.BudgetVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.BeginningBalances).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_beginningBalances");
    }
}
