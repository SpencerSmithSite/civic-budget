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

        // One line per fund/department/account within a version. On SQL Server, EF Core gives a unique
        // index over a nullable column the filter "[DepartmentId] IS NOT NULL", so this one only covers
        // lines that have a department.
        builder.HasIndex(l => new { l.BudgetVersionId, l.FundId, l.DepartmentId, l.AccountId }).IsUnique();

        // Fund-level lines (revenue and transfers with no department) therefore need their own index.
        // Without it two users adding the same revenue line at once, or an import racing a manual add,
        // would count that revenue twice and break every later import.
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
