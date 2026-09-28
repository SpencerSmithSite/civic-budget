using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Infrastructure.Seed;

namespace CivicBudget.Infrastructure.Erp;

/// <summary>
/// Stands in for the ERP's payroll API in development and the live demo. Its employees are the demo
/// governments' fictional payrolls: Maple Ridge's has moved on a little since the FY2027 budget was
/// started (a hire, a raise, a retirement), so the sync has something to show; Pine Hollow's is what a
/// government without personnel in its budget brings in. A real connection implements
/// <see cref="IErpEmployeesApi"/> the same way and nothing else changes.
/// </summary>
public sealed class SimulatedErpEmployeesApi(TimeProvider clock) : IErpEmployeesApi
{
    public string Name => "ERP (simulated)";

    public Task<Result<ErpEmployees>> FetchAsync(ErpEntity entity, CancellationToken ct = default)
    {
        IReadOnlyList<SeedEmployee>? payroll = entity.Slug switch
        {
            MapleRidgeSeed.Slug => MapleRidgePersonnel.Payroll,
            PineHollowSeed.Slug => PineHollowSeed.Payroll,
            _ => null,
        };

        return Task.FromResult(payroll is null
            ? Result.Failure<ErpEmployees>($"The ERP has no entity set up for {entity.Name}.")
            : Result.Success(new ErpEmployees(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), payroll.Select(e => e.ToErp()).ToList())));
    }
}
