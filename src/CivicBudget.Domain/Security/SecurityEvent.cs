using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Security;

/// <summary>What happened at the door.</summary>
public enum SecurityEventKind
{
    SignedIn = 1,

    /// <summary>A wrong password for a real account.</summary>
    SignInFailed = 2,

    /// <summary>A sign-in attempt for an address with no account. Kept for the operator only: it belongs to no government.</summary>
    UnknownAccount = 3,

    /// <summary>Too many wrong passwords; the account is locked for a while.</summary>
    LockedOut = 4,

    /// <summary>The password was right and the second step is next.</summary>
    TwoStepRequired = 5,

    TwoStepFailed = 6,
    RecoveryCodeUsed = 7,
    SignedOut = 8,

    /// <summary>Signed out by the idle timeout rather than by the person.</summary>
    SessionExpired = 9,

    PasswordChanged = 10,

    /// <summary>A file of the government's data left the building: an export, a report, a journal, the full archive.</summary>
    DataExported = 11,

    /// <summary>A request refused for coming too fast (sign-in, password reset, exports).</summary>
    RateLimited = 12,

    /// <summary>The operator removed a government and all of its data (<c>--offboard</c>).</summary>
    GovernmentRemoved = 13,

    /// <summary>The retention job removed security events and emails past their keeping period.</summary>
    RetentionPurge = 14,

    /// <summary>
    /// A question to the assistant, with the tools it used. The question's text is not kept: it can
    /// hold anything, and the log records what was reached, not what was asked.
    /// </summary>
    AssistantUsed = 15,
}

/// <summary>
/// One security-relevant event: a sign-in, a failure, a lockout, a second step, an export. Kept
/// apart from the audit trail, which records changes to the budget and its setup, so the question
/// "who got in, who tried to, and what did they take away" has one place to look. Events before a
/// government is known (an address with no account) have no government and are visible only to
/// the operator. Written once, never changed; removed only by the retention job.
/// </summary>
public sealed class SecurityEvent : Entity
{
    public const int EmailMaxLength = 256;
    public const int AddressMaxLength = 64;
    public const int DetailMaxLength = 500;

    public Guid? GovernmentId { get; private set; }
    public SecurityEventKind Kind { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? UserId { get; private set; }

    /// <summary>The address the person typed or signed in with.</summary>
    public string? Email { get; private set; }

    /// <summary>Where the request came from (the client address the load balancer reports).</summary>
    public string? IpAddress { get; private set; }

    /// <summary>A short description: which export, how many recovery codes are left.</summary>
    public string? Detail { get; private set; }

    public SecurityEvent(Guid? governmentId, SecurityEventKind kind, DateTimeOffset occurredAtUtc, string? userId, string? email, string? ipAddress, string? detail = null)
    {
        GovernmentId = governmentId;
        Kind = kind;
        OccurredAtUtc = occurredAtUtc;
        UserId = userId;
        Email = Clip(email?.Trim().ToLowerInvariant(), EmailMaxLength);
        IpAddress = Clip(ipAddress, AddressMaxLength);
        Detail = Clip(detail, DetailMaxLength);
    }

    private SecurityEvent()
    {
    }

    private static string? Clip(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
