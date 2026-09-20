using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

/// <summary>Shared shape for both contexts; the admin context adds the foreign key below.</summary>
public static class GovernmentLogoModel
{
    public static void Configure(ModelBuilder builder) =>
        builder.Entity<GovernmentLogo>(logo =>
        {
            logo.ToTable("GovernmentLogos");
            logo.HasKey(l => l.GovernmentId);
            logo.Property(l => l.ContentType).HasMaxLength(64);
            logo.Property(l => l.Data).HasMaxLength(GovernmentLogo.MaxBytes);
        });
}

internal sealed class GovernmentLogoAdminConfiguration : IEntityTypeConfiguration<GovernmentLogo>
{
    public void Configure(EntityTypeBuilder<GovernmentLogo> builder) =>
        builder.HasOne<Government>().WithOne().HasForeignKey<GovernmentLogo>(l => l.GovernmentId).OnDelete(DeleteBehavior.Cascade);
}
