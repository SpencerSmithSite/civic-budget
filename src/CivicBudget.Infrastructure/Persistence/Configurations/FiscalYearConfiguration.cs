using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> builder)
    {
        builder.Ignore(f => f.Label);
        builder.HasIndex(f => new { f.GovernmentId, f.Year }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(f => f.GovernmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
