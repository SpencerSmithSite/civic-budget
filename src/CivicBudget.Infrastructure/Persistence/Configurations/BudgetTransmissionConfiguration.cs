using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class BudgetTransmissionConfiguration : IEntityTypeConfiguration<BudgetTransmission>
{
    public void Configure(EntityTypeBuilder<BudgetTransmission> builder)
    {
        builder.Property(t => t.TargetName).HasMaxLength(BudgetTransmission.NameMaxLength);
        builder.Property(t => t.Description).HasMaxLength(BudgetTransmission.DescriptionMaxLength);
        builder.Property(t => t.UserId).HasMaxLength(450);
        builder.Property(t => t.UserName).HasMaxLength(BudgetTransmission.NameMaxLength);
        builder.Property(t => t.ErpReference).HasMaxLength(BudgetTransmission.ReferenceMaxLength);
        builder.Property(t => t.Message).HasMaxLength(BudgetTransmission.MessageMaxLength);
        builder.Ignore(t => t.CountsAsSent);
        builder.Ignore(t => t.IsOpen);
        builder.Ignore(t => t.NetChange);

        builder.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.BudgetTransmissionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
        builder.HasOne<BudgetVersion>().WithMany().HasForeignKey(t => t.BudgetVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(t => t.GovernmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.GovernmentId, t.FiscalYear, t.CreatedAtUtc });

        // At most one unfinished send per government and year (Sending, Failed, AwaitingImport). The
        // service checks first so people get a sentence; this catches two clicks racing each other,
        // which would otherwise post the same changes to the ERP twice.
        builder.HasIndex(t => new { t.GovernmentId, t.FiscalYear })
            .IsUnique()
            .HasFilter($"[Status] IN ({(int)TransmissionStatus.Sending}, {(int)TransmissionStatus.Failed}, {(int)TransmissionStatus.AwaitingImport})")
            .HasDatabaseName("IX_BudgetTransmissions_OneOpenPerYear");
    }
}

internal sealed class BudgetTransmissionLineConfiguration : IEntityTypeConfiguration<BudgetTransmissionLine>
{
    public void Configure(EntityTypeBuilder<BudgetTransmissionLine> builder)
    {
        builder.Property(l => l.AccountNumber).HasMaxLength(BudgetTransmissionLine.AccountNumberMaxLength);
        builder.Property(l => l.RefusedReason).HasMaxLength(BudgetTransmissionLine.ReasonMaxLength);
        builder.HasOne<Fund>().WithMany().HasForeignKey(l => l.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(l => l.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(l => l.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
