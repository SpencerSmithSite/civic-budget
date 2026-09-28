using CivicBudget.Application.Persistence;
using CivicBudget.Domain.Notifications;

namespace CivicBudget.Application.Notifications;

/// <summary>Someone an email goes to; the user id lets a sender leave out the person who caused the notice.</summary>
public sealed record EmailRecipient(string UserId, string Email, string Name);

/// <summary>What an email says; written by <see cref="EmailTemplates"/>.</summary>
public sealed record EmailContent(string Subject, string Body);

/// <summary>
/// Writes emails into the outbox, in the caller's unit of work, so an email is saved with the change
/// that caused it or not at all. After the caller saves, <see cref="Notify"/> wakes the sender.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>Whether this deployment delivers mail; without a mail server, emails are kept for an administrator to read.</summary>
    bool Delivers { get; }

    void Add(ICivicBudgetDbContext db, Guid governmentId, EmailKind kind, EmailRecipient recipient, EmailContent content);

    /// <summary>Call after a successful save: the sender picks the new emails up straight away rather than on a timer.</summary>
    void Notify();
}

/// <summary>
/// The people notices go to. Users are Identity rows, which the Application layer does not query, so
/// Infrastructure answers; only users with a confirmed email and an unlocked account are returned.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Administrators and Fiscal Officers: who a department's submission goes to.</summary>
    Task<IReadOnlyList<EmailRecipient>> FiscalAuthorityAsync(Guid governmentId, CancellationToken ct = default);

    /// <summary>The department users assigned to a department: who a returned request goes to.</summary>
    Task<IReadOnlyList<EmailRecipient>> DepartmentUsersAsync(Guid governmentId, Guid departmentId, CancellationToken ct = default);

    /// <summary>How many user accounts the government has.</summary>
    Task<int> CountAsync(Guid governmentId, CancellationToken ct = default);
}

/// <summary>
/// Absolute links for emails. A page knows where it is being served from; a service writing an email
/// does not, so the Web layer tells it (from the request or the circuit's address).
/// </summary>
public interface IAppLinks
{
    string Absolute(string relativePath);
}

/// <summary>
/// The wording of every email, in one place and in plain text: every mail client shows plain text,
/// screen readers read it well, and it carries no tracking. Each one says why it was sent, and none
/// of them contains a password.
/// </summary>
public static class EmailTemplates
{
    private const string Footer = "\n\n--\nCivicBudget. You receive this because of your role in your government's budget.";

    public static EmailContent DepartmentSubmitted(string governmentName, string departmentName, string budget, string submittedBy, string link) => new(
        $"{departmentName} submitted its {budget} request",
        $"{submittedBy} submitted {departmentName}'s request for the {budget} budget of {governmentName}.\n\n" +
        $"Review it, and return it with a note if anything should change:\n{link}" + Footer);

    public static EmailContent DepartmentReturned(string governmentName, string departmentName, string budget, string note, string link) => new(
        $"{departmentName}'s {budget} request was returned",
        $"The fiscal officer of {governmentName} returned {departmentName}'s request for the {budget} budget with this note:\n\n" +
        $"  {note.Replace("\n", "\n  ", StringComparison.Ordinal)}\n\n" +
        $"Make the changes and submit it again:\n{link}" + Footer);

    public static EmailContent PasswordReset(string governmentName, string link, TimeSpan validFor) => new(
        "Reset your CivicBudget password",
        $"Someone asked to reset the password for your {governmentName} CivicBudget account. If it was you, choose a new password here:\n{link}\n\n" +
        $"The link works once, for {Hours(validFor)}. If you did not ask, ignore this email; your password has not changed." + Footer);

    public static EmailContent Welcome(string governmentName, string role, string addedBy, string link, TimeSpan validFor) => new(
        $"Your {governmentName} CivicBudget account",
        $"{addedBy} added you to {governmentName}'s budget in CivicBudget as {role}.\n\n" +
        $"Choose your password to sign in:\n{link}\n\n" +
        $"The link works once, for {Hours(validFor)}. If it has expired, use \"Forgot your password?\" on the sign-in page, or ask {addedBy} to send a new one." + Footer);

    private static string Hours(TimeSpan span) => span.TotalHours >= 48 ? $"{span.TotalDays:0} days" : $"{span.TotalHours:0} hours";
}
