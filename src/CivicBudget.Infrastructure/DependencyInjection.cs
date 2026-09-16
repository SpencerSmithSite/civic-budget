using CivicBudget.Application.Tenancy;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Persistence.Interceptors;
using CivicBudget.Infrastructure.Seed;
using CivicBudget.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and tenancy. The Web project (composition root) calls this once.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // One AmbientTenantContext per scope, exposed through the interface the rest of the app uses.
        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddScoped<TenantSaveChangesInterceptor>();

        // Factory, not AddDbContext: see CivicBudgetDbContext remarks and docs/DECISIONS.md ADR-0003.
        // The factory itself is a singleton, so the tenant context and interceptor are resolved from
        // the *current scope's* provider via the (sp, options) overload — this is what makes the
        // per-circuit tenant flow into contexts created on that circuit.
        services.AddDbContextFactory<CivicBudgetDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()),
            ServiceLifetime.Scoped);

        services.AddScoped<DevelopmentSeeder>();

        return services;
    }
}
