using System.Collections.Concurrent;
using System.Globalization;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CivicBudget.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes the audit trail. Runs inside SaveChanges, walks the change tracker, and for every entity
/// marked <see cref="AuditedAttribute"/> appends <see cref="AuditEntry"/> rows: one for a create or
/// delete, one per changed property for an update. The rows are added to the same context before the
/// save proceeds, so they commit in the same transaction as the change they describe: an audited
/// change without its audit row cannot happen.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private const string SystemUser = "system";

    // Reflection once per type, not once per save.
    private static readonly ConcurrentDictionary<Type, bool> IsAuditedCache = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AppendAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AppendAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AppendAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        string userId = currentUser.UserId ?? SystemUser;
        string userName = currentUser.DisplayName ?? SystemUser;
        DateTimeOffset now = clock.GetUtcNow();

        // Materialize first: adding audit rows while iterating the tracker would modify the collection.
        List<EntityEntry> audited = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && IsAudited(e.Entity.GetType()))
            .ToList();

        var entries = new List<AuditEntry>();
        foreach (EntityEntry entry in audited)
        {
            if (entry.Entity is not Entity entity || GovernmentIdOf(entry.Entity) is not { } governmentId)
            {
                continue;
            }

            string entityName = entry.Metadata.ClrType.Name;
            switch (entry.State)
            {
                case EntityState.Added:
                    entries.Add(AuditEntry.Created(governmentId, entityName, entity.Id, userId, userName, now));
                    break;

                case EntityState.Deleted:
                    entries.Add(AuditEntry.Deleted(governmentId, entityName, entity.Id, userId, userName, now));
                    break;

                case EntityState.Modified:
                    foreach (PropertyEntry property in entry.Properties.Where(p => p.IsModified && !p.Metadata.IsPrimaryKey() && !IsOptedOut(p)))
                    {
                        string? oldValue = Format(property.OriginalValue);
                        string? newValue = Format(property.CurrentValue);
                        if (oldValue != newValue)
                        {
                            entries.Add(AuditEntry.FieldChanged(governmentId, entityName, entity.Id, property.Metadata.Name, oldValue, newValue, userId, userName, now));
                        }
                    }

                    break;
            }
        }

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }
    }

    private static bool IsAudited(Type type) =>
        IsAuditedCache.GetOrAdd(type, t => t.GetCustomAttributes(typeof(AuditedAttribute), inherit: false).Length > 0);

    /// <summary>Tenant-owned entities know their government; the Government row is its own tenant.</summary>
    /// <summary>Properties marked [NotAudited]; the domain records those changes as named events instead.</summary>
    private static bool IsOptedOut(PropertyEntry property) =>
        property.Metadata.PropertyInfo?.IsDefined(typeof(NotAuditedAttribute), inherit: false) == true;

    private static Guid? GovernmentIdOf(object entity) => entity switch
    {
        ITenantOwned owned => owned.GovernmentId,
        Government government => government.Id,
        _ => null,
    };

    /// <summary>Values are stored as text for people to read, so the formatting is fixed and culture-free.</summary>
    private static string? Format(object? value) => value switch
    {
        null => null,
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
