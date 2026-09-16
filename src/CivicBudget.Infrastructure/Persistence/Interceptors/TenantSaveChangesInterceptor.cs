using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CivicBudget.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Write-side half of tenant isolation. Query filters stop a tenant from <em>reading</em> another
/// tenant's rows; this interceptor stops it from <em>writing</em> them. Entities carry their
/// GovernmentId from their constructor, so the job here is verification, not stamping.
/// </summary>
public sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verify(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Verify(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Verify(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (EntityEntry<ITenantOwned> entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (tenantContext.GovernmentId is not { } tenantId)
            {
                throw new TenantIsolationException(
                    $"Cannot save {entry.Metadata.ClrType.Name}: no tenant is set for this operation.");
            }

            if (entry.Entity.GovernmentId != tenantId)
            {
                throw new TenantIsolationException(
                    $"Cannot save {entry.Metadata.ClrType.Name} belonging to government {entry.Entity.GovernmentId} while acting for government {tenantId}.");
            }
        }
    }
}
