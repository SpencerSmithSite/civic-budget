using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Budgets.Planning;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class PlanAssumptionConfiguration : IEntityTypeConfiguration<PlanAssumption>
{
    public void Configure(EntityTypeBuilder<PlanAssumption> builder)
    {
        builder.ToTable("PlanAssumptions");
        builder.Property(a => a.RevenuePercent).HasPrecision(9, 4);
        builder.Property(a => a.ExpenditurePercent).HasPrecision(9, 4);
        builder.HasIndex(a => new { a.BudgetVersionId, a.YearOffset }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(a => a.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlannedAmountConfiguration : IEntityTypeConfiguration<PlannedAmount>
{
    public void Configure(EntityTypeBuilder<PlannedAmount> builder)
    {
        builder.ToTable("PlannedAmounts");
        builder.HasIndex(p => new { p.BudgetLineId, p.YearOffset }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
