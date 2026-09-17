using CivicBudget.Application.Persistence;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;
using CivicBudget.Application.Users;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Persistence.Interceptors;
using CivicBudget.Infrastructure.Security;
using CivicBudget.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CivicBudget.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, tenancy, and Identity. The Web project (composition root) calls this once
    /// and adds the HTTP-specific pieces (cookies, authorization policies) itself.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // One CurrentUserContext per scope, exposed through the two interfaces the rest of the app uses.
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddScoped<TenantSaveChangesInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.TryAddSingleton(TimeProvider.System); // tests substitute a fake clock

        // Factory, not AddDbContext: see CivicBudgetDbContext remarks and docs/DECISIONS.md ADR-0003.
        // The factory is registered Scoped so the (sp, options) overload resolves the tenant context
        // and interceptor from the *current scope's* provider. This is what makes the per-circuit
        // user flow into contexts created on that circuit.
        services.AddDbContextFactory<CivicBudgetDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                // Audit first so the audit rows it adds are also checked by the tenant interceptor.
                .AddInterceptors(
                    sp.GetRequiredService<AuditInterceptor>(),
                    sp.GetRequiredService<TenantSaveChangesInterceptor>()),
            ServiceLifetime.Scoped);
        services.AddScoped<ICivicBudgetDbContextFactory, CivicBudgetDbContextFactoryAdapter>();

        // The portal's read-only context: same database, three tables, no tracking, no interceptors.
        // Singleton factory is fine here because nothing per-scope flows into it (tenant comes from the URL slug).
        services.AddDbContextFactory<PublicPortalDbContext>(options =>
            options
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddSingleton<IPublishedSnapshotCacheInvalidator, NoOpSnapshotCacheInvalidator>();

        // Identity core: users, roles, password hashing, lockout, tokens, sign-in. Cookie
        // authentication itself is added by the Web project because it is an HTTP pipeline concern.
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false; // accounts are created by an admin, not self-registered
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<CivicBudgetDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<DevelopmentSeeder>();

        return services;
    }
}
