using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class FundBeginningBalanceConfiguration : IEntityTypeConfiguration<FundBeginningBalance>
{
    public void Configure(EntityTypeBuilder<FundBeginningBalance> builder)
    {
        builder.HasIndex(b => new { b.BudgetVersionId, b.FundId }).IsUnique();
        builder.HasOne<Fund>().WithMany().HasForeignKey(b => b.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(b => b.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
