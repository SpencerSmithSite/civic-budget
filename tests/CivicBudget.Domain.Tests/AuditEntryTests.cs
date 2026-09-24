using CivicBudget.Domain.Auditing;

namespace CivicBudget.Domain.Tests;

/// <summary>History is for people to read; text that is too long is shortened, never allowed to fail the action it records.</summary>
public class AuditEntryTests
{
    [Fact]
    public void An_event_quoting_a_long_note_is_shortened_to_fit()
    {
        string note = new('x', AuditEntry.DescriptionMaxLength);

        AuditEntry entry = AuditEntry.Event(Guid.CreateVersion7(), "BudgetVersion", Guid.CreateVersion7(), $"Returned Parks & Recreation's budget request: {note}", "user-1", "Dana", DateTimeOffset.UnixEpoch);

        Assert.Equal(AuditEntry.DescriptionMaxLength, entry.Description!.Length);
        Assert.EndsWith("…", entry.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_event_is_kept_as_written()
    {
        AuditEntry entry = AuditEntry.Event(Guid.CreateVersion7(), "BudgetVersion", Guid.CreateVersion7(), "Adopted by resolution 2026-41", "user-1", "Dana", DateTimeOffset.UnixEpoch);

        Assert.Equal("Adopted by resolution 2026-41", entry.Description);
    }
}
