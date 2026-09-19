using CivicBudget.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace CivicBudget.Infrastructure.Identity;

/// <summary>
/// An ASP.NET Core Identity user, extended with the government they belong to and a display name.
/// Lives in Infrastructure (not Domain) because it inherits from an Identity framework type.
/// It does <b>not</b> implement <see cref="ITenantOwned"/>: sign-in has to find the user before any
/// tenant is known, so Identity tables sit outside the tenant query filter and
/// <c>UserAdminService</c> scopes its queries by GovernmentId explicitly.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public const int DisplayNameMaxLength = 100;

    public Guid GovernmentId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Set when an administrator creates the account or resets its password: the password they
    /// typed is temporary, and the user is sent to change it before seeing anything else.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Departments a department user may edit. Empty for other roles.</summary>
    public ICollection<UserDepartment> Departments { get; } = [];
}

/// <summary>Join row: this user may edit budget lines in this department.</summary>
public sealed class UserDepartment
{
    public string UserId { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
}
