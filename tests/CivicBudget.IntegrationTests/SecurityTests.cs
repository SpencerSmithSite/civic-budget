using System.IO.Compression;
using System.Security.Claims;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Notifications;
using CivicBudget.Domain.Security;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The SOC 2 controls that live in the database: the security log (every sign-in outcome, read only
/// by the government's own Administrators), the full export, removing a government that leaves,
/// and the retention job.
/// </summary>
[Collection(SqlServerTests.Name)]
public class SecurityTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Security");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<SecurityEvent>> EventsAsync()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        return await db.SecurityEvents.OrderBy(e => e.OccurredAtUtc).ToListAsync();
    }

    /// <summary>
    /// The app's sign-in manager on a request whose cookie sign-in goes nowhere. The test host has no
    /// authentication schemes (the web host adds them), so it gets an empty scheme list.
    /// </summary>
    private static SignInManager<ApplicationUser> SignIn(AsyncServiceScope scope)
    {
        var signIn = ActivatorUtilities.CreateInstance<AuditingSignInManager>(scope.ServiceProvider,
            new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())));
        signIn.Context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService, NoCookies>().BuildServiceProvider(),
        };
        return signIn;
    }

    [Fact]
    public async Task Every_sign_in_outcome_is_a_security_event()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        SignInManager<ApplicationUser> signIn = SignIn(scope);

        Assert.False((await signIn.PasswordSignInAsync("finance@mapleridge.example", "not-the-password", false, lockoutOnFailure: true)).Succeeded);
        Assert.False((await signIn.PasswordSignInAsync("nobody@nowhere.example", "anything", false, lockoutOnFailure: true)).Succeeded);
        Assert.True((await signIn.PasswordSignInAsync("finance@mapleridge.example", TestDatabase.DemoPassword, isPersistent: true, lockoutOnFailure: true)).Succeeded);

        List<SecurityEvent> events = await EventsAsync();
        Assert.Collection(events,
            failed => Assert.Equal((SecurityEventKind.SignInFailed, _mapleRidge, "finance@mapleridge.example"), (failed.Kind, failed.GovernmentId!.Value, failed.Email)),
            unknown =>
            {
                // An address with no account belongs to no government, so no Administrator sees it.
                Assert.Equal(SecurityEventKind.UnknownAccount, unknown.Kind);
                Assert.Null(unknown.GovernmentId);
                Assert.Equal("nobody@nowhere.example", unknown.Email);
            },
            signedIn =>
            {
                Assert.Equal(SecurityEventKind.SignedIn, signedIn.Kind);
                Assert.Equal("Remembered on this device", signedIn.Detail);
            });
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_the_log_says_so()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        SignInManager<ApplicationUser> signIn = SignIn(scope);

        SignInResult last = SignInResult.Failed;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            last = await signIn.PasswordSignInAsync("streets@mapleridge.example", "guess-" + attempt, false, lockoutOnFailure: true);
        }

        Assert.True(last.IsLockedOut);
        // Locked, the right password is refused too, until the lockout ends.
        Assert.True((await signIn.PasswordSignInAsync("streets@mapleridge.example", TestDatabase.DemoPassword, false, lockoutOnFailure: true)).IsLockedOut);

        List<SecurityEvent> events = await EventsAsync();
        Assert.Equal(4, events.Count(e => e.Kind == SecurityEventKind.SignInFailed));
        Assert.Equal(2, events.Count(e => e.Kind == SecurityEventKind.LockedOut));
    }

    [Fact]
    public async Task Only_an_Administrator_reads_the_security_log_and_only_their_own_government()
    {
        await using (CivicBudgetDbContext db = _database.CreateContext(tenant: null))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            db.SecurityEvents.AddRange(
                new SecurityEvent(_mapleRidge, SecurityEventKind.SignedIn, now, "u1", "admin@mapleridge.example", "203.0.113.5"),
                new SecurityEvent(_mapleRidge, SecurityEventKind.DataExported, now.AddMinutes(1), "u1", "admin@mapleridge.example", "203.0.113.5", "/admin/export/government/all-data.zip"),
                new SecurityEvent(_pineHollow, SecurityEventKind.SignedIn, now, "u2", "admin@pinehollow.example", "198.51.100.7"),
                new SecurityEvent(null, SecurityEventKind.UnknownAccount, now, null, "nobody@nowhere.example", "192.0.2.1"));
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope admin = _database.CreateScopeAs(Roles.Admin, _mapleRidge))
        {
            ISecurityLogService log = admin.ServiceProvider.GetRequiredService<ISecurityLogService>();
            SecurityLogPageDto? all = await log.ListAsync(null, 0, 50);
            Assert.NotNull(all);
            Assert.Equal(2, all.Total);
            Assert.All(all.Events, e => Assert.Equal("admin@mapleridge.example", e.Email));
            Assert.Equal(SecurityEventKind.DataExported, all.Events[0].Kind); // newest first

            SecurityLogPageDto? exports = await log.ListAsync(SecurityEventKind.DataExported, 0, 50);
            Assert.Equal(1, exports!.Total);
        }

        await using AsyncServiceScope fiscal = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        Assert.Null(await fiscal.ServiceProvider.GetRequiredService<ISecurityLogService>().ListAsync(null, 0, 50));
    }

    [Fact]
    public async Task The_full_export_holds_every_table_of_one_government_and_no_secrets()
    {
        await using AsyncServiceScope admin = _database.CreateScopeAs(Roles.Admin, _mapleRidge);
        GovernmentExportFile? file = await admin.ServiceProvider.GetRequiredService<IGovernmentExportService>().ExportAsync();
        Assert.NotNull(file);
        Assert.StartsWith("maple-ridge-oh-all-data-", file.FileName, StringComparison.Ordinal);

        using var zip = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        Dictionary<string, string> files = zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());

        Assert.Contains("README.txt", files.Keys);
        foreach (string table in new[] { "Governments", "Funds", "Accounts", "BudgetVersions", "BudgetLines", "Positions", "AuditEntries", "AspNetUsers", "AspNetUserRoles", "UserDepartments" })
        {
            Assert.Contains($"data/{table}.csv", files.Keys);
        }

        // Two-step keys and recovery codes, password hashes, and session stamps never leave.
        Assert.DoesNotContain("data/AspNetUserTokens.csv", files.Keys);
        string users = files["data/AspNetUsers.csv"];
        Assert.DoesNotContain("PasswordHash", users, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityStamp", users, StringComparison.Ordinal);
        Assert.Contains("admin@mapleridge.example", users, StringComparison.Ordinal);

        // Every row is Maple Ridge's: Pine Hollow's id appears nowhere, and the line count matches.
        string everything = string.Concat(files.Values);
        Assert.DoesNotContain(_pineHollow.ToString(), everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pinehollow.example", everything, StringComparison.Ordinal);
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        int lines = await db.BudgetLines.CountAsync();
        Assert.Equal(lines + 1, files["data/BudgetLines.csv"].Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains($"BudgetLines.csv  {lines} rows", files["README.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_an_Administrator_can_take_the_full_export()
    {
        await using AsyncServiceScope fiscal = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        Assert.Null(await fiscal.ServiceProvider.GetRequiredService<IGovernmentExportService>().ExportAsync());
    }

    [Fact]
    public void Every_table_is_reached_from_a_government_unless_it_is_shared_on_purpose()
    {
        using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        IReadOnlyList<string> covered = GovernmentDataStore.CoveredTables(db.Model);
        string[] shared = ["AspNetRoleClaims", "AspNetRoles", "DataProtectionKeys"];

        IEnumerable<string> missed = db.Model.GetRelationalModel().Tables.Select(t => t.Name).Except(covered).Except(shared);
        Assert.Empty(missed);
    }

    [Fact]
    public async Task Removing_a_government_deletes_every_row_it_had_and_nothing_else()
    {
        (string Table, int Rows)[] mapleBefore = await RowsPerTableAsync(_mapleRidge);
        await using (CivicBudgetDbContext db = _database.CreateContext(tenant: null))
        {
            db.SecurityEvents.Add(new SecurityEvent(_pineHollow, SecurityEventKind.SignedIn, DateTimeOffset.UtcNow, "u2", "admin@pinehollow.example", null));
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope scope = _database.CreateScope();
        IReadOnlyList<(string Table, int Rows)> removed = await scope.ServiceProvider.GetRequiredService<GovernmentDataStore>().DeleteAsync(_pineHollow);

        Assert.Equal(1, removed.Single(r => r.Table == "Governments").Rows);
        Assert.True(removed.Single(r => r.Table == "BudgetLines").Rows > 0);
        Assert.All(await RowsPerTableAsync(_pineHollow), t => Assert.True(t.Rows == 0 || t.Table == "SecurityEvents", $"{t.Table} still has {t.Rows} rows"));

        await using CivicBudgetDbContext check = _database.CreateContext(tenant: null);
        Assert.False(await check.Users.AnyAsync(u => u.Email!.EndsWith("@pinehollow.example")));
        Assert.Equal(1, await check.SecurityEvents.CountAsync(e => e.GovernmentId == _pineHollow)); // the log is kept for its year
        Assert.Equal(mapleBefore, await RowsPerTableAsync(_mapleRidge));
    }

    [Fact]
    public async Task Retention_removes_year_old_events_and_finished_emails_but_never_a_waiting_one()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            db.SecurityEvents.AddRange(
                new SecurityEvent(_mapleRidge, SecurityEventKind.SignedIn, now.AddDays(-400), "u1", "old@mapleridge.example", null),
                new SecurityEvent(_mapleRidge, SecurityEventKind.SignedIn, now.AddDays(-10), "u1", "recent@mapleridge.example", null));
            db.OutboxEmails.AddRange(
                new OutboxEmail(_mapleRidge, EmailKind.PasswordReset, "old-held@mapleridge.example", "Old", "Old", "Body", now.AddDays(-400), deliver: false),
                new OutboxEmail(_mapleRidge, EmailKind.PasswordReset, "old-waiting@mapleridge.example", "Old", "Old", "Body", now.AddDays(-400), deliver: true));
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope scope = _database.CreateScope();
        (int events, int emails) = await scope.ServiceProvider.GetRequiredService<RetentionService>().PurgeAsync();

        Assert.Equal((1, 1), (events, emails));
        await using CivicBudgetDbContext check = _database.CreateContext(_mapleRidge);
        Assert.Equal(["recent@mapleridge.example", null], await check.SecurityEvents.OrderBy(e => e.OccurredAtUtc).Select(e => e.Email).ToListAsync());
        Assert.Single(await check.SecurityEvents.Where(e => e.Kind == SecurityEventKind.RetentionPurge).ToListAsync());
        Assert.Equal("old-waiting@mapleridge.example", (await check.OutboxEmails.SingleAsync(e => e.ToAddress.StartsWith("old"))).ToAddress);
    }

    /// <summary>Rows per table with a GovernmentId column, read from the database's own catalog rather than the code under test.</summary>
    private async Task<(string Table, int Rows)[]> RowsPerTableAsync(Guid governmentId)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = new SqlCommand("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = 'GovernmentId' ORDER BY TABLE_NAME", connection))
        await using (SqlDataReader reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new List<(string, int)>();
        foreach (string table in tables)
        {
#pragma warning disable CA2100 // table names come from the database catalog, not from input
            await using var count = new SqlCommand($"SELECT COUNT(*) FROM [{table}] WHERE GovernmentId = @g", connection);
#pragma warning restore CA2100
            count.Parameters.AddWithValue("@g", governmentId);
            counts.Add((table, (int)(await count.ExecuteScalarAsync())!));
        }

        return [.. counts];
    }

    private sealed class NoCookies : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
