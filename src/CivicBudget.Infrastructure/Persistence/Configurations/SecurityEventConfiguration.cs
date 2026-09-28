using CivicBudget.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.Property(e => e.UserId).HasMaxLength(450); // matches ASP.NET Core Identity's key length
        builder.Property(e => e.Email).HasMaxLength(SecurityEvent.EmailMaxLength);
        builder.Property(e => e.IpAddress).HasMaxLength(SecurityEvent.AddressMaxLength);
        builder.Property(e => e.Detail).HasMaxLength(SecurityEvent.DetailMaxLength);

        // A government's log newest first; the retention job deletes by age; failures are counted by address.
        builder.HasIndex(e => new { e.GovernmentId, e.OccurredAtUtc });
        builder.HasIndex(e => e.OccurredAtUtc);

        // No foreign key to Governments: an event may have none, and off-boarding a government removes
        // its events explicitly, so a key would only add a delete order to get wrong.
    }
}
