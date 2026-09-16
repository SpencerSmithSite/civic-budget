using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class GovernmentConfiguration : IEntityTypeConfiguration<Government>
{
    public void Configure(EntityTypeBuilder<Government> builder)
    {
        builder.Property(g => g.Name).HasMaxLength(Government.NameMaxLength);
        builder.Property(g => g.State).HasMaxLength(2).IsFixedLength();
        builder.Property(g => g.PublicSlug).HasMaxLength(Government.SlugMaxLength);
        builder.HasIndex(g => g.PublicSlug).IsUnique();
    }
}
