using CivicBudget.Application.Tenancy;
using CivicBudget.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CivicBudget.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>. Building the model needs a provider but not a live
/// database, so a placeholder connection string is enough and no app configuration is required.
/// </summary>
internal sealed class CivicBudgetDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CivicBudgetDbContext>
{
    public CivicBudgetDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<CivicBudgetDbContext> options = new DbContextOptionsBuilder<CivicBudgetDbContext>()
            .UseSqlServer("Server=design-time;Database=CivicBudget;Encrypt=False")
            .Options;

        ITenantContext noTenant = new CurrentUserContext();
        return new CivicBudgetDbContext(options, noTenant);
    }
}
