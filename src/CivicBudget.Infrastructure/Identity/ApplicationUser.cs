using CivicBudget.Application.Common;
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

    /// <summary>
    /// When the user last uploaded a profile picture, null when they use their initials. Lives on
    /// the user row so lists can show pictures without touching the image bytes in <see cref="UserAvatar"/>;
    /// the ticks double as the cache-busting version in the image URL.
    /// </summary>
    public DateTimeOffset? AvatarUpdatedAtUtc { get; set; }

    /// <summary>Departments a department user may edit. Empty for other roles.</summary>
    public ICollection<UserDepartment> Departments { get; } = [];
}

/// <summary>
/// A user's profile picture, already resized by the browser to at most 256 px. Its own table so the
/// bytes are read only by the image endpoint, never by a user list.
/// </summary>
public sealed class UserAvatar
{
    public const int MaxBytes = UploadedImage.MaxBytes;

    public string UserId { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Join row: this user may edit budget lines in this department.</summary>
public sealed class UserDepartment
{
    public string UserId { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
}
