using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> builder)
    {
        builder.Property(l => l.Justification).HasMaxLength(BudgetLine.JustificationMaxLength);
        builder.Ignore(l => l.DollarChange);
        builder.Ignore(l => l.PercentChange);

        // One line per fund/department/account within a version. SQL Server treats NULL as a value
        // in unique indexes, so a fund-only revenue line (DepartmentId NULL) is also unique per fund+account.
        builder.HasIndex(l => new { l.BudgetVersionId, l.FundId, l.DepartmentId, l.AccountId }).IsUnique();

        // SQL Server leaves NULL department ids out of the index above, so fund-level lines (revenue,
        // transfers) need their own. Without it two users adding the same revenue line at once, or an
        // import racing a manual add, would count that revenue twice and break every later import.
        builder.HasIndex(l => new { l.BudgetVersionId, l.FundId, l.AccountId })
            .IsUnique()
            .HasFilter("[DepartmentId] IS NULL")
            .HasDatabaseName("IX_BudgetLines_FundLevel_Unique");

        // Restrict: a fund, department, or account that is referenced by any budget line cannot be deleted.
        // Retire it with IsActive = false instead. Deleting history is not a feature.
        builder.HasOne(l => l.Fund).WithMany().HasForeignKey(l => l.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Department).WithMany().HasForeignKey(l => l.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Account).WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(l => l.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
