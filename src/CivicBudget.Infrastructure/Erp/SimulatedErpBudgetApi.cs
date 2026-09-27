using System.Collections.Concurrent;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Infrastructure.Seed;

namespace CivicBudget.Infrastructure.Erp;

/// <summary>
/// Stands in for the ERP's budget journal API in development and the live demo. It behaves the way the
/// rest of CivicBudget has to cope with: it knows only the accounts set up in its own chart (the
/// demo governments' seeded accounts) and refuses a journal whole when any line names another; and
/// it remembers each journal's id, so a retry after a lost answer gets the first answer back
/// instead of posting twice. Its memory lasts as long as the process, which is enough for a stand-in.
/// </summary>
public sealed class SimulatedErpBudgetApi : IErpBudgetApi
{
    private readonly ConcurrentDictionary<Guid, ErpJournalAnswer> _answers = new();
    private int _nextJournal = 300;

    public string Name => "ERP (simulated)";

    public Task<ErpJournalAnswer> PostBudgetJournalAsync(ErpEntity entity, ErpBudgetJournal journal, CancellationToken ct = default) =>
        Task.FromResult(_answers.GetOrAdd(journal.ExternalId, _ => Post(entity, journal)));

    private ErpJournalAnswer Post(ErpEntity entity, ErpBudgetJournal journal)
    {
        IReadOnlyList<SeedLine>? lines = entity.Slug switch
        {
            MapleRidgeSeed.Slug => MapleRidgeSeed.Lines,
            PineHollowSeed.Slug => PineHollowSeed.Lines,
            _ => null,
        };
        if (lines is null)
        {
            return ErpJournalAnswer.Refused(new Dictionary<string, string>(), $"The ERP has no entity set up for {entity.Name}.");
        }

        var chart = lines.Select(l => AccountNumber.Compose(entity.NumberFormat, l.FundCode, l.DepartmentCode, l.AccountCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var refused = journal.Lines.Where(l => !chart.Contains(l.Account))
            .ToDictionary(l => l.Account, _ => "Account is not set up in the ERP. Add it to the ERP's chart, then send again.");
        if (refused.Count > 0)
        {
            return ErpJournalAnswer.Refused(refused, $"The ERP posted nothing: {refused.Count} account{(refused.Count == 1 ? " is" : "s are")} not set up.");
        }

        return ErpJournalAnswer.Accepted($"BJ{journal.FiscalYear}-{Interlocked.Increment(ref _nextJournal):00000}");
    }
}
