using System.Globalization;
using CivicBudget.Domain.Notifications;
using CivicBudget.Domain.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Security;

/// <summary>
/// Removes records past their keeping period: security events and finished emails after a year.
/// The audit trail and the budgets are the government's financial records and are kept for as long
/// as the government is a customer. Run by the operator's scheduled <c>--maintenance</c> job, never
/// by a timer in the app, which would keep the demo's serverless database awake.
/// </summary>
public sealed class RetentionService(IDbContextFactory<CivicBudgetDbContext> dbFactory, TimeProvider clock)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(365);

    public async Task<(int SecurityEvents, int Emails)> PurgeAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset cutoff = now - KeepFor;
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);

        int events = await db.SecurityEvents.Where(e => e.OccurredAtUtc < cutoff).ExecuteDeleteAsync(ct);

        // The job works for every government at once, so it reads past the tenant filter here. An
        // email still waiting for the mail server is never removed, however old.
        int emails = await db.OutboxEmails.IgnoreQueryFilters()
            .Where(e => e.CreatedAtUtc < cutoff && e.Status != EmailStatus.Pending)
            .ExecuteDeleteAsync(ct);

        db.SecurityEvents.Add(new SecurityEvent(null, SecurityEventKind.RetentionPurge, now, null, null, null,
            string.Create(CultureInfo.InvariantCulture, $"Removed {events} security events and {emails} emails from before {cutoff:yyyy-MM-dd}")));
        await db.SaveChangesAsync(ct);
        return (events, emails);
    }
}
