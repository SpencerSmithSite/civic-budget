using CivicBudget.Application.Common;

namespace CivicBudget.Application.Setup;

/// <summary>
/// The government's logo for the public portal, managed by its administrator under Government
/// settings. The portal reads it through <see cref="Portal.ISnapshotQueryService.GetLogoAsync"/>.
/// </summary>
public interface IGovernmentLogoService
{
    /// <summary>The current logo for the settings preview, or null when the portal shows the CivicBudget mark.</summary>
    Task<Portal.PortalLogoDto?> GetAsync(CancellationToken ct = default);

    /// <summary>Stores the logo for the current government. The browser has already resized it; the service checks type and size.</summary>
    Task<Result> SetAsync(byte[] data, string contentType, CancellationToken ct = default);

    Task<Result> RemoveAsync(CancellationToken ct = default);
}
