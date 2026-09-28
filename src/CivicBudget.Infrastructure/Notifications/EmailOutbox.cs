using System.Threading.Channels;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Persistence;
using CivicBudget.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Notifications;

/// <summary>
/// A wake-up call for the sender. Writing to it costs nothing and needs no database, so the sender
/// runs only when there is mail: a timer that checked the outbox every minute would keep the demo's
/// free serverless database from ever pausing (ADR-0031).
/// </summary>
public sealed class EmailSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Raise() => channel.Writer.TryWrite(true);

    public ValueTask<bool> WaitAsync(CancellationToken ct) => channel.Reader.WaitToReadAsync(ct);

    public void Consume() => channel.Reader.TryRead(out _);
}

public sealed class EmailOutbox(IOptions<EmailOptions> options, EmailSignal signal, TimeProvider clock) : IEmailOutbox
{
    public bool Delivers => options.Value.Delivers;

    public void Add(ICivicBudgetDbContext db, Guid governmentId, EmailKind kind, EmailRecipient recipient, EmailContent content) =>
        db.OutboxEmails.Add(new OutboxEmail(governmentId, kind, recipient.Email, recipient.Name, content.Subject, content.Body, clock.GetUtcNow(), Delivers));

    public void Notify()
    {
        if (Delivers)
        {
            signal.Raise();
        }
    }
}
