using CivicBudget.Application.Reports;
using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Reports;

/// <summary>
/// Prints a book for every published budget that has none: the demo's seeded snapshots, which are
/// written straight to the database rather than through publishing, and any budget published before
/// books existed. It acts for one government at a time, so every read stays inside the tenant filter.
/// </summary>
internal static class PublishedBookBackfill
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        await using AsyncServiceScope outer = services.CreateAsyncScope();
        ILogger logger = outer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(PublishedBookBackfill));
        var dbFactory = outer.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>();
        List<Guid> governments;
        await using (CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct))
        {
            governments = await db.Governments.Select(g => g.Id).ToListAsync(ct);
        }

        int printed = 0;
        foreach (Guid governmentId in governments)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentUserContext>().SetTenant(governmentId);
            await using CivicBudgetDbContext db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>().CreateDbContextAsync(ct);
            var missing = await db.PublishedBudgetSnapshots
                .Where(s => s.Status == SnapshotStatus.Active && !db.PublishedBudgetBooks.Any(b => b.SnapshotId == s.Id))
                .Select(s => new { s.Id, s.BudgetVersionId })
                .ToListAsync(ct);
            if (missing.Count == 0)
            {
                continue;
            }

            IBudgetBookService books = scope.ServiceProvider.GetRequiredService<IBudgetBookService>();
            BudgetBookOptions options = await books.GetDefaultsAsync(ct);
            foreach (var snapshot in missing)
            {
                if (await books.RenderAsync(snapshot.BudgetVersionId, options, ct) is { } book)
                {
                    db.PublishedBudgetBooks.Add(new PublishedBudgetBook(snapshot.Id, governmentId, book.Content, DateTimeOffset.UtcNow));
                    printed++;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        if (printed > 0)
        {
            logger.LogInformation("Printed {Count} budget book(s) for published budgets that had none.", printed);
        }
    }
}
