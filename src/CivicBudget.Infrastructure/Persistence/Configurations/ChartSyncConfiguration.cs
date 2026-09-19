using CivicBudget.Domain.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class ChartSyncConfiguration : IEntityTypeConfiguration<ChartSync>
{
    public void Configure(EntityTypeBuilder<ChartSync> builder)
    {
        builder.Property(s => s.SourceName).HasMaxLength(ChartSync.NameMaxLength);
        builder.Property(s => s.FileName).HasMaxLength(ChartSync.NameMaxLength);
        builder.Property(s => s.UserId).HasMaxLength(450);
        builder.Property(s => s.UserName).HasMaxLength(ChartSync.NameMaxLength);
        builder.HasIndex(s => new { s.GovernmentId, s.SyncedAtUtc });
    }
}
