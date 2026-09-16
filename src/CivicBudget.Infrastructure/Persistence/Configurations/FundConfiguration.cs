using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.Property(f => f.Code).HasMaxLength(Fund.CodeMaxLength);
        builder.Property(f => f.Name).HasMaxLength(Fund.NameMaxLength);
        builder.Ignore(f => f.Group);

        // Codes are unique within a government, not globally: two villages can both have a fund "1000".
        builder.HasIndex(f => new { f.GovernmentId, f.Code }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(f => f.GovernmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
