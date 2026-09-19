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

        // The format is a value object stored in the government's own row (five columns, no table).
        builder.OwnsOne(g => g.AccountNumberFormat, f =>
        {
            f.Property(x => x.FundWidth).HasColumnName("AccountNumberFundWidth");
            f.Property(x => x.DepartmentWidth).HasColumnName("AccountNumberDepartmentWidth");
            f.Property(x => x.ObjectWidth).HasColumnName("AccountNumberObjectWidth");
            f.Property(x => x.Separator).HasColumnName("AccountNumberSeparator").HasMaxLength(1);
            f.Property(x => x.DepartmentLabel).HasColumnName("AccountNumberDepartmentLabel").HasMaxLength(20);
        });
        builder.Navigation(g => g.AccountNumberFormat).IsRequired();
    }
}
