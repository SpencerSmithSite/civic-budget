using CivicBudget.Application.Export;

namespace CivicBudget.Application.Erp;

/// <summary>
/// A budget journal the way VIP takes one, by API or import file: every line carries the full
/// account number and an amount, plus the journal's one description and one posting date. The
/// amounts are changes to the ERP's budget, not totals, so an amendment's journal holds only what moved.
/// </summary>
/// <param name="ExternalId">CivicBudget's id for the send; the ERP uses it to recognize a retry.</param>
public sealed record ErpBudgetJournal(Guid ExternalId, int FiscalYear, string Description, DateOnly Date, IReadOnlyList<ErpBudgetJournalLine> Lines);

public sealed record ErpBudgetJournalLine(string Account, decimal Amount);

/// <summary>
/// What the ERP said. A journal is posted whole or not at all: when any account is refused, nothing
/// posts, and the refused accounts come back with the ERP's reason. A call that gets no answer at
/// all throws instead, because then nobody knows whether it posted.
/// </summary>
public sealed record ErpJournalAnswer(bool Posted, string? JournalNumber, IReadOnlyDictionary<string, string> RefusedAccounts, string? Message)
{
    public static ErpJournalAnswer Accepted(string journalNumber) => new(true, journalNumber, new Dictionary<string, string>(), null);

    public static ErpJournalAnswer Refused(IReadOnlyDictionary<string, string> accounts, string message) => new(false, null, accounts, message);
}

/// <summary>
/// Posts a budget journal to the ERP. Registered only where a connection is configured (the demo
/// registers a simulated VIP); without one the send page offers the import file alone.
/// </summary>
public interface IErpBudgetApi
{
    /// <summary>For the send history and the audit trail ("VIP (simulated)").</summary>
    string Name { get; }

    /// <summary>What people call the system, for buttons ("Send to VIP").</summary>
    string SystemName { get; }

    Task<ErpJournalAnswer> PostBudgetJournalAsync(ErpEntity entity, ErpBudgetJournal journal, CancellationToken ct = default);
}

/// <summary>The import file: VIP's four columns, the description and date repeated on every line as it expects.</summary>
public static class BudgetJournalFile
{
    public static ExportTable ToTable(ErpBudgetJournal journal) => new(
        "Budget journal",
        ["Account", "Amount", "Description", "Date"],
        journal.Lines.Select(l => new object?[] { l.Account, l.Amount, journal.Description, journal.Date }).ToList());
}
