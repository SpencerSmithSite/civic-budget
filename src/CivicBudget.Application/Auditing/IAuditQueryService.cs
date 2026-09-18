using CivicBudget.Application.Persistence;
using CivicBudget.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Auditing;

public sealed record AuditEntryDto(
    DateTimeOffset TimestampUtc,
    string UserName,
    AuditKind Kind,
    string? PropertyName,
    string? OldValue,
    string? NewValue,
    string? Description,
    string EntityName = "");

/// <summary>Read side of the audit trail. The write side is the interceptor in Infrastructure.</summary>
public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditEntryDto>> GetHistoryAsync(string entityName, Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// The latest human-attributed entries for the overview page. Seeding and row creations are
    /// excluded as noise: an amendment copies 95 lines, and the "Amendment started" event already says so.
    /// </summary>
    Task<IReadOnlyList<AuditEntryDto>> GetRecentAsync(int count, CancellationToken ct = default);
}

public sealed class AuditQueryService(ICivicBudgetDbContextFactory dbFactory) : IAuditQueryService
{
    public async Task<IReadOnlyList<AuditEntryDto>> GetHistoryAsync(string entityName, Guid entityId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditEntries
            .Where(a => a.EntityName == entityName && a.EntityId == entityId)
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => new AuditEntryDto(a.TimestampUtc, a.UserName, a.Kind, a.PropertyName, a.OldValue, a.NewValue, a.Description, a.EntityName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditEntries
            .Where(a => a.UserId != "system" && a.Kind != AuditKind.Created)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(count)
            .Select(a => new AuditEntryDto(a.TimestampUtc, a.UserName, a.Kind, a.PropertyName, a.OldValue, a.NewValue, a.Description, a.EntityName))
            .ToListAsync(ct);
    }
}
