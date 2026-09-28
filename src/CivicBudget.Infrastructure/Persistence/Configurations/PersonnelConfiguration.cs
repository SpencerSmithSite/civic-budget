using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Personnel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

// Rates are percentages kept to four places (a BWC rate of 1.2345%); money stays decimal(18,2) by convention.
// Every child cascades from its parent only; its other references Restrict, because SQL Server allows one
// cascade path to a table and because a plan or fund a position uses must not disappear under it.

internal sealed class PersonnelSettingsConfiguration : IEntityTypeConfiguration<PersonnelSettings>
{
    public void Configure(EntityTypeBuilder<PersonnelSettings> builder)
    {
        builder.ToTable("PersonnelSettings");
        builder.Property(s => s.StandardHours).HasPrecision(9, 2);
        builder.Property(s => s.MedicareRate).HasPrecision(9, 4);
        builder.Property(s => s.WorkersCompRate).HasPrecision(9, 4);
        builder.HasIndex(s => new { s.GovernmentId, s.FiscalYear }).IsUnique(); // one set per government and year
        builder.HasOne<Government>().WithMany().HasForeignKey(s => s.GovernmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(s => s.PayAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(s => s.MedicareAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(s => s.WorkersCompAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.RetirementPlans).WithOne().HasForeignKey(p => p.PersonnelSettingsId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.RetirementPlans).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_retirementPlans");
        builder.HasMany(s => s.InsurancePlans).WithOne().HasForeignKey(p => p.PersonnelSettingsId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.InsurancePlans).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_insurancePlans");
        builder.HasMany(s => s.ExtraPay).WithOne().HasForeignKey(p => p.PersonnelSettingsId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.ExtraPay).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_extraPay");
        builder.HasMany(s => s.LongevitySchedules).WithOne().HasForeignKey(p => p.PersonnelSettingsId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.LongevitySchedules).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_longevitySchedules");
        builder.HasMany(s => s.PayScales).WithOne().HasForeignKey(p => p.PersonnelSettingsId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.PayScales).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_payScales");
    }
}

internal sealed class RetirementPlanConfiguration : IEntityTypeConfiguration<RetirementPlan>
{
    public void Configure(EntityTypeBuilder<RetirementPlan> builder)
    {
        builder.ToTable("RetirementPlans");
        builder.Property(p => p.Name).HasMaxLength(PersonnelSettings.NameMaxLength);
        builder.Property(p => p.EmployerRate).HasPrecision(9, 4);
        builder.Property(p => p.EmployeeRate).HasPrecision(9, 4);
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InsurancePlanConfiguration : IEntityTypeConfiguration<InsurancePlan>
{
    public void Configure(EntityTypeBuilder<InsurancePlan> builder)
    {
        builder.ToTable("InsurancePlans");
        builder.Property(p => p.Name).HasMaxLength(PersonnelSettings.NameMaxLength);
        builder.Property(p => p.EmployeeSharePercent).HasPrecision(9, 4);
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExtraPayConfiguration : IEntityTypeConfiguration<ExtraPay>
{
    public void Configure(EntityTypeBuilder<ExtraPay> builder)
    {
        builder.ToTable("ExtraPay");
        builder.Property(p => p.Name).HasMaxLength(PersonnelSettings.NameMaxLength);
        builder.Property(p => p.Multiplier).HasPrecision(9, 4);
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LongevityScheduleConfiguration : IEntityTypeConfiguration<LongevitySchedule>
{
    public void Configure(EntityTypeBuilder<LongevitySchedule> builder)
    {
        builder.ToTable("LongevitySchedules");
        builder.Property(p => p.Name).HasMaxLength(PersonnelSettings.NameMaxLength);
        builder.HasMany(p => p.Steps).WithOne().HasForeignKey(s => s.LongevityScheduleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Steps).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_steps");
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LongevityStepConfiguration : IEntityTypeConfiguration<LongevityStep>
{
    public void Configure(EntityTypeBuilder<LongevityStep> builder)
    {
        builder.ToTable("LongevitySteps");
        builder.HasIndex(s => new { s.LongevityScheduleId, s.MinYears }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(s => s.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayScaleConfiguration : IEntityTypeConfiguration<PayScale>
{
    public void Configure(EntityTypeBuilder<PayScale> builder)
    {
        builder.ToTable("PayScales");
        builder.Property(p => p.Name).HasMaxLength(PersonnelSettings.NameMaxLength);
        builder.HasMany(p => p.Rates).WithOne().HasForeignKey(r => r.PayScaleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Rates).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_rates");
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayScaleRateConfiguration : IEntityTypeConfiguration<PayScaleRate>
{
    public void Configure(EntityTypeBuilder<PayScaleRate> builder)
    {
        builder.ToTable("PayScaleRates");
        builder.Property(r => r.Grade).HasMaxLength(PositionDetails.GradeMaxLength);
        builder.HasIndex(r => new { r.PayScaleId, r.Grade, r.Step }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(r => r.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(PositionDetails.TitleMaxLength);
        builder.Property(p => p.EmployeeName).HasMaxLength(PositionDetails.NameMaxLength);
        builder.Property(p => p.EmployeeId).HasMaxLength(PositionDetails.EmployeeIdMaxLength);
        builder.Property(p => p.Grade).HasMaxLength(PositionDetails.GradeMaxLength);
        builder.Property(p => p.AnnualHours).HasPrecision(9, 2);
        builder.Property(p => p.RaisePercent).HasPrecision(9, 4);
        builder.HasIndex(p => new { p.BudgetVersionId, p.DepartmentId });

        builder.HasOne<Department>().WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(p => p.GovernmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.PayAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PayScale>().WithMany().HasForeignKey(p => p.PayScaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LongevitySchedule>().WithMany().HasForeignKey(p => p.LongevityScheduleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RetirementPlan>().WithMany().HasForeignKey(p => p.RetirementPlanId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Funds).WithOne().HasForeignKey(f => f.PositionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Funds).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_funds");
        builder.HasMany(p => p.Coverages).WithOne().HasForeignKey(c => c.PositionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Coverages).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_coverages");
        builder.HasMany(p => p.ExtraPay).WithOne().HasForeignKey(e => e.PositionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.ExtraPay).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_extraPay");
    }
}

internal sealed class PositionFundShareConfiguration : IEntityTypeConfiguration<PositionFundShare>
{
    public void Configure(EntityTypeBuilder<PositionFundShare> builder)
    {
        builder.ToTable("PositionFundShares");
        builder.Property(f => f.Percent).HasPrecision(9, 4);
        builder.HasIndex(f => new { f.PositionId, f.FundId }).IsUnique();
        builder.HasOne<Fund>().WithMany().HasForeignKey(f => f.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(f => f.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PositionCoverageConfiguration : IEntityTypeConfiguration<PositionCoverage>
{
    public void Configure(EntityTypeBuilder<PositionCoverage> builder)
    {
        builder.ToTable("PositionCoverages");
        builder.HasIndex(c => new { c.PositionId, c.InsurancePlanId }).IsUnique();
        builder.HasOne<InsurancePlan>().WithMany().HasForeignKey(c => c.InsurancePlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(c => c.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PositionExtraPayConfiguration : IEntityTypeConfiguration<PositionExtraPay>
{
    public void Configure(EntityTypeBuilder<PositionExtraPay> builder)
    {
        builder.ToTable("PositionExtraPay");
        builder.HasIndex(e => new { e.PositionId, e.ExtraPayId }).IsUnique();
        builder.HasOne<ExtraPay>().WithMany().HasForeignKey(e => e.ExtraPayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(e => e.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PersonnelSyncConfiguration : IEntityTypeConfiguration<PersonnelSync>
{
    public void Configure(EntityTypeBuilder<PersonnelSync> builder)
    {
        builder.Property(s => s.SourceName).HasMaxLength(PersonnelSync.SourceMaxLength);
        builder.Property(s => s.FileName).HasMaxLength(PersonnelSync.FileNameMaxLength);
        builder.Property(s => s.UserId).HasMaxLength(450); // matches ASP.NET Core Identity's key length
        builder.Property(s => s.UserName).HasMaxLength(256);
        builder.HasIndex(s => new { s.GovernmentId, s.SyncedAtUtc });
        builder.HasOne<Domain.Budgets.BudgetVersion>().WithMany().HasForeignKey(s => s.BudgetVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Government>().WithMany().HasForeignKey(s => s.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
