using CivicBudget.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// Lets Application services ask for an <see cref="ICivicBudgetDbContext"/> while EF Core's own
/// <see cref="IDbContextFactory{TContext}"/> does the real work. One line of glue, no logic.
/// </summary>
public sealed class CivicBudgetDbContextFactoryAdapter(IDbContextFactory<CivicBudgetDbContext> inner) : ICivicBudgetDbContextFactory
{
    public async Task<ICivicBudgetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await inner.CreateDbContextAsync(cancellationToken);
}
