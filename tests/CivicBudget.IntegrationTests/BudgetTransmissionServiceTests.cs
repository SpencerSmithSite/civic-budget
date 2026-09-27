using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Erp;
using CivicBudget.Infrastructure.Erp;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Sending the adopted budget to the ERP: only what changed since the ERP last took the year, by API or by
/// import file, refused whole when the ERP lacks an account, retried safely after a lost answer, and
/// never two unfinished sends for one year.
/// </summary>
[Collection(SqlServerTests.Name)]
public class BudgetTransmissionServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _original2026;
    private Guid _amendment2026;
    private Guid _draft2027;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Send");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        var versions = await db.BudgetVersions.IgnoreQueryFilters()
            .Join(db.FiscalYears.IgnoreQueryFilters(), v => v.FiscalYearId, f => f.Id, (v, f) => new { v.Id, v.GovernmentId, f.Year, v.VersionNumber })
            .Where(x => x.GovernmentId == _mapleRidge).ToListAsync();
        _original2026 = versions.Single(v => v.Year == 2026 && v.VersionNumber == 1).Id;
        _amendment2026 = versions.Single(v => v.Year == 2026 && v.VersionNumber == 2).Id;
        _draft2027 = versions.Single(v => v.Year == 2027).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_amendment_sends_only_the_supplemental_appropriation_and_then_vip_has_the_budget()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetTransmissionService sends = scope.ServiceProvider.GetRequiredService<IBudgetTransmissionService>();

        SendPageDto page = (await sends.GetAsync(_amendment2026))!;
        Assert.Null(page.CannotSendReason);
        Assert.Equal(("ERP (simulated)", new DateOnly(2026, 6, 15)), (page.ApiName, page.DefaultPostingDate)); // adopted June 15
        Assert.Equal("FY2026 Amendment 1, resolution 2026-11", page.DefaultDescription);
        Assert.Equal([("1000-110-5120", 53_000m), ("1000-110-5310", 71_000m)], page.Changes.Select(c => (c.AccountNumber, c.InBudget)));
        Assert.All(page.Changes, c => Assert.True(c.InErp > 0m));                // the original's amounts, sent in January

        Result<TransmissionDto> sent = await sends.SendAsync(new SendBudgetRequest(_amendment2026, page.DefaultDescription, page.DefaultPostingDate));

        Assert.True(sent.IsSuccess, string.Join("; ", sent.Errors.Select(e => e.Message)));
        Assert.Equal(TransmissionStatus.Accepted, sent.Value.Status);
        Assert.StartsWith("BJ2026-", sent.Value.ErpReference, StringComparison.Ordinal);
        Assert.Equal(page.Changes.Sum(c => c.Change), sent.Value.Net);
        Assert.Empty((await sends.GetAsync(_amendment2026))!.Changes);
        Result<TransmissionDto> again = await sends.SendAsync(new SendBudgetRequest(_amendment2026, "Again", page.DefaultPostingDate));
        Assert.Equal("The ERP already has this budget. There is nothing to send.", again.Errors.Single().Message);

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.Contains(await db.AuditEntries.Where(a => a.EntityId == _amendment2026).Select(a => a.Description).ToListAsync(),
            d => d != null && d.StartsWith($"Sent FY2026 Amendment 1 to ERP (simulated) as journal {sent.Value.ErpReference}: 2 lines, net +$", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_the_latest_adopted_version_goes_and_only_within_its_year()
    {
        await using AsyncServiceScope scope = As(Roles.Admin);
        IBudgetTransmissionService sends = scope.ServiceProvider.GetRequiredService<IBudgetTransmissionService>();

        Assert.Contains("later amendment replaced", (await sends.GetAsync(_original2026))!.CannotSendReason, StringComparison.Ordinal);
        Assert.Contains("Only an adopted budget", (await sends.GetAsync(_draft2027))!.CannotSendReason, StringComparison.Ordinal);
        Assert.True((await sends.SendAsync(new SendBudgetRequest(_draft2027, "FY2027", new DateOnly(2027, 1, 1)))).IsFailure);

        Result<TransmissionDto> wrongDate = await sends.SendAsync(new SendBudgetRequest(_amendment2026, "FY2026 Amendment 1", new DateOnly(2027, 1, 4)));
        Assert.Equal(nameof(SendBudgetRequest.PostingDate), wrongDate.Errors.Single().PropertyName);
        Result<TransmissionDto> noDescription = await sends.SendAsync(new SendBudgetRequest(_amendment2026, "  ", new DateOnly(2026, 6, 15)));
        Assert.Equal(nameof(SendBudgetRequest.Description), noDescription.Errors.Single().PropertyName);
    }

    [Fact]
    public async Task An_import_file_holds_the_year_until_someone_confirms_it_was_loaded()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetTransmissionService sends = scope.ServiceProvider.GetRequiredService<IBudgetTransmissionService>();
        var request = new SendBudgetRequest(_amendment2026, "FY2026 supplemental", new DateOnly(2026, 6, 15));

        Result<TransmissionDto> file = await sends.CreateFileAsync(request);

        Assert.Equal((TransmissionStatus.AwaitingImport, "ERP import file"), (file.Value.Status, file.Value.TargetName));
        (string name, Application.Export.ExportTable table) = (await sends.FileAsync(file.Value.Id))!.Value;
        Assert.Equal("budget-journal-fy2026-amendment-1.csv", name);
        Assert.Equal(["Account", "Amount", "Description", "Date"], table.Headers);
        Assert.All(table.Rows, r => Assert.Equal(("FY2026 supplemental", new DateOnly(2026, 6, 15)), ((string)r[2]!, (DateOnly)r[3]!)));

        // Nothing else may start for the year while the file is out, not even by API.
        Assert.Contains("not finished", (await sends.SendAsync(request)).Errors.Single().Message, StringComparison.Ordinal);
        Assert.Equal(file.Value.Id, (await sends.GetAsync(_amendment2026))!.Open!.Id);

        Assert.True((await sends.ConfirmImportedAsync(file.Value.Id, "BJ2026-00410")).IsSuccess);

        SendPageDto after = (await sends.GetAsync(_amendment2026))!;
        Assert.Null(after.Open);
        Assert.Empty(after.Changes);                                                     // an imported file counts as sent
        Assert.Equal("BJ2026-00410", after.History[0].ErpReference);
    }

    [Fact]
    public async Task Erp_refusing_an_account_posts_nothing_and_leaves_the_year_ready_to_send_again()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IServiceProvider sp = scope.ServiceProvider;
        IBudgetWorkflowService workflow = sp.GetRequiredService<IBudgetWorkflowService>();
        IBudgetEntryService entry = sp.GetRequiredService<IBudgetEntryService>();

        // Amendment 2 adds Building & Zoning fuel, an account the ERP has never had.
        Guid amendment2 = (await workflow.CreateAmendmentAsync(_amendment2026, "Zoning vehicle")).Value;
        BudgetWorkspaceDto workspace = (await entry.GetWorkspaceAsync(amendment2))!;
        Guid general = workspace.Funds.Single(f => f.Code == "1000").Id, zoning = workspace.Departments.Single(d => d.Code == "410").Id, fuel = workspace.Accounts.Single(a => a.Code == "5420").Id;
        Assert.True((await entry.AddLineAsync(new AddBudgetLineRequest(amendment2, general, zoning, fuel, 900m, 0m, 0m, null))).IsSuccess);
        Assert.True((await workflow.ProposeAsync(amendment2, acknowledgeWarnings: true)).IsSuccess);
        Assert.True((await workflow.AdoptAsync(amendment2, "2026-30", acknowledgeWarnings: true)).IsSuccess);

        IBudgetTransmissionService sends = sp.GetRequiredService<IBudgetTransmissionService>();
        SendPageDto page = (await sends.GetAsync(amendment2))!;
        Assert.Contains(page.Changes, c => c.AccountNumber == "1000-410-5420" && c.Change == 900m);

        TransmissionDto refused = (await sends.SendAsync(new SendBudgetRequest(amendment2, page.DefaultDescription, page.DefaultPostingDate))).Value;

        Assert.Equal(TransmissionStatus.Rejected, refused.Status);
        Assert.Equal("Account is not set up in the ERP. Add it to the ERP's chart, then send again.", refused.Lines.Single(l => l.AccountNumber == "1000-410-5420").RefusedReason);
        SendPageDto after = (await sends.GetAsync(amendment2))!;
        Assert.Equal(page.Changes.Count, after.Changes.Count);                            // nothing counted as sent
        Assert.Null(after.Open);                                                         // and nothing is holding the year
    }

    [Fact]
    public async Task A_lost_answer_is_retried_under_the_same_id_and_vip_posts_it_once()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IServiceProvider sp = scope.ServiceProvider;
        var vip = new LosesFirstAnswer(new SimulatedErpBudgetApi());
        var sends = new BudgetTransmissionService(sp.GetRequiredService<ICivicBudgetDbContextFactory>(), sp.GetRequiredService<ICurrentUser>(), [vip], TimeProvider.System);

        TransmissionDto failed = (await sends.SendAsync(new SendBudgetRequest(_amendment2026, "FY2026 Amendment 1", new DateOnly(2026, 6, 15)))).Value;

        Assert.Equal(TransmissionStatus.Failed, failed.Status);
        Assert.Contains("No answer from ERP (simulated)", failed.Message, StringComparison.Ordinal);
        Assert.Equal(failed.Id, (await sends.GetAsync(_amendment2026))!.Open!.Id);       // the year is held until it is settled

        TransmissionDto retried = (await sends.RetryAsync(failed.Id)).Value;

        Assert.Equal((failed.Id, TransmissionStatus.Accepted), (retried.Id, retried.Status));
        Assert.Equal(vip.FirstJournalNumber, retried.ErpReference);                      // the ERP recognized the id: one journal, not two
        Assert.Equal([failed.Id, failed.Id], vip.ExternalIds);
    }

    [Fact]
    public async Task The_database_refuses_a_second_unfinished_send_for_a_year()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        BudgetTransmission Open() => new(_mapleRidge, _amendment2026, 2026, TransmissionMethod.File, "ERP import file", "x", new DateOnly(2026, 6, 15), "u", "n", DateTimeOffset.UtcNow);

        db.BudgetTransmissions.Add(Open());
        await db.SaveChangesAsync();
        db.BudgetTransmissions.Add(Open());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(Roles.DepartmentHead)]
    [InlineData(Roles.Viewer)]
    public async Task Only_the_fiscal_officer_or_an_administrator_sends(string role)
    {
        await using AsyncServiceScope scope = As(role);
        IBudgetTransmissionService sends = scope.ServiceProvider.GetRequiredService<IBudgetTransmissionService>();

        Assert.Null(await sends.GetAsync(_amendment2026));
        Assert.True((await sends.SendAsync(new SendBudgetRequest(_amendment2026, "x", new DateOnly(2026, 6, 15)))).IsFailure);
        Assert.True((await sends.CreateFileAsync(new SendBudgetRequest(_amendment2026, "x", new DateOnly(2026, 6, 15)))).IsFailure);
    }

    /// <summary>The ERP posts the journal, but the answer never arrives: the case a retry has to survive.</summary>
    private sealed class LosesFirstAnswer(IErpBudgetApi inner) : IErpBudgetApi
    {
        public List<Guid> ExternalIds { get; } = [];
        public string? FirstJournalNumber { get; private set; }
        public string Name => inner.Name;

        public async Task<ErpJournalAnswer> PostBudgetJournalAsync(ErpEntity entity, ErpBudgetJournal journal, CancellationToken ct = default)
        {
            ExternalIds.Add(journal.ExternalId);
            ErpJournalAnswer answer = await inner.PostBudgetJournalAsync(entity, journal, ct);
            if (FirstJournalNumber is null)
            {
                FirstJournalNumber = answer.JournalNumber;
                throw new TimeoutException("The operation timed out.");
            }

            return answer;
        }
    }

    private AsyncServiceScope As(string role)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
