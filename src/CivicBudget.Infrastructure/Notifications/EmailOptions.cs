namespace CivicBudget.Infrastructure.Notifications;

/// <summary>
/// How this deployment handles email, from the "Email" configuration section. Without a mail server
/// (the default, and the live demo, whose addresses are all fictional) every email is kept in the
/// outbox for an administrator to read. With one, the outbox delivers over SMTP; any provider that
/// speaks SMTP works, and the password comes from user-secrets or the host's secret store.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Outbox" (keep and show) or "Smtp" (deliver).</summary>
    public string Mode { get; set; } = "Outbox";

    public string FromAddress { get; set; } = "no-reply@civicbudget.example";
    public string FromName { get; set; } = "CivicBudget";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUserName { get; set; }
    public string? SmtpPassword { get; set; }

    public bool Delivers => string.Equals(Mode, "Smtp", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(SmtpHost);
}
