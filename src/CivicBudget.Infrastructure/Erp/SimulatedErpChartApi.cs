using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;
using CivicBudget.Infrastructure.Seed;

namespace CivicBudget.Infrastructure.Erp;

/// <summary>
/// Stands in for the ERP's chart API in development and the live demo. Its chart is the demo
/// governments' seeded chart, so a fetch finds nothing to change until someone edits the chart
/// here; that is what a sync against an ERP that already matches looks like.
/// </summary>
public sealed class SimulatedErpChartApi : IErpChartApi
{
    public string Name => "ERP (simulated)";

    // The demo simulates the ERP for every government it seeds.
    public bool IsConnected(Guid governmentId) => true;

    public Task<Result<ErpChart>> FetchAsync(ErpEntity entity, CancellationToken ct = default)
    {
        (IReadOnlyList<Fund> Funds, IReadOnlyList<Department> Departments, IReadOnlyList<Account> Accounts)? chart = entity.Slug switch
        {
            MapleRidgeSeed.Slug => (MapleRidgeSeed.Funds(entity.GovernmentId), MapleRidgeSeed.Departments(entity.GovernmentId), MapleRidgeSeed.Accounts(entity.GovernmentId)),
            PineHollowSeed.Slug => (PineHollowSeed.Funds(entity.GovernmentId), PineHollowSeed.Departments(entity.GovernmentId), PineHollowSeed.Accounts(entity.GovernmentId)),
            _ => null,
        };
        if (chart is null)
        {
            return Task.FromResult(Result.Failure<ErpChart>($"The ERP has no entity set up for {entity.Name}."));
        }

        return Task.FromResult(Result.Success(new ErpChart(
            [.. chart.Value.Funds.Select(f => new ErpFund(f.Code, f.Name, f.Category, f.Description, f.IsActive))],
            [.. chart.Value.Departments.Select(d => new ErpDepartment(d.Code, d.Name, d.Description, d.IsActive))],
            [.. chart.Value.Accounts.Select(a => new ErpObject(a.Code, a.Name, a.Type, a.Category, a.IsActive))],
            entity.NumberFormat)));
    }
}
