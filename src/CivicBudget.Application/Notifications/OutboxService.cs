using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Notifications;

/// <summary>One email in the outbox, as the page lists it.</summary>
public sealed record OutboxEmailDto(Guid Id, EmailKind Kind, string ToAddress, string ToName, string Subject, string Body,
    DateTimeOffset CreatedAtUtc, EmailStatus Status, int Attempts, string? LastError, DateTimeOffset? SentAtUtc);

/// <summary>The outbox page: whether this deployment sends mail, and the newest emails first.</summary>
public sealed record OutboxPageDto(bool Delivers, int Total, IReadOnlyList<OutboxEmailDto> Emails);

/// <summary>
/// The government's email outbox, for its Administrators: every email the application wrote, to
/// whom, and whether it went. The emails include sign-in links, so nobody else reads them.
/// </summary>
public interface IOutboxService
{
    Task<OutboxPageDto?> ListAsync(int skip, int take, CancellationToken ct = default);
}

public sealed class OutboxService(ICivicBudgetDbContextFactory dbFactory, ICurrentUser currentUser, IEmailOutbox outbox) : IOutboxService
{
    public async Task<OutboxPageDto?> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin))
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int total = await db.OutboxEmails.CountAsync(ct);
        List<OutboxEmailDto> emails = await db.OutboxEmails
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 100))
            .Select(e => new OutboxEmailDto(e.Id, e.Kind, e.ToAddress, e.ToName, e.Subject, e.Body, e.CreatedAtUtc, e.Status, e.Attempts, e.LastError, e.SentAtUtc))
            .ToListAsync(ct);
        return new OutboxPageDto(outbox.Delivers, total, emails);
    }
}
