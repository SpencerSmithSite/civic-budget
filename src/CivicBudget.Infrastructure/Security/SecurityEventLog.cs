using CivicBudget.Application.Security;
using CivicBudget.Domain.Security;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Security;

/// <summary>
/// Writes security events in their own unit of work, with the client address from the request when
/// there is one (a circuit has none). A failure to write is logged and swallowed, so a database
/// hiccup never refuses a sign-in or an export; the structured log line is the fallback record.
/// </summary>
public sealed class SecurityEventLog(
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    IHttpContextAccessor http,
    TimeProvider clock,
    ILogger<SecurityEventLog> logger) : ISecurityEventLog
{
    public async Task RecordAsync(SecurityEventKind kind, Guid? governmentId, string? userId, string? email, string? detail = null, CancellationToken ct = default)
    {
        string? address = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        // The platform log names the account by its opaque id, never its address: the address belongs
        // in the security log table, where retention and access rules apply, not in log storage.
        logger.LogInformation("Security event {Kind} for user {UserId} from {Address}: {Detail}", kind, userId, address, detail);
        try
        {
            await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
            db.SecurityEvents.Add(new SecurityEvent(governmentId, kind, clock.GetUtcNow(), userId, email, address, detail));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or TimeoutException or Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException)
        {
            logger.LogError(ex, "Could not record security event {Kind} for user {UserId}.", kind, userId);
        }
    }
}
