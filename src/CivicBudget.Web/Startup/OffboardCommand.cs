using System.Globalization;
using CivicBudget.Domain.Security;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Web.Startup;

/// <summary>
/// <c>dotnet CivicBudget.Web.dll --offboard --slug cedar-falls-oh --export-to /secure/cedar-falls.zip --confirm cedar-falls-oh</c>
/// <para>
/// Removes a government that has left: first writes its full export (the same ZIP an Administrator
/// downloads) to the path given, then deletes every row it has, in one transaction. Nothing is
/// deleted unless the export was written, and the slug must be typed twice so a paste into the wrong
/// terminal cannot do it. The security log is kept for its year, with an event saying who ran this.
/// Backups still hold the data until they age out (see docs/security/data-retention.md).
/// </para>
/// </summary>
public static class OffboardCommand
{
    public const string Flag = "--offboard";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--slug") is not { } slug || Arg("--export-to") is not { } exportPath)
        {
            await Console.Error.WriteLineAsync("Usage: --offboard --slug <portal-address> --export-to <file.zip> --confirm <portal-address> [--by <your name>]");
            return 2;
        }

        if (!string.Equals(Arg("--confirm"), slug, StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync($"This deletes everything {slug} has. Repeat the address after --confirm to go ahead.");
            return 2;
        }

        if (File.Exists(exportPath))
        {
            await Console.Error.WriteLineAsync($"{exportPath} already exists; choose a new file so no earlier export is overwritten.");
            return 1;
        }

        await DatabaseInitializer.MigrateAsync(services);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CivicBudgetDbContext>>();
        Guid governmentId;
        await using (CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync())
        {
            governmentId = await db.Governments.Where(g => g.PublicSlug == slug).Select(g => g.Id).SingleOrDefaultAsync();
        }

        if (governmentId == Guid.Empty)
        {
            await Console.Error.WriteLineAsync($"No government has the address {slug}.");
            return 1;
        }

        GovernmentDataStore store = scope.ServiceProvider.GetRequiredService<GovernmentDataStore>();
        GovernmentExportSummary? export;
        await using (FileStream file = new(exportPath, FileMode.CreateNew, FileAccess.Write))
        {
            export = await store.WriteExportAsync(governmentId, file);
        }

        if (export is null)
        {
            await Console.Error.WriteLineAsync("The export could not be written, so nothing was deleted.");
            return 1;
        }

        Console.WriteLine($"Exported {export.TotalRows} rows of {export.GovernmentName} to {exportPath}.");
        IReadOnlyList<(string Table, int Rows)> removed = await store.DeleteAsync(governmentId);
        int total = removed.Sum(r => r.Rows);

        string by = Arg("--by") ?? Environment.UserName;
        await using (CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync())
        {
            db.SecurityEvents.Add(new SecurityEvent(governmentId, SecurityEventKind.GovernmentRemoved, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
                null, null, null, string.Create(CultureInfo.InvariantCulture, $"{export.GovernmentName} ({slug}) removed by {by}: {total} rows")));
            await db.SaveChangesAsync();
        }

        foreach ((string table, int rows) in removed.Where(r => r.Rows > 0))
        {
            Console.WriteLine($"  {table}: {rows}");
        }

        Console.WriteLine($"Removed {export.GovernmentName}: {total} rows. Keep {exportPath} somewhere safe, or hand it to the government.");
        return 0;
    }
}
