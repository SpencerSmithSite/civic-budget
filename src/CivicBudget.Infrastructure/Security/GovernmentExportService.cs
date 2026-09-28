using CivicBudget.Application.Security;

namespace CivicBudget.Infrastructure.Security;

/// <summary>The Administrator's full export of their own government, through <see cref="GovernmentDataStore"/>.</summary>
public sealed class GovernmentExportService(GovernmentDataStore store, ICurrentUser currentUser, TimeProvider clock) : IGovernmentExportService
{
    public async Task<GovernmentExportFile?> ExportAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin) || currentUser.GovernmentId is not { } governmentId)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        GovernmentExportSummary? summary = await store.WriteExportAsync(governmentId, buffer, ct);
        return summary is null ? null : new GovernmentExportFile($"{summary.PublicSlug}-all-data-{clock.GetUtcNow():yyyy-MM-dd}.zip", buffer.ToArray());
    }
}
