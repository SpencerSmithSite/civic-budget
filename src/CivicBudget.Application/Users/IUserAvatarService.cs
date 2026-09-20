using CivicBudget.Application.Common;

namespace CivicBudget.Application.Users;

/// <summary>A profile picture as stored: the bytes the browser produced and when they were uploaded.</summary>
public sealed record UserAvatarDto(byte[] Data, string ContentType, DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Profile pictures. A user uploads their own; an administrator may remove anyone's in their
/// government. Reads are scoped to the current government, like every other Identity read.
/// </summary>
public interface IUserAvatarService
{
    /// <summary>The picture bytes for the image endpoint; null when the user has none or is outside the government.</summary>
    Task<UserAvatarDto?> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// The version (upload ticks) a page needs to build the image URL, or null for initials.
    /// Cached for the life of the scope (one circuit in the admin app), so a timeline of forty
    /// entries by three people costs three lookups.
    /// </summary>
    Task<long?> GetVersionAsync(string userId, CancellationToken ct = default);

    /// <summary>Stores the current user's picture. The browser has already resized it; the service only checks type and size.</summary>
    Task<Result> SetOwnAsync(byte[] data, string contentType, CancellationToken ct = default);

    /// <summary>Removes a picture: one's own, or any user's in the government when the caller is an Administrator.</summary>
    Task<Result> RemoveAsync(string userId, CancellationToken ct = default);

    /// <summary>Raised after a set or remove so open components (the top bar) refresh their image.</summary>
    event Action? Changed;
}
