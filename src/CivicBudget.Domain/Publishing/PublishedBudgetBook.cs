using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Publishing;

/// <summary>
/// The budget book as it was printed when the budget was published, kept beside its snapshot so a
/// resident downloads exactly what was published; later edits to the budget, its message, or the
/// book's layout never change it. In its own table so loading a snapshot's figures does not load a
/// PDF with them.
/// </summary>
public sealed class PublishedBudgetBook : Entity, ITenantOwned
{
    /// <summary>A village's book is well under a megabyte; this is room for a large city with every line.</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    public Guid SnapshotId { get; private set; }
    public Guid GovernmentId { get; private set; }
    public byte[] Content { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public PublishedBudgetBook(Guid snapshotId, Guid governmentId, byte[] content, DateTimeOffset createdAtUtc)
    {
        Guard.Against(content.Length == 0, "The budget book is empty.");
        Guard.Against(content.Length > MaxBytes, "The budget book is larger than a published book may be.");
        SnapshotId = snapshotId;
        GovernmentId = governmentId;
        Content = content;
        CreatedAtUtc = createdAtUtc;
    }

    private PublishedBudgetBook() => Content = [];
}
