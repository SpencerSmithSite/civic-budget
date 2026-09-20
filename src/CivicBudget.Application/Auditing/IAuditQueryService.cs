using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
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
    string EntityName = "",
    /// <summary>Who acted, so a timeline can show their picture next to their name.</summary>
    string UserId = "");

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

/// <summary>
/// A department user sees only the trail of their own departments' budget lines: their
/// assignment is the whole of what they may see, on this screen as on every other. Everyone
/// else sees the government's whole trail.
/// </summary>
public sealed class AuditQueryService(ICivicBudgetDbContextFactory dbFactory, ICurrentUser currentUser) : IAuditQueryService
{
    public async Task<IReadOnlyList<AuditEntryDto>> GetHistoryAsync(string entityName, Guid entityId, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (currentUser.IsDepartmentUser() && !await IsTheirLineAsync(db, entityName, entityId, ct))
        {
            return [];
        }

        return await db.AuditEntries
            .Where(a => a.EntityName == entityName && a.EntityId == entityId)
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => new AuditEntryDto(a.TimestampUtc, a.UserName, a.Kind, a.PropertyName, a.OldValue, a.NewValue, a.Description, a.EntityName, a.UserId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<AuditEntry> entries = db.AuditEntries.Where(a => a.UserId != "system" && a.Kind != AuditKind.Created);
        if (currentUser.IsDepartmentUser())
        {
            List<Guid> departments = currentUser.DepartmentIds.ToList();
            IQueryable<Guid> theirLines = db.BudgetLines.Where(l => l.DepartmentId != null && departments.Contains(l.DepartmentId.Value)).Select(l => l.Id);
            entries = entries.Where(a => a.EntityName == nameof(BudgetLine) && theirLines.Contains(a.EntityId));
        }

        return await entries
            .OrderByDescending(a => a.TimestampUtc)
            .Take(count)
            .Select(a => new AuditEntryDto(a.TimestampUtc, a.UserName, a.Kind, a.PropertyName, a.OldValue, a.NewValue, a.Description, a.EntityName, a.UserId))
            .ToListAsync(ct);
    }

    private async Task<bool> IsTheirLineAsync(ICivicBudgetDbContext db, string entityName, Guid entityId, CancellationToken ct)
    {
        if (entityName != nameof(BudgetLine))
        {
            return false;
        }

        List<Guid> departments = currentUser.DepartmentIds.ToList();
        return await db.BudgetLines.AnyAsync(l => l.Id == entityId && l.DepartmentId != null && departments.Contains(l.DepartmentId.Value), ct);
    }
}
