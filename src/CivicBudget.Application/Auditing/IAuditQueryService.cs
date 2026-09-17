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
    string? Description);

/// <summary>Read side of the audit trail. The write side is the interceptor in Infrastructure.</summary>
public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditEntryDto>> GetHistoryAsync(string entityName, Guid entityId, CancellationToken ct = default);
}

public sealed class AuditQueryService(ICivicBudgetDbContextFactory dbFactory) : IAuditQueryService
{
    public async Task<IReadOnlyList<AuditEntryDto>> GetHistoryAsync(string entityName, Guid entityId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditEntries
            .Where(a => a.EntityName == entityName && a.EntityId == entityId)
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => new AuditEntryDto(a.TimestampUtc, a.UserName, a.Kind, a.PropertyName, a.OldValue, a.NewValue, a.Description))
            .ToListAsync(ct);
    }
}
