using CivicBudget.Domain.Common;
using CivicBudget.Domain.Notifications;

namespace CivicBudget.Domain.Tests;

/// <summary>An email is kept, sent, or retried a limited number of times and then left failed with the reason.</summary>
public class OutboxEmailTests
{
    private static OutboxEmail Email(bool deliver) =>
        new(TestData.GovernmentId, EmailKind.PasswordReset, "lee@mapleridge.example", "Council Member Lee", "Reset your password", "Link", DateTimeOffset.UtcNow, deliver);

    [Fact]
    public void Without_a_mail_server_an_email_is_held_and_cannot_be_sent()
    {
        OutboxEmail held = Email(deliver: false);

        Assert.Equal(EmailStatus.Held, held.Status);
        Assert.Throws<DomainException>(() => held.MarkSent(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_pending_email_is_sent_once()
    {
        OutboxEmail email = Email(deliver: true);

        email.MarkSent(DateTimeOffset.UtcNow);

        Assert.Equal((EmailStatus.Sent, 1), (email.Status, email.Attempts));
        Assert.Throws<DomainException>(() => email.MarkSent(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Refusals_are_retried_up_to_the_limit_and_the_last_reason_is_kept()
    {
        OutboxEmail email = Email(deliver: true);

        for (int i = 1; i < OutboxEmail.MaxAttempts; i++)
        {
            email.MarkAttemptFailed($"try {i}");
            Assert.Equal(EmailStatus.Pending, email.Status);
        }

        email.MarkAttemptFailed(new string('x', 2_000));

        Assert.Equal((EmailStatus.Failed, OutboxEmail.MaxAttempts), (email.Status, email.Attempts));
        Assert.Equal(OutboxEmail.ErrorMaxLength, email.LastError!.Length);
    }

    [Fact]
    public void An_email_needs_an_address_and_a_subject()
    {
        Assert.Throws<DomainException>(() => new OutboxEmail(TestData.GovernmentId, EmailKind.Welcome, " ", "", "Hi", "Body", DateTimeOffset.UtcNow, true));
        Assert.Throws<DomainException>(() => new OutboxEmail(TestData.GovernmentId, EmailKind.Welcome, "a@b.example", "", "", "Body", DateTimeOffset.UtcNow, true));
    }
}
