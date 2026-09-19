using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class DepartmentRequestConfiguration : IEntityTypeConfiguration<DepartmentRequest>
{
    public void Configure(EntityTypeBuilder<DepartmentRequest> builder)
    {
        builder.Property(r => r.Narrative).HasMaxLength(DepartmentRequest.NarrativeMaxLength);
        builder.Property(r => r.ReturnNote).HasMaxLength(DepartmentRequest.ReturnNoteMaxLength);
        builder.Property(r => r.SubmittedByUserId).HasMaxLength(450); // matches ASP.NET Core Identity's key length
        builder.Property(r => r.SubmittedByUserName).HasMaxLength(256);
        builder.Ignore(r => r.IsSubmitted);

        builder.HasIndex(r => new { r.BudgetVersionId, r.DepartmentId }).IsUnique();
        // Restrict on both: the row already cascades from its version, and SQL Server allows one cascade path.
        builder.HasOne<Department>().WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Government>().WithMany().HasForeignKey(r => r.GovernmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
