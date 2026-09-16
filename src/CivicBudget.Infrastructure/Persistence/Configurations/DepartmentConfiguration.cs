using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.Property(d => d.Code).HasMaxLength(Department.CodeMaxLength);
        builder.Property(d => d.Name).HasMaxLength(Department.NameMaxLength);
        builder.HasIndex(d => new { d.GovernmentId, d.Code }).IsUnique();
        builder.HasOne<Government>().WithMany().HasForeignKey(d => d.GovernmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
