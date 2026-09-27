namespace CivicBudget.Application.Erp;

/// <summary>A line of the budget being sent.</summary>
public sealed record JournalSourceLine(LineKey Key, string AccountNumber, string AccountName, decimal Amount);

/// <summary>An amount the ERP already holds for the year, from a journal it took earlier.</summary>
public sealed record SentAmount(LineKey Key, string AccountNumber, decimal Amount);

/// <summary>One account's move: what the ERP has, what the budget says, and the difference the journal posts.</summary>
public sealed record JournalChange(LineKey Key, string AccountNumber, string AccountName, decimal InErp, decimal InBudget)
{
    public decimal Change => InBudget - InErp;
}

/// <summary>
/// Works out a budget journal as a pure function: for every account in the budget or already in the
/// ERP, the budget's amount less what earlier journals posted. The first send of a year is therefore
/// the whole budget; an amendment's is only what it changed; a line removed from the budget sends
/// its amount back out; and a budget the ERP already matches produces nothing, which is what makes
/// pressing Send twice harmless.
/// </summary>
public static class BudgetJournalBuilder
{
    public static IReadOnlyList<JournalChange> Changes(IEnumerable<JournalSourceLine> budget, IEnumerable<SentAmount> alreadySent)
    {
        var inBudget = budget.ToDictionary(l => l.Key);
        var inErp = alreadySent.GroupBy(s => s.Key).ToDictionary(g => g.Key, g => (g.First().AccountNumber, Amount: g.Sum(s => s.Amount)));

        return inBudget.Keys.Union(inErp.Keys)
            .Select(key =>
            {
                inBudget.TryGetValue(key, out JournalSourceLine? line);
                (string AccountNumber, decimal Amount) sent = inErp.GetValueOrDefault(key);
                return new JournalChange(key, line?.AccountNumber ?? sent.AccountNumber, line?.AccountName ?? "", sent.Amount, line?.Amount ?? 0m);
            })
            .Where(c => c.Change != 0m)
            .OrderBy(c => c.AccountNumber, StringComparer.Ordinal)
            .ToList();
    }
}
