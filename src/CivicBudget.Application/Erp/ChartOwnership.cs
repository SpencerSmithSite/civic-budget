using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Erp;

/// <summary>
/// The one rule the setup services share: when the chart is managed by the ERP, funds, departments,
/// and accounts are not edited here. The screens hide the buttons; this is the check behind them.
/// </summary>
public static class ChartOwnership
{
    public const string ManagedByErp = "The chart of accounts is managed by the ERP. Sync it to bring changes in, or an Administrator can switch maintenance back to CivicBudget under Government settings.";

    /// <summary>Null when local edits are allowed; a failed Result to return otherwise.</summary>
    public static async Task<Result?> RefuseIfErpManagedAsync(ICivicBudgetDbContext db, Guid? governmentId, CancellationToken ct)
    {
        ChartSource source = await db.Governments.Where(g => g.Id == governmentId).Select(g => g.ChartSource).SingleAsync(ct);
        return source == ChartSource.Erp ? Result.Failure(ManagedByErp) : null;
    }
}
