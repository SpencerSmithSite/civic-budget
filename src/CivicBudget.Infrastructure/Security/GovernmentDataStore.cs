using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CivicBudget.Infrastructure.Security;

/// <summary>What a full export held: the government and the row count of each file.</summary>
public sealed record GovernmentExportSummary(string GovernmentName, string PublicSlug, IReadOnlyList<(string Table, int Rows)> Tables)
{
    public int TotalRows => Tables.Sum(t => t.Rows);
}

/// <summary>
/// Everything one government has in the database, found from the EF model rather than from a list
/// someone must remember to update. It covers the Governments row, every table with a GovernmentId
/// column, and the tables that hang off one of those without their own (a user's roles, a user's
/// departments). A table added later is covered on the day it is added.
/// <para>
/// It does two jobs. It writes that data as one CSV file per table in a ZIP, which is what a
/// government takes with it when it leaves or asks for a copy. And it deletes that data when the
/// government leaves. Both read past the tenant filter on purpose and scope by the government's id
/// in every statement, like the email sender and the Identity services; only the export service
/// (an Administrator's own government) and the operator's <c>--offboard</c> command call them.
/// </para>
/// </summary>
public sealed class GovernmentDataStore(IDbContextFactory<CivicBudgetDbContext> dbFactory, TimeProvider clock)
{
    private const string GovernmentColumn = "GovernmentId";
    private const string GovernmentsTable = "Governments";

    // Secrets that are useless outside this system and dangerous in a file: password hashes, the
    // stamps that end sessions when they change, and two-step keys and recovery codes (the tokens
    // table). They are not exported, and they are deleted with everything else.
    private static readonly HashSet<string> WithheldColumns = new(StringComparer.Ordinal) { "PasswordHash", "SecurityStamp", "ConcurrencyStamp" };
    private static readonly HashSet<string> WithheldTables = new(StringComparer.Ordinal) { "AspNetUserTokens" };

    // The security log outlives the government: it is the operator's evidence of who got in and what
    // they took away, and the retention job removes it after a year like every other event.
    private static readonly HashSet<string> KeptAfterRemoval = new(StringComparer.Ordinal) { "SecurityEvents" };

    /// <summary>Writes the ZIP to <paramref name="output"/>; null if there is no such government.</summary>
    public async Task<GovernmentExportSummary?> WriteExportAsync(Guid governmentId, Stream output, CancellationToken ct = default)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        var government = await db.Governments.Where(g => g.Id == governmentId).Select(g => new { g.Name, g.PublicSlug }).SingleOrDefaultAsync(ct);
        if (government is null)
        {
            return null;
        }

        await db.Database.OpenConnectionAsync(ct);
        DbConnection connection = db.Database.GetDbConnection();
        var counts = new List<(string Table, int Rows)>();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ScopedTable table in ScopedTables(db.Model).Where(t => !WithheldTables.Contains(t.Table.Name)).OrderBy(t => t.Table.Name, StringComparer.Ordinal))
            {
                IColumn[] columns = [.. table.Table.Columns.Where(Exportable).OrderBy(c => c.Name, StringComparer.Ordinal)];
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = table.Select(columns);
                command.Parameters.Add(new SqlParameter("@g", governmentId));

                ZipArchiveEntry entry = zip.CreateEntry($"data/{table.Table.Name}.csv", CompressionLevel.Optimal);
                await using var writer = new StreamWriter(await entry.OpenAsync(ct), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                await writer.WriteLineAsync(string.Join(',', columns.Select(c => Csv.Field(c.Name))));
                int rows = 0;
                await using DbDataReader reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    await writer.WriteLineAsync(string.Join(',', Enumerable.Range(0, columns.Length).Select(i => Csv.Value(reader.GetValue(i)))));
                    rows++;
                }

                counts.Add((table.Table.Name, rows));
            }

            var summary = new GovernmentExportSummary(government.Name, government.PublicSlug, counts);
            ZipArchiveEntry readme = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
            await using var readmeWriter = new StreamWriter(await readme.OpenAsync(ct), new UTF8Encoding(false));
            await readmeWriter.WriteAsync(Readme(summary, clock.GetUtcNow()));
            return summary;
        }
    }

    /// <summary>
    /// Deletes everything the government has, in one transaction, children before the tables they
    /// point at, and returns the rows removed per table. The security log is kept (see above).
    /// </summary>
    public async Task<IReadOnlyList<(string Table, int Rows)>> DeleteAsync(Guid governmentId, CancellationToken ct = default)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        IReadOnlyList<ScopedTable> order = DeletionOrder(ScopedTables(db.Model).Where(t => !KeptAfterRemoval.Contains(t.Table.Name)).ToList());

        // The context retries dropped connections, and a retrying context wants a transaction run
        // through its strategy so the whole thing can be replayed, not half of it.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var removed = new List<(string Table, int Rows)>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (ScopedTable table in order)
            {
                string sql = table.Delete();
                int rows = await db.Database.ExecuteSqlRawAsync(sql, [new SqlParameter("@g", governmentId)], ct);
                removed.Add((table.Table.Name, rows));
            }

            await transaction.CommitAsync(ct);
            return (IReadOnlyList<(string Table, int Rows)>)removed;
        });
    }

    /// <summary>
    /// The tables a government's data lives in. A test compares this with every table in the model,
    /// so a new table that no government reaches is a decision someone makes, not an accident.
    /// </summary>
    public static IReadOnlyList<string> CoveredTables(IModel model) => [.. ScopedTables(model).Select(t => t.Table.Name).Order(StringComparer.Ordinal)];

    /// <summary>How a table's rows are tied to one government.</summary>
    internal enum Scope
    {
        /// <summary>The Governments table itself: the row whose Id is the government.</summary>
        Government,

        /// <summary>A table with its own GovernmentId column.</summary>
        Owned,

        /// <summary>A table without one, reached through a foreign key to an owned table.</summary>
        Child,
    }

    internal sealed record ScopedTable(ITable Table, Scope Scope, IForeignKeyConstraint? Via = null)
    {
        public string Select(IEnumerable<IColumn> columns)
        {
            string list = string.Join(", ", columns.Select(c => "t." + Quote(c.Name)));
            return $"SELECT {list} FROM {Name(Table)} AS t {Where()}";
        }

        public string Delete() => $"DELETE t FROM {Name(Table)} AS t {Where()}";

        private string Where() => Scope switch
        {
            Scope.Government => "WHERE t.[Id] = @g",
            Scope.Owned => $"WHERE t.{Quote(GovernmentColumn)} = @g",
            _ => $"JOIN {Name(Via!.PrincipalTable)} AS p ON t.{Quote(Via.Columns[0].Name)} = p.{Quote(Via.PrincipalColumns[0].Name)} WHERE p.{Quote(GovernmentColumn)} = @g",
        };
    }

    /// <summary>Every table that holds a government's rows, and how.</summary>
    internal static IReadOnlyList<ScopedTable> ScopedTables(IModel model)
    {
        ITable[] tables = [.. model.GetRelationalModel().Tables];
        var owned = tables.Where(t => t.FindColumn(GovernmentColumn) is not null).ToDictionary(t => t.Name, StringComparer.Ordinal);
        var scoped = new List<ScopedTable>(owned.Values.Select(t => new ScopedTable(t, Scope.Owned)));
        scoped.AddRange(tables.Where(t => t.Name == GovernmentsTable).Select(t => new ScopedTable(t, Scope.Government)));

        foreach (ITable table in tables.Where(t => !owned.ContainsKey(t.Name) && t.Name != GovernmentsTable))
        {
            // Prefer the key that cascades: it is the parent the row belongs to (a user's department
            // link belongs to the user, and only points at the department).
            IForeignKeyConstraint? via = table.ForeignKeyConstraints
                .Where(fk => owned.ContainsKey(fk.PrincipalTable.Name))
                .OrderBy(fk => fk.OnDeleteAction == ReferentialAction.Cascade ? 0 : 1)
                .FirstOrDefault();
            if (via is not null)
            {
                if (via.Columns.Count != 1)
                {
                    throw new InvalidOperationException($"{table.Name} reaches a government through a composite key, which the export cannot follow yet.");
                }

                scoped.Add(new ScopedTable(table, Scope.Child, via));
            }
        }

        return scoped;
    }

    /// <summary>
    /// Orders tables so that each is deleted only after every table that points at it, which is what
    /// SQL Server needs where the foreign keys do not cascade. A table pointing at itself (a budget
    /// version replaced by another) is fine: one statement removes all of its rows together.
    /// </summary>
    internal static IReadOnlyList<ScopedTable> DeletionOrder(IReadOnlyList<ScopedTable> tables)
    {
        var remaining = tables.ToList();
        var order = new List<ScopedTable>();
        while (remaining.Count > 0)
        {
            // Ordinal order among the ready tables keeps the sequence the same on every run.
            ScopedTable? next = remaining
                .Where(candidate => !remaining.Any(other => other != candidate && other.Table.ForeignKeyConstraints.Any(fk => fk.PrincipalTable == candidate.Table)))
                .OrderBy(t => t.Table.Name, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"These tables point at each other, so none can be deleted first: {string.Join(", ", remaining.Select(t => t.Table.Name))}.");
            order.Add(next);
            remaining.Remove(next);
        }

        return order;
    }

    // Images (the logo, profile pictures) and row versions are binary: no use in a CSV file.
    private static bool Exportable(IColumn column) => !WithheldColumns.Contains(column.Name) && column.ProviderClrType != typeof(byte[]);

    private static string Name(ITable table) => (table.Schema is { } schema ? Quote(schema) + "." : "") + Quote(table.Name);

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string Readme(GovernmentExportSummary summary, DateTimeOffset nowUtc)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{summary.GovernmentName}: all data, exported {nowUtc:yyyy-MM-dd HH:mm} UTC");
        text.AppendLine();
        text.AppendLine("One CSV file per table in data/, with the column names in the first row.");
        text.AppendLine("UTF-8, comma separated, quoted where a value needs it. Times are UTC (ISO 8601); amounts are plain decimals.");
        text.AppendLine("Ids are the same in every file, so the files join the way the tables do.");
        text.AppendLine();
        text.AppendLine("Left out on purpose:");
        text.AppendLine("- password hashes, the stamps that end sessions, and two-step sign-in keys and recovery codes;");
        text.AppendLine("- images (the logo and profile pictures), which are binary.");
        text.AppendLine();
        text.AppendLine("Text that begins with = + - or @ has an apostrophe in front, so a spreadsheet shows it instead of running it as a formula.");
        text.AppendLine();
        text.AppendLine("Files:");
        foreach ((string table, int rows) in summary.Tables)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {table}.csv  {rows} row{(rows == 1 ? "" : "s")}");
        }

        return text.ToString();
    }

    /// <summary>RFC 4180 fields, plus the spreadsheet formula guard on text.</summary>
    internal static class Csv
    {
        public static string Value(object? value) => value switch
        {
            null or DBNull => "",
            string text => Field(Guard(text)),
            DateTimeOffset time => time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            DateTime time => time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => Field(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => Field(value.ToString() ?? ""),
        };

        public static string Field(string text) =>
            text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

        // A cell that starts with one of these is a formula to Excel and Sheets; an exported comment
        // must not become one when an auditor opens the file.
        private static string Guard(string text) => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
    }
}
