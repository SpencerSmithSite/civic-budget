using CivicBudget.Domain.Notifications;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CivicBudget.Infrastructure.Notifications;

/// <summary>Hands one email to a mail server. An interface so tests can stand in for SMTP.</summary>
public interface IEmailTransport
{
    Task SendAsync(OutboxEmail email, CancellationToken ct);
}

/// <summary>SMTP through MailKit, with STARTTLS whenever the server offers it.</summary>
public sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(OutboxEmail email, CancellationToken ct)
    {
        EmailOptions o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName, email.ToAddress));
        message.Subject = email.Subject;
        message.Body = new TextPart("plain") { Text = email.Body };

        // Registered only when EmailOptions.Delivers, which requires a host.
        using var client = new SmtpClient();
        await client.ConnectAsync(o.SmtpHost!, o.SmtpPort, SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrEmpty(o.SmtpUserName))
        {
            await client.AuthenticateAsync(o.SmtpUserName, o.SmtpPassword ?? "", ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>
/// Delivers pending outbox emails when woken by <see cref="EmailSignal"/>: straight after the save
/// that wrote them. Each wake sends everything pending, so an email left over from a restart goes
/// out with the next one. A refusal is retried after a pause that grows with each attempt, up to
/// <see cref="OutboxEmail.MaxAttempts"/>. Registered only when a mail server is configured.
/// </summary>
public sealed class EmailDeliveryService(
    IServiceScopeFactory scopes,
    EmailSignal signal,
    IEmailTransport transport,
    TimeProvider clock,
    ILogger<EmailDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await signal.WaitAsync(stoppingToken))
        {
            signal.Consume();
            int retryAfterAttempts = await DeliverPendingAsync(stoppingToken);
            if (retryAfterAttempts > 0)
            {
                // Wake again later rather than hold the loop: a minute per attempt so far.
                _ = RaiseLaterAsync(TimeSpan.FromMinutes(retryAfterAttempts), stoppingToken);
            }
        }
    }

    private async Task RaiseLaterAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, clock, ct);
            signal.Raise();
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the email stays pending for the next start.
        }
    }

    /// <summary>Sends every pending email; returns the attempts of the most-tried one still pending, or 0.</summary>
    public async Task<int> DeliverPendingAsync(CancellationToken ct)
    {
        List<(Guid Id, Guid GovernmentId)> pending;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            // The sender works for every government at once, so it reads past the tenant filter here, and
            // only here; each update below is made acting for that email's own government.
            await using CivicBudgetDbContext db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>().CreateDbContextAsync(ct);
            pending = (await db.OutboxEmails.IgnoreQueryFilters().Where(e => e.Status == EmailStatus.Pending)
                .OrderBy(e => e.CreatedAtUtc).Select(e => new { e.Id, e.GovernmentId }).Take(200).ToListAsync(ct))
                .Select(e => (e.Id, e.GovernmentId)).ToList();
        }

        int retry = 0;
        foreach ((Guid id, Guid governmentId) in pending)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentUserContext>().SetTenant(governmentId);
            await using CivicBudgetDbContext db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>().CreateDbContextAsync(ct);
            OutboxEmail? email = await db.OutboxEmails.FirstOrDefaultAsync(e => e.Id == id && e.Status == EmailStatus.Pending, ct);
            if (email is null)
            {
                continue;
            }

            try
            {
                await transport.SendAsync(email, ct);
                email.MarkSent(clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Email {EmailId} to {Address} was refused (attempt {Attempt}).", email.Id, email.ToAddress, email.Attempts + 1);
                email.MarkAttemptFailed(ex.Message);
                if (email.Status == EmailStatus.Pending)
                {
                    retry = Math.Max(retry, email.Attempts);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        return retry;
    }
}
