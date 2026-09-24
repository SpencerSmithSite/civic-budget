using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The whole lifecycle against seeded Maple Ridge: limit enforcement, transitions, amendments,
/// publishing, and what the read-only portal context can and cannot see.
/// </summary>
[Collection(SqlServerTests.Name)]
public class WorkflowAndPublishingTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _streetFund;

    public async Task InitializeAsync()
    {
        // Each test adopts, publishes, or amends, so each gets its own freshly seeded database.
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Workflow");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _streetFund = (await scoped.Funds.SingleAsync(f => f.Code == "2011")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope As(string role)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    private async Task<Guid> FixStreetFundAsync(IServiceProvider services) =>
        (await services.GetRequiredService<IBudgetEntryService>().SetBeginningBalanceAsync(_draft2027, _streetFund, 100_000m)).IsSuccess
            ? _draft2027
            : throw new InvalidOperationException("Could not fix the Street fund.");

    // ---- enforcement -----------------------------------------------------------------------------

    [Fact]
    public async Task Block_mode_refuses_to_propose_while_a_fund_is_over_its_limit()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();

        WorkflowStateDto state = (await workflow.GetStateAsync(_draft2027))!;
        Result proposed = await workflow.ProposeAsync(_draft2027, acknowledgeWarnings: true);

        Assert.True(state.BlocksTransition);
        Assert.True(proposed.IsFailure);
        Assert.Contains("estimated resources", proposed.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Equal(BudgetStatus.Draft, (await workflow.GetStateAsync(_draft2027))!.Status);
    }

    [Fact]
    public async Task Warn_mode_requires_an_acknowledgement_and_then_allows_it()
    {
        // Settings are the Administrator's; the budget moves under the Fiscal Officer.
        await using (AsyncServiceScope admin = As(Roles.Admin))
        {
            Result warn = await admin.ServiceProvider.GetRequiredService<IGovernmentSettingsService>().UpdateAsync(
                new UpdateGovernmentSettingsRequest("Village of Maple Ridge", "maple-ridge-oh", AppropriationLimitMode.Warn, null, 4, 3, 4, "-", "Program"));
            Assert.True(warn.IsSuccess);
        }

        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();

        WorkflowStateDto state = (await workflow.GetStateAsync(_draft2027))!;
        Result unacknowledged = await workflow.ProposeAsync(_draft2027, acknowledgeWarnings: false);
        Result acknowledged = await workflow.ProposeAsync(_draft2027, acknowledgeWarnings: true);

        Assert.False(state.BlocksTransition);
        Assert.True(state.RequiresAcknowledgement);
        Assert.True(unacknowledged.IsFailure);
        Assert.True(acknowledged.IsSuccess);
        Assert.Equal(BudgetStatus.Proposed, (await workflow.GetStateAsync(_draft2027))!.Status);
    }

    [Fact]
    public async Task Only_the_fiscal_authority_moves_the_workflow()
    {
        await using AsyncServiceScope fd = As(Roles.FinanceDirector);
        await FixStreetFundAsync(fd.ServiceProvider);

        await using AsyncServiceScope admin = As(Roles.Admin);
        Assert.True((await admin.ServiceProvider.GetRequiredService<IBudgetWorkflowService>().GetStateAsync(_draft2027))!.CanPropose); // the Administrator may

        foreach (string role in new[] { Roles.DepartmentHead, Roles.Viewer })
        {
            await using AsyncServiceScope scope = As(role);
            IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();
            WorkflowStateDto state = (await workflow.GetStateAsync(_draft2027))!;

            Assert.False(state.CanPropose);
            Assert.True((await workflow.ProposeAsync(_draft2027, true)).IsFailure);
            Assert.True((await workflow.CreateAmendmentAsync(_draft2027, "Not theirs to start")).IsFailure);

            IPublishingService publishing = scope.ServiceProvider.GetRequiredService<IPublishingService>();
            Result<Guid> publish = await publishing.PublishAsync(_draft2027);
            Assert.Contains("Administrator or the Fiscal Officer", publish.Errors.Single().Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Only_the_fiscal_authority_unpublishes()
    {
        Guid snapshotId;
        await using (AsyncServiceScope fd = As(Roles.FinanceDirector))
        {
            snapshotId = (await fd.ServiceProvider.GetRequiredService<IPublishingService>().ListAsync()).First(s => s.Status == SnapshotStatus.Active).Id;
        }

        foreach (string role in new[] { Roles.DepartmentHead, Roles.Viewer })
        {
            await using AsyncServiceScope scope = As(role);
            Result unpublish = await scope.ServiceProvider.GetRequiredService<IPublishingService>().UnpublishAsync(snapshotId);
            Assert.True(unpublish.IsFailure, role);
        }

        await using AsyncServiceScope officer = As(Roles.FinanceDirector);
        Assert.Equal(SnapshotStatus.Active, (await officer.ServiceProvider.GetRequiredService<IPublishingService>().ListAsync()).Single(s => s.Id == snapshotId).Status);
    }

    // ---- transitions and amendments -----------------------------------------------------------

    [Fact]
    public async Task A_new_year_starts_empty_or_from_last_years_adopted_budget_changed_by_a_percentage()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();
        IFiscalYearService years = scope.ServiceProvider.GetRequiredService<IFiscalYearService>();
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        await FixStreetFundAsync(scope.ServiceProvider);

        Guid fy2028 = (await years.CreateAsync(new CreateFiscalYearRequest(2028))).Value;
        Result<StartBudgetResultDto> tooEarly = await workflow.StartBudgetAsync(new StartBudgetRequest(fy2028, StartFromPriorYear: true, 3m));
        Assert.Contains("FY2027 has no adopted budget", tooEarly.Errors.Single().Message, StringComparison.Ordinal);
        Assert.Null((await years.ListAsync()).Single(y => y.Year == 2028).StartFrom);

        Assert.True((await workflow.ProposeAsync(_draft2027, false)).IsSuccess);
        Assert.True((await workflow.AdoptAsync(_draft2027, "2027-50", false)).IsSuccess);
        Assert.Equal("Original", (await years.ListAsync()).Single(y => y.Year == 2028).StartFrom);

        Result<StartBudgetResultDto> started = await workflow.StartBudgetAsync(new StartBudgetRequest(fy2028, StartFromPriorYear: true, 3m, SeedAdjustmentScope.AppropriationsOnly, RoundToWholeDollars: true));
        Assert.True(started.IsSuccess, string.Join("; ", started.Errors.Select(e => e.Message)));

        BudgetWorkspaceDto prior = (await entry.GetWorkspaceAsync(_draft2027))!;
        BudgetWorkspaceDto next = (await entry.GetWorkspaceAsync(started.Value.VersionId))!;
        Assert.Equal(prior.Lines.Count, started.Value.LineCount);
        BudgetLineDto priorSalaries = prior.Lines.First(l => l.AccountType == AccountType.Expenditure);
        BudgetLineDto nextSalaries = next.Lines.Single(l => l.FundId == priorSalaries.FundId && l.DepartmentId == priorSalaries.DepartmentId && l.AccountId == priorSalaries.AccountId);
        Assert.Equal(priorSalaries.Amount, nextSalaries.CurrentYearBudget);
        Assert.Equal(Math.Round(priorSalaries.Amount * 1.03m, 0, MidpointRounding.AwayFromZero), nextSalaries.Amount);
        BudgetLineDto priorRevenue = prior.Lines.First(l => l.AccountType == AccountType.Revenue);
        Assert.Equal(Math.Round(priorRevenue.Amount, 0, MidpointRounding.AwayFromZero), next.Lines.Single(l => l.FundId == priorRevenue.FundId && l.AccountId == priorRevenue.AccountId && l.DepartmentId == priorRevenue.DepartmentId).Amount);

        // Once a year has a budget, it is not started again; a closed year is not started at all.
        Assert.Contains("already has a budget", (await workflow.StartBudgetAsync(new StartBudgetRequest(fy2028, false))).Errors.Single().Message, StringComparison.Ordinal);
        Guid fy2029 = (await years.CreateAsync(new CreateFiscalYearRequest(2029))).Value;
        Assert.True((await years.SetClosedAsync(fy2029, true)).IsSuccess);
        Assert.True((await workflow.StartBudgetAsync(new StartBudgetRequest(fy2029, false))).IsFailure);
    }

    [Fact]
    public async Task Only_the_fiscal_authority_starts_a_budget()
    {
        Guid fy2030;
        await using (AsyncServiceScope fd = As(Roles.FinanceDirector))
        {
            fy2030 = (await fd.ServiceProvider.GetRequiredService<IFiscalYearService>().CreateAsync(new CreateFiscalYearRequest(2030))).Value;
        }

        foreach (string role in new[] { Roles.DepartmentHead, Roles.Viewer })
        {
            await using AsyncServiceScope scope = As(role);
            Assert.True((await scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>().StartBudgetAsync(new StartBudgetRequest(fy2030, false))).IsFailure, role);
        }
    }

    [Fact]
    public async Task A_closed_fiscal_year_takes_no_new_amendment_until_it_is_reopened()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();
        IFiscalYearService years = scope.ServiceProvider.GetRequiredService<IFiscalYearService>();
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        FiscalYear fy2026 = await db.FiscalYears.SingleAsync(f => f.Year == 2026);
        Guid latest2026 = (await db.BudgetVersions.SingleAsync(v => v.FiscalYearId == fy2026.Id && v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null)).Id;

        Assert.True((await years.SetClosedAsync(fy2026.Id, true)).IsSuccess);
        Result<Guid> refused = await workflow.CreateAmendmentAsync(latest2026, "Late supplemental");
        WorkflowStateDto closedState = (await workflow.GetStateAsync(latest2026))!;

        Assert.Contains("FY2026 is closed", refused.Errors.Single().Message, StringComparison.Ordinal);
        Assert.False(closedState.CanAmend);

        Assert.True((await years.SetClosedAsync(fy2026.Id, false)).IsSuccess);
        Assert.True((await workflow.CreateAmendmentAsync(latest2026, "Late supplemental")).IsSuccess);
    }

    [Fact]
    public async Task Propose_adopt_publish_amend_records_audit_events_and_supersedes_history()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();
        IPublishingService publishing = scope.ServiceProvider.GetRequiredService<IPublishingService>();
        await FixStreetFundAsync(scope.ServiceProvider);

        Assert.True((await workflow.ProposeAsync(_draft2027, false)).IsSuccess);
        Assert.True((await workflow.AdoptAsync(_draft2027, "", false)).IsFailure);            // resolution required
        Assert.True((await workflow.AdoptAsync(_draft2027, "2026-44", false)).IsSuccess);

        Result<Guid> published = await publishing.PublishAsync(_draft2027);
        Assert.True(published.IsSuccess, string.Join("; ", published.Errors.Select(e => e.Message)));
        Assert.True((await publishing.PublishAsync(_draft2027)).IsFailure);                    // already published

        Result<Guid> amendment = await workflow.CreateAmendmentAsync(_draft2027, "Culvert replacement on Mill Road");
        Assert.True(amendment.IsSuccess);
        Assert.True((await workflow.CreateAmendmentAsync(_draft2027, "second")).IsFailure);     // one open version per year

        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto amended = (await entry.GetWorkspaceAsync(amendment.Value))!;
        Assert.Equal(95, amended.Lines.Count);
        Assert.Equal("Amendment 1", amended.Version.Label);
        Assert.True(amended.IsEditable);

        // Adopt the amendment: the original is superseded; publishing it replaces the active snapshot.
        Assert.True((await workflow.ProposeAsync(amendment.Value, false)).IsSuccess);
        Assert.True((await workflow.AdoptAsync(amendment.Value, "2027-03", false)).IsSuccess);
        Result<Guid> republished = await publishing.PublishAsync(amendment.Value);
        Assert.True(republished.IsSuccess);

        // The original is history now: it cannot go back on the portal over the amendment.
        Assert.Contains("latest adopted version", (await publishing.PublishAsync(_draft2027)).Errors.Single().Message, StringComparison.Ordinal);
        Assert.False((await workflow.GetStateAsync(_draft2027))!.CanPublish);

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        BudgetVersion original = await db.BudgetVersions.SingleAsync(v => v.Id == _draft2027);
        Assert.Equal(amendment.Value, original.SupersededByVersionId);

        List<PublishedBudgetSnapshot> snapshots = await db.PublishedBudgetSnapshots.Where(s => s.FiscalYear == 2027).OrderBy(s => s.PublishedAtUtc).ToListAsync();
        Assert.Equal([SnapshotStatus.Superseded, SnapshotStatus.Active], snapshots.Select(s => s.Status));

        List<string> events = await db.AuditEntries
            .Where(a => a.EntityName == nameof(BudgetVersion) && a.EntityId == _draft2027 && a.Kind == AuditKind.Event)
            .OrderBy(a => a.TimestampUtc).Select(a => a.Description!).ToListAsync();
        Assert.Equal(["Proposed to council", "Adopted by resolution 2026-44", "Published to the public portal", "Superseded by Amendment 1"], events);

        Assert.All(await db.AuditEntries.Where(a => a.Kind == AuditKind.Event && a.EntityId == _draft2027).ToListAsync(), a => Assert.Equal("Test FinanceDirector", a.UserName));
    }

    [Fact]
    public async Task Return_to_draft_reopens_the_version_for_department_heads()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>();
        await FixStreetFundAsync(scope.ServiceProvider);
        await workflow.ProposeAsync(_draft2027, false);

        Assert.True((await workflow.ReturnToDraftAsync(_draft2027)).IsSuccess);

        Assert.Equal(BudgetStatus.Draft, (await workflow.GetStateAsync(_draft2027))!.Status);
    }

    // ---- publishing and the portal boundary ------------------------------------------------------

    [Fact]
    public async Task Unpublish_keeps_the_snapshot_but_hides_it_from_the_portal()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IPublishingService publishing = scope.ServiceProvider.GetRequiredService<IPublishingService>();
        SnapshotSummaryDto fy2025 = (await publishing.ListAsync()).Single(s => s.FiscalYear == 2025);

        Assert.True((await publishing.UnpublishAsync(fy2025.Id)).IsSuccess);
        Assert.True((await publishing.UnpublishAsync(fy2025.Id)).IsFailure); // only once

        Assert.Equal(SnapshotStatus.Unpublished, (await publishing.ListAsync()).Single(s => s.Id == fy2025.Id).Status);
        await using PublicPortalDbContext portal = CreatePortalContext();
        Assert.False(await portal.Snapshots.AnyAsync(s => s.Id == fy2025.Id));
        Assert.False(await portal.SnapshotLines.AnyAsync(l => l.SnapshotId == fy2025.Id));
        await using CivicBudgetDbContext admin = _database.CreateContext(_mapleRidge);
        Assert.Equal(95, await admin.PublishedBudgetSnapshots.Where(s => s.Id == fy2025.Id).SelectMany(s => s.Lines).CountAsync());
    }

    [Fact]
    public async Task Portal_context_sees_only_active_snapshots_by_slug_and_nothing_live()
    {
        await using PublicPortalDbContext portal = CreatePortalContext();

        List<PublishedBudgetSnapshot> maple = await portal.Snapshots.Where(s => s.GovernmentSlug == "maple-ridge-oh").OrderBy(s => s.FiscalYear).ToListAsync();
        Assert.Equal([2025, 2026], maple.Select(s => s.FiscalYear));
        Assert.Equal("Amendment 1", maple[1].VersionLabel);
        Assert.All(maple, s => Assert.Equal(SnapshotStatus.Active, s.Status));

        PublishedBudgetSnapshot pine = await portal.Snapshots.SingleAsync(s => s.GovernmentSlug == "pine-hollow-twp-oh");
        Assert.Equal(2026, pine.FiscalYear);

        // The model maps the four snapshot tables and the government logo (a public image, ADR-0029) and
        // nothing else: no live lines, users, or governments to leak.
        List<string> tables = portal.Model.GetEntityTypes().Select(e => e.GetTableName()!).OrderBy(t => t).ToList();
        Assert.Equal(["GovernmentLogos", "PublishedBudgetSnapshotDepartments", "PublishedBudgetSnapshotFunds", "PublishedBudgetSnapshotLines", "PublishedBudgetSnapshots"], tables);
    }

    [Fact]
    public async Task Portal_context_cannot_write()
    {
        await using PublicPortalDbContext portal = CreatePortalContext();
        PublishedBudgetSnapshot snapshot = await portal.Snapshots.FirstAsync();

        portal.Snapshots.Remove(snapshot);

        await Assert.ThrowsAsync<InvalidOperationException>(() => portal.SaveChangesAsync());
    }

    [Fact]
    public async Task Snapshot_carries_names_and_balances_so_the_portal_needs_no_joins()
    {
        await using PublicPortalDbContext portal = CreatePortalContext();
        PublishedBudgetSnapshot fy2026 = await portal.Snapshots
            .Include(s => s.Lines).Include(s => s.Funds)
            .SingleAsync(s => s.GovernmentSlug == "maple-ridge-oh" && s.FiscalYear == 2026);

        Assert.Equal(95, fy2026.Lines.Count);
        Assert.Equal(5, fy2026.Funds.Count);
        Assert.Equal(620_000m, fy2026.Funds.Single(f => f.Code == "1000").BeginningBalance);
        Assert.Equal(53_000m, fy2026.Lines.Single(l => l.DepartmentCode == "110" && l.AccountCode == "5120").Amount); // the amended overtime
        Assert.Contains("gasoline tax", fy2026.Funds.Single(f => f.Code == "2011").Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Village of Maple Ridge", fy2026.GovernmentName);
    }

    private PublicPortalDbContext CreatePortalContext() =>
        new(new DbContextOptionsBuilder<PublicPortalDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
