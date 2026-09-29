using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class CertificateSettingsConfiguration : IEntityTypeConfiguration<CertificateSettings>
{
    public void Configure(EntityTypeBuilder<CertificateSettings> builder)
    {
        builder.Property(s => s.County).HasMaxLength(CertificateSettings.NameMaxLength);
        builder.Property(s => s.FiscalOfficerName).HasMaxLength(CertificateSettings.NameMaxLength);
        builder.Property(s => s.FiscalOfficerTitle).HasMaxLength(CertificateSettings.NameMaxLength);
        builder.Property(s => s.BalanceLabel).HasMaxLength(CertificateSettings.LabelMaxLength);
        builder.Property(s => s.OtherSourcesLabel).HasMaxLength(CertificateSettings.LabelMaxLength);
        builder.HasIndex(s => s.GovernmentId).IsUnique(); // one set of certificate settings per government
        builder.HasOne<Government>().WithMany().HasForeignKey(s => s.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BudgetBookSettingsConfiguration : IEntityTypeConfiguration<BudgetBookSettings>
{
    public void Configure(EntityTypeBuilder<BudgetBookSettings> builder)
    {
        builder.HasIndex(s => s.GovernmentId).IsUnique(); // one set of book defaults per government
        builder.HasOne<Government>().WithMany().HasForeignKey(s => s.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReportAccountGroupConfiguration : IEntityTypeConfiguration<ReportAccountGroup>
{
    public void Configure(EntityTypeBuilder<ReportAccountGroup> builder)
    {
        builder.Property(g => g.Label).HasMaxLength(ReportAccountGroup.LabelMaxLength);
        builder.HasIndex(g => new { g.GovernmentId, g.Report, g.SortOrder });
        builder.HasMany(g => g.Accounts).WithOne().HasForeignKey(a => a.ReportAccountGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(g => g.Accounts).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_accounts");
        builder.HasOne<Government>().WithMany().HasForeignKey(g => g.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReportAccountGroupAccountConfiguration : IEntityTypeConfiguration<ReportAccountGroupAccount>
{
    public void Configure(EntityTypeBuilder<ReportAccountGroupAccount> builder)
    {
        builder.ToTable("ReportAccountGroupAccounts");
        builder.HasIndex(a => new { a.ReportAccountGroupId, a.AccountId }).IsUnique();
        builder.HasOne<Account>().WithMany().HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(a => a.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CertificateFundAdjustmentConfiguration : IEntityTypeConfiguration<CertificateFundAdjustment>
{
    public void Configure(EntityTypeBuilder<CertificateFundAdjustment> builder)
    {
        builder.Ignore(a => a.IsEmpty);
        builder.HasIndex(a => new { a.GovernmentId, a.FiscalYear, a.FundId }).IsUnique();
        builder.HasOne<Fund>().WithMany().HasForeignKey(a => a.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(a => a.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
