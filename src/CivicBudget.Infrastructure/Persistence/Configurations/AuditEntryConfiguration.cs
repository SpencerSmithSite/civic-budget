using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.Property(a => a.EntityName).HasMaxLength(AuditEntry.EntityNameMaxLength);
        builder.Property(a => a.PropertyName).HasMaxLength(AuditEntry.PropertyNameMaxLength);
        builder.Property(a => a.OldValue).HasMaxLength(AuditEntry.ValueMaxLength);
        builder.Property(a => a.NewValue).HasMaxLength(AuditEntry.ValueMaxLength);
        builder.Property(a => a.Description).HasMaxLength(AuditEntry.DescriptionMaxLength);
        builder.Property(a => a.UserId).HasMaxLength(AuditEntry.UserIdMaxLength);
        builder.Property(a => a.UserName).HasMaxLength(AuditEntry.UserNameMaxLength);

        // The history screen asks "everything about this entity, newest first".
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.TimestampUtc });
        builder.HasOne<Government>().WithMany().HasForeignKey(a => a.GovernmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
