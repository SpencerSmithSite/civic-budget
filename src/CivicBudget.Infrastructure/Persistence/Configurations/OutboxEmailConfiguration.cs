using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        builder.Property(e => e.ToAddress).HasMaxLength(OutboxEmail.AddressMaxLength);
        builder.Property(e => e.ToName).HasMaxLength(OutboxEmail.NameMaxLength);
        builder.Property(e => e.Subject).HasMaxLength(OutboxEmail.SubjectMaxLength);
        builder.Property(e => e.Body).HasMaxLength(OutboxEmail.BodyMaxLength);
        builder.Property(e => e.LastError).HasMaxLength(OutboxEmail.ErrorMaxLength);

        // The sender looks for pending mail across governments; the outbox page lists a government's newest first.
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.GovernmentId, e.CreatedAtUtc });
        builder.HasOne<Government>().WithMany().HasForeignKey(e => e.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
