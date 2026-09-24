using System.Security.Claims;
using CivicBudget.Application.Auditing;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Phase 9c's two rules, proven through the services rather than the pages: an Administrator can
/// do everything the Fiscal Officer can, and a department user's assignment bounds everything they
/// see, including the audit trail.
/// </summary>
[Collection(SqlServerTests.Name)]
public class PermissionsTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _police;
    private Guid _streets;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Permissions");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _police = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
        _streets = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_administrator_has_everything_the_fiscal_officer_has()
    {
        await using AsyncServiceScope scope = As(Roles.Admin);
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto workspace = (await entry.GetWorkspaceAsync(_draft2027))!;

        Assert.True(workspace.CanAddLines);
        Assert.All(workspace.Lines, l => Assert.True(l.CanEdit));
        Assert.All(workspace.FundBalances, f => Assert.True(f.CanEditBeginningBalance));

        BudgetLineDto line = workspace.Lines.First(l => l.DepartmentCode == "620");
        Assert.True((await entry.UpdateLineAmountAsync(_draft2027, line.Id, line.Amount + 1m)).IsSuccess);
        Assert.True((await entry.SetBeginningBalanceAsync(_draft2027, workspace.FundBalances[0].FundId, 1_000m)).IsSuccess);

        WorkflowStateDto state = (await scope.ServiceProvider.GetRequiredService<IBudgetWorkflowService>().GetStateAsync(_draft2027))!;
        Assert.True(state.CanPropose); // the role allows it; whether a fund over its limit blocks it is BlocksTransition's job

        // Publishing refuses for the right reason (nothing adopted yet), not because of the role.
        Result<Guid> publish = await scope.ServiceProvider.GetRequiredService<IPublishingService>().PublishAsync(_draft2027);
        Assert.True(publish.IsFailure);
        Assert.DoesNotContain("Fiscal Officer", publish.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_viewer_can_look_and_touch_nothing()
    {
        await using AsyncServiceScope scope = As(Roles.Viewer);
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto workspace = (await entry.GetWorkspaceAsync(_draft2027))!;

        Assert.False(workspace.CanAddLines);
        Assert.All(workspace.Lines, l => Assert.False(l.CanEdit));
        Assert.True((await entry.UpdateLineAmountAsync(_draft2027, workspace.Lines[0].Id, 1m)).IsFailure);
        Assert.True((await entry.SetBeginningBalanceAsync(_draft2027, workspace.FundBalances[0].FundId, 1m)).IsFailure);
    }

    [Fact]
    public async Task A_department_user_sees_and_edits_only_their_departments_including_the_audit_trail()
    {
        // The Fiscal Officer changes a Streets line and a Police line; the Police chief sees only the Police change.
        await using (AsyncServiceScope officer = As(Roles.FinanceDirector))
        {
            IBudgetEntryService entry = officer.ServiceProvider.GetRequiredService<IBudgetEntryService>();
            BudgetWorkspaceDto all = (await entry.GetWorkspaceAsync(_draft2027))!;
            BudgetLineDto streets = all.Lines.First(l => l.DepartmentCode == "620");
            BudgetLineDto police = all.Lines.First(l => l.DepartmentCode == "110");
            Assert.True((await entry.UpdateLineAmountAsync(_draft2027, streets.Id, streets.Amount + 10m)).IsSuccess);
            Assert.True((await entry.UpdateLineAmountAsync(_draft2027, police.Id, police.Amount + 20m)).IsSuccess);
        }

        await using AsyncServiceScope chief = As(Roles.DepartmentHead, _police);
        IBudgetEntryService chiefEntry = chief.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        BudgetWorkspaceDto mine = (await chiefEntry.GetWorkspaceAsync(_draft2027))!;
        Assert.All(mine.Lines, l => Assert.Equal("110", l.DepartmentCode));
        Assert.True(mine.Lines.Count > 0);

        IAuditQueryService audit = chief.ServiceProvider.GetRequiredService<IAuditQueryService>();
        IReadOnlyList<AuditEntryDto> recent = await audit.GetRecentAsync(50);
        Assert.NotEmpty(recent);
        Assert.All(recent, a => Assert.Equal(nameof(BudgetLine), a.EntityName));

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Guid streetsLineId = (await db.BudgetLines.FirstAsync(l => l.BudgetVersionId == _draft2027 && l.DepartmentId == _streets)).Id;
        Guid policeLineId = (await db.BudgetLines.FirstAsync(l => l.BudgetVersionId == _draft2027 && l.DepartmentId == _police)).Id;
        Assert.Empty(await audit.GetHistoryAsync(nameof(BudgetLine), streetsLineId));    // another department's line: nothing
        Assert.NotEmpty(await audit.GetHistoryAsync(nameof(BudgetLine), policeLineId));  // their own: the trail
        Assert.True((await chiefEntry.UpdateLineAmountAsync(_draft2027, streetsLineId, 5m)).IsFailure);
    }

    private AsyncServiceScope As(string role, params Guid[] departments)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        foreach (Guid department in departments)
        {
            identity.AddClaim(new Claim(ClaimNames.DepartmentId, department.ToString()));
        }

        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
