using System.Security.Claims;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Phase 9d through the services: a department user enters and submits their own department's
/// request, submitting locks it for them and not for the fiscal officer, and the officer can send
/// it back with a note. The seed leaves FY2027 mid-round: Police submitted, Parks returned, the
/// rest in progress.
/// </summary>
[Collection(SqlServerTests.Name)]
public class DepartmentRequestServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _police;
    private Guid _streets;
    private Guid _parks;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_DeptRequests");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _police = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
        _streets = (await scoped.Departments.SingleAsync(d => d.Code == "620")).Id;
        _parks = (await scoped.Departments.SingleAsync(d => d.Code == "310")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_workspace_tells_each_user_where_their_departments_stand()
    {
        await using AsyncServiceScope officer = As(Roles.FinanceDirector);
        BudgetWorkspaceDto all = (await Entry(officer).GetWorkspaceAsync(_draft2027))!;
        Assert.Equal(8, all.DepartmentRequests.Count); // every active department, even ones with nothing entered
        Assert.Equal(1, all.DepartmentsSubmitted);
        DepartmentRequestDto police = all.DepartmentRequests.Single(r => r.DepartmentId == _police);
        Assert.Equal(DepartmentRequestStatus.Submitted, police.Status);
        Assert.Equal("Chief Morgan Hale", police.SubmittedByUserName);
        Assert.True(police.CanReturn);
        Assert.False(police.CanSubmit);
        Assert.True(police.CanEditNarrative); // the officer edits anything, submitted or not
        Assert.Equal(DepartmentRequestStatus.Returned, all.DepartmentRequests.Single(r => r.DepartmentId == _parks).Status);

        await using AsyncServiceScope director = As(Roles.DepartmentHead, _streets, _parks);
        BudgetWorkspaceDto mine = (await Entry(director).GetWorkspaceAsync(_draft2027))!;
        Assert.Equal([_parks, _streets], mine.DepartmentRequests.Select(r => r.DepartmentId)); // by code: 310 before 620
        DepartmentRequestDto parks = mine.DepartmentRequests.Single(r => r.DepartmentId == _parks);
        Assert.True(parks.CanSubmit);            // a returned request can go back in
        Assert.False(parks.CanReturn);           // returning is the officer's move
        Assert.Contains("Capital Projects fund", parks.ReturnNote, StringComparison.Ordinal);
        Assert.True(parks.CanEditNarrative);
    }

    [Fact]
    public async Task A_submitted_department_is_locked_for_its_users_and_open_to_the_fiscal_officer()
    {
        await using AsyncServiceScope chief = As(Roles.DepartmentHead, _police);
        IBudgetEntryService chiefEntry = Entry(chief);
        BudgetWorkspaceDto mine = (await chiefEntry.GetWorkspaceAsync(_draft2027))!;

        Assert.All(mine.Lines, l => Assert.False(l.CanEdit));
        Assert.False(mine.CanAddLines);
        Assert.True((await chiefEntry.UpdateLineAmountAsync(_draft2027, mine.Lines[0].Id, 1m)).IsFailure);
        Assert.True((await Requests(chief).SaveNarrativeAsync(_draft2027, _police, "Late edit")).IsFailure);
        Assert.True((await Requests(chief).SubmitAsync(_draft2027, _police)).IsFailure); // not twice

        await using AsyncServiceScope officer = As(Roles.FinanceDirector);
        BudgetLineDto policeLine = (await Entry(officer).GetWorkspaceAsync(_draft2027))!.Lines.First(l => l.DepartmentId == _police);
        Assert.True(policeLine.CanEdit);
        Assert.True((await Entry(officer).UpdateLineAmountAsync(_draft2027, policeLine.Id, policeLine.Amount + 1m)).IsSuccess);
    }

    [Fact]
    public async Task A_department_user_submits_their_own_department_and_it_is_audited()
    {
        await using AsyncServiceScope director = As(Roles.DepartmentHead, _streets, _parks);
        IDepartmentRequestService requests = Requests(director);

        Assert.True((await requests.SaveNarrativeAsync(_draft2027, _streets, "  Same crew, one seasonal hire.  ")).IsSuccess);
        Result submitted = await requests.SubmitAsync(_draft2027, _streets);
        Assert.True(submitted.IsSuccess, string.Join("; ", submitted.Errors.Select(e => e.Message)));

        BudgetWorkspaceDto mine = (await Entry(director).GetWorkspaceAsync(_draft2027))!;
        DepartmentRequestDto streets = mine.DepartmentRequests.Single(r => r.DepartmentId == _streets);
        Assert.Equal(DepartmentRequestStatus.Submitted, streets.Status);
        Assert.Equal("Same crew, one seasonal hire.", streets.Narrative);
        Assert.Equal("Test DepartmentHead", streets.SubmittedByUserName);
        Assert.All(mine.Lines.Where(l => l.DepartmentId == _streets), l => Assert.False(l.CanEdit));
        Assert.All(mine.Lines.Where(l => l.DepartmentId == _parks), l => Assert.True(l.CanEdit)); // the other department is untouched
        Assert.True(mine.CanAddLines);                                                            // still one open department

        // Streets belongs to Sam; the Police chief cannot submit or narrate it.
        await using AsyncServiceScope chief = As(Roles.DepartmentHead, _police);
        Assert.True((await Requests(chief).SubmitAsync(_draft2027, _parks)).IsFailure);
        Assert.True((await Requests(chief).SaveNarrativeAsync(_draft2027, _parks, "Not mine")).IsFailure);

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityName == nameof(BudgetVersion) && a.Description == "Streets & Service submitted its budget request"));
        // The named event carries who and when; the bookkeeping fields are [NotAudited] so the timeline is not cluttered with ids and timestamps.
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityName == nameof(DepartmentRequest) && a.PropertyName == nameof(DepartmentRequest.Status)));
        Assert.False(await db.AuditEntries.AnyAsync(a => a.EntityName == nameof(DepartmentRequest) && (a.PropertyName == nameof(DepartmentRequest.SubmittedByUserId) || a.PropertyName == nameof(DepartmentRequest.SubmittedAtUtc))));
    }

    [Fact]
    public async Task Only_the_fiscal_officer_returns_and_a_note_is_required()
    {
        await using AsyncServiceScope chief = As(Roles.DepartmentHead, _police);
        Assert.True((await Requests(chief).ReturnAsync(_draft2027, _police, "Let me back in")).IsFailure);

        await using AsyncServiceScope officer = As(Roles.FinanceDirector);
        IDepartmentRequestService requests = Requests(officer);
        Assert.True((await requests.ReturnAsync(_draft2027, _police, "  ")).IsFailure);
        Assert.True((await requests.ReturnAsync(_draft2027, _streets, "Nothing to return")).IsFailure); // in progress, never submitted

        Result returned = await requests.ReturnAsync(_draft2027, _police, "Move the cruiser to the capital fund.");
        Assert.True(returned.IsSuccess, string.Join("; ", returned.Errors.Select(e => e.Message)));

        BudgetWorkspaceDto mine = (await Entry(chief).GetWorkspaceAsync(_draft2027))!;
        DepartmentRequestDto police = mine.DepartmentRequests.Single();
        Assert.Equal(DepartmentRequestStatus.Returned, police.Status);
        Assert.Equal("Move the cruiser to the capital fund.", police.ReturnNote);
        Assert.All(mine.Lines, l => Assert.True(l.CanEdit)); // the chief is back in
        Assert.True((await Requests(chief).SubmitAsync(_draft2027, _police)).IsSuccess);

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Description!.StartsWith("Returned Police's budget request")));
    }

    [Fact]
    public async Task A_viewer_can_neither_narrate_nor_submit()
    {
        await using AsyncServiceScope viewer = As(Roles.Viewer);
        Assert.True((await Requests(viewer).SaveNarrativeAsync(_draft2027, _streets, "x")).IsFailure);
        Assert.True((await Requests(viewer).SubmitAsync(_draft2027, _streets)).IsFailure);
        BudgetWorkspaceDto workspace = (await Entry(viewer).GetWorkspaceAsync(_draft2027))!;
        Assert.All(workspace.DepartmentRequests, r => Assert.False(r.CanSubmit || r.CanReturn || r.CanEditNarrative));
    }

    private static IBudgetEntryService Entry(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
    private static IDepartmentRequestService Requests(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDepartmentRequestService>();

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
