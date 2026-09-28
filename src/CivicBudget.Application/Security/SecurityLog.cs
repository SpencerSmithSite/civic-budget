using CivicBudget.Application.Persistence;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Security;

/// <summary>
/// Writes security events (sign-ins, failures, lockouts, second steps, exports). Infrastructure adds
/// where the request came from. Recording never fails the thing it records: a sign-in is not refused
/// because its log row could not be written.
/// </summary>
public interface ISecurityEventLog
{
    Task RecordAsync(SecurityEventKind kind, Guid? governmentId, string? userId, string? email, string? detail = null, CancellationToken ct = default);
}

/// <summary>One row of the security log page.</summary>
public sealed record SecurityEventDto(Guid Id, SecurityEventKind Kind, DateTimeOffset OccurredAtUtc, string? Email, string? IpAddress, string? Detail);

public sealed record SecurityLogPageDto(int Total, IReadOnlyList<SecurityEventDto> Events);

/// <summary>
/// A government's security log, for its Administrators: who signed in, who failed to and from where,
/// lockouts, and every export. The events are not tenant-filtered (some belong to no government), so
/// this service scopes by the current government itself, as the Identity services do.
/// </summary>
public interface ISecurityLogService
{
    Task<SecurityLogPageDto?> ListAsync(SecurityEventKind? kind, int skip, int take, CancellationToken ct = default);
}

public sealed class SecurityLogService(ICivicBudgetDbContextFactory dbFactory, ICurrentUser currentUser, ITenantContext tenant) : ISecurityLogService
{
    public async Task<SecurityLogPageDto?> ListAsync(SecurityEventKind? kind, int skip, int take, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin) || tenant.GovernmentId is not { } governmentId)
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<SecurityEvent> events = db.SecurityEvents.Where(e => e.GovernmentId == governmentId);
        if (kind is { } only)
        {
            events = events.Where(e => e.Kind == only);
        }

        int total = await events.CountAsync(ct);
        List<SecurityEventDto> page = await events.OrderByDescending(e => e.OccurredAtUtc)
            .Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 200))
            .Select(e => new SecurityEventDto(e.Id, e.Kind, e.OccurredAtUtc, e.Email, e.IpAddress, e.Detail))
            .ToListAsync(ct);
        return new SecurityLogPageDto(total, page);
    }
}
