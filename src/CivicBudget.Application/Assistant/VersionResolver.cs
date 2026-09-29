using CivicBudget.Application.Budgets;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Assistant;

/// <summary>
/// Which budget a tool means when the model does not name one, the same way for every tool: the one
/// on the user's page, else the current fiscal year's latest adopted version, else the newest adopted,
/// else the newest of all. For actuals the page's version counts only if it is this year's: on next
/// year's draft, "how are we doing" is about the year under way, which is the only one with books.
/// </summary>
public sealed class VersionResolver(ICivicBudgetDbContextFactory dbFactory, ICurrentUser currentUser, IBudgetEntryService entry, TimeProvider clock)
{
    public async Task<BudgetVersionSummaryDto?> ResolveAsync(AssistantTurn turn, string? versionId, CancellationToken ct, bool forActuals = false)
    {
        IReadOnlyList<BudgetVersionSummaryDto> versions = await entry.ListVersionsAsync(ct);
        if (versionId is { Length: > 0 })
        {
            return Guid.TryParse(versionId, out Guid id) ? versions.FirstOrDefault(v => v.Id == id) : null;
        }

        int currentYear = await CurrentFiscalYearAsync(ct);
        if (turn.PageVersionId is { } onPage && versions.FirstOrDefault(v => v.Id == onPage) is { } shown && (!forActuals || shown.Year == currentYear))
        {
            return shown;
        }

        IEnumerable<BudgetVersionSummaryDto> adopted = versions.Where(v => v.Status == BudgetStatus.Adopted).OrderByDescending(v => v.Year).ThenByDescending(v => v.VersionNumber);
        return adopted.FirstOrDefault(v => v.Year == currentYear) ?? adopted.FirstOrDefault() ?? versions.OrderByDescending(v => v.Year).ThenByDescending(v => v.VersionNumber).FirstOrDefault();
    }

    /// <summary>A budget still open for changes: the one named, else the one on the page, else the open version (Draft or Proposed).</summary>
    public async Task<BudgetVersionSummaryDto?> ResolveOpenAsync(AssistantTurn turn, string? versionId, CancellationToken ct)
    {
        IReadOnlyList<BudgetVersionSummaryDto> versions = await entry.ListVersionsAsync(ct);
        if (versionId is { Length: > 0 })
        {
            return Guid.TryParse(versionId, out Guid id) ? versions.FirstOrDefault(v => v.Id == id) : null;
        }

        if (turn.PageVersionId is { } onPage && versions.FirstOrDefault(v => v.Id == onPage) is { Status: not BudgetStatus.Adopted } shown)
        {
            return shown;
        }

        return versions.Where(v => v.Status != BudgetStatus.Adopted).OrderByDescending(v => v.Year).ThenByDescending(v => v.VersionNumber).FirstOrDefault();
    }

    public async Task<int> CurrentFiscalYearAsync(CancellationToken ct)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int startMonth = await db.Governments.Where(g => g.Id == currentUser.GovernmentId).Select(g => g.FiscalYearStartMonth).SingleAsync(ct);
        DateOnly today = Common.OhioTime.DateOf(clock.GetUtcNow());
        return startMonth == 1 || today.Month < startMonth ? today.Year : today.Year + 1;
    }

    public static string Name(BudgetVersionSummaryDto v) => $"FY{v.Year} {v.Label} ({v.Status})";
}
