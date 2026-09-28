using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Notifications;

/// <summary>Why an email was written.</summary>
public enum EmailKind
{
    DepartmentSubmitted = 1,
    DepartmentReturned = 2,
    PasswordReset = 3,
    Welcome = 4,
}

/// <summary>Where an email stands.</summary>
public enum EmailStatus
{
    /// <summary>Kept in the outbox: this deployment has no mail server (the demo), so it is shown, not sent.</summary>
    Held = 1,

    /// <summary>Waiting for the mail server.</summary>
    Pending = 2,

    Sent = 3,

    /// <summary>The mail server refused it after every retry; <see cref="OutboxEmail.LastError"/> says why.</summary>
    Failed = 4,
}

/// <summary>
/// One email the application wrote, kept as a record of what was sent to whom. Emails are written in
/// the same save as the change that caused them (the transactional outbox pattern): a department's
/// submission and the notice about it are saved together or not at all, and a mail server that is
/// down delays the notice rather than failing the submission. A background sender delivers pending
/// ones; a deployment without a mail server keeps them here, held, for an administrator to read.
/// </summary>
public sealed class OutboxEmail : Entity, ITenantOwned
{
    public const int AddressMaxLength = 256;
    public const int NameMaxLength = 100;
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 8000;
    public const int ErrorMaxLength = 1000;

    /// <summary>A message the server keeps refusing stops here, so a bad address does not retry forever.</summary>
    public const int MaxAttempts = 5;

    public Guid GovernmentId { get; private set; }
    public EmailKind Kind { get; private set; }
    public string ToAddress { get; private set; } = null!;
    public string ToName { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public EmailStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }

    public OutboxEmail(Guid governmentId, EmailKind kind, string toAddress, string toName, string subject, string body, DateTimeOffset createdAtUtc, bool deliver)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        GovernmentId = governmentId;
        Kind = kind;
        ToAddress = Guard.MaxLength(Guard.NotNullOrWhiteSpace(toAddress, nameof(toAddress)), AddressMaxLength, nameof(toAddress));
        ToName = Truncate(toName.Trim(), NameMaxLength);
        Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject, nameof(subject)), SubjectMaxLength, nameof(subject));
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body, nameof(body)), BodyMaxLength, nameof(body));
        CreatedAtUtc = createdAtUtc;
        Status = deliver ? EmailStatus.Pending : EmailStatus.Held;
    }

    private OutboxEmail()
    {
    }

    public void MarkSent(DateTimeOffset nowUtc)
    {
        Guard.Against(Status != EmailStatus.Pending, "Only a pending email can be sent.");
        Attempts++;
        Status = EmailStatus.Sent;
        SentAtUtc = nowUtc;
        LastError = null;
    }

    /// <summary>Records a refusal; after <see cref="MaxAttempts"/> the email stops being retried.</summary>
    public void MarkAttemptFailed(string error)
    {
        Guard.Against(Status != EmailStatus.Pending, "Only a pending email can be attempted.");
        Attempts++;
        LastError = Truncate(error, ErrorMaxLength);
        if (Attempts >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
