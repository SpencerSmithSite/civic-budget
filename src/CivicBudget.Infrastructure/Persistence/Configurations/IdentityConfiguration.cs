using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicBudget.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.HasIndex(u => u.GovernmentId);
        builder.HasOne<Government>().WithMany().HasForeignKey(u => u.GovernmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(u => u.Departments).WithOne().HasForeignKey(ud => ud.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserDepartmentConfiguration : IEntityTypeConfiguration<UserDepartment>
{
    public void Configure(EntityTypeBuilder<UserDepartment> builder)
    {
        builder.ToTable("UserDepartments");
        builder.HasKey(ud => new { ud.UserId, ud.DepartmentId });

        // Restrict, not Cascade: rows already cascade from the user, and SQL Server refuses a second
        // cascade path (Government -> Departments -> here, Government -> Users -> here). Departments
        // are retired with IsActive, never deleted, so this is never hit in practice.
        builder.HasOne<Department>().WithMany().HasForeignKey(ud => ud.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserAvatarConfiguration : IEntityTypeConfiguration<UserAvatar>
{
    public void Configure(EntityTypeBuilder<UserAvatar> builder)
    {
        builder.ToTable("UserAvatars");
        builder.HasKey(a => a.UserId);
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.ContentType).HasMaxLength(64);
        builder.Property(a => a.Data).HasMaxLength(UserAvatar.MaxBytes);
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserAvatar>(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
