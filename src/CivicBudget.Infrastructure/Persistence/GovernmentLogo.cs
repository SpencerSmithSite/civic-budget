namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// A government's logo for the public portal, uploaded by its administrator and resized by the
/// browser to at most 512 px. Its own table, mapped by both contexts: the admin context writes it
/// and the read-only portal context serves it. It holds nothing but a public image, which is why
/// the portal may read it without breaking the "snapshots only" rule (ADR-0006, ADR-0029).
/// </summary>
public sealed class GovernmentLogo
{
    public const int MaxBytes = 512 * 1024;

    public Guid GovernmentId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
