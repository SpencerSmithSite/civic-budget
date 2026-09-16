using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(a => a.Code).HasMaxLength(Account.CodeMaxLength);
        builder.Property(a => a.Name).HasMaxLength(Account.NameMaxLength);
        builder.HasIndex(a => new { a.GovernmentId, a.Code }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(a => a.GovernmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
