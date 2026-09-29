using System.Security.Claims;
using CivicBudget.Application.Common;
using CivicBudget.Application.Portal;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The budget book against the seeded budgets: who may print it and write its message, that the
/// chosen sections change what is printed, and that publishing keeps the book the portal serves.
/// </summary>
[Collection(SqlServerTests.Name)]
public class BudgetBookServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _adopted2026;
    private Guid _police;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Book");
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _adopted2026 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Adopted && v.SupersededByVersionId == null && v.VersionNumber > 1)).Id;
        _police = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope DepartmentHead()
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-police"));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.DepartmentHead));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        identity.AddClaim(new Claim(ClaimNames.DepartmentId, _police.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    private static IBudgetBookService Books(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBudgetBookService>();

    private static int Pages(byte[] pdf)
    {
        using PdfDocument document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    private static readonly BudgetBookOptions Core = new(false, false, false, false);
    private static readonly BudgetBookOptions Everything = new(true, true, true, true);

    [Fact]
    public async Task The_fiscal_officer_prints_a_book_and_the_chosen_sections_change_it()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);

        BudgetBookFile core = (await Books(scope).RenderAsync(_draft2027, Core))!;
        BudgetBookFile full = (await Books(scope).RenderAsync(_draft2027, Everything))!;

        Assert.Equal("budget-book-fy2027-original-proposed.pdf", core.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(core.Content, 0, 4));
        Assert.True(Pages(core.Content) >= 10, $"{Pages(core.Content)} pages");
        Assert.True(Pages(full.Content) > Pages(core.Content) + 3); // the outlook, personnel, line items, and glossary
    }

    [Fact]
    public async Task A_viewer_prints_it_and_a_department_head_gets_none()
    {
        await using AsyncServiceScope viewer = _database.CreateScopeAs(Roles.Viewer, _mapleRidge);
        Assert.NotNull(await Books(viewer).RenderAsync(_adopted2026, Core));
        Assert.False((await Books(viewer).GetAsync(_adopted2026))!.CanEditMessage);

        await using AsyncServiceScope department = DepartmentHead();
        Assert.Null(await Books(department).RenderAsync(_draft2027, Core));
        Assert.Null(await Books(department).GetAsync(_draft2027));
    }

    [Fact]
    public async Task The_message_is_written_by_the_fiscal_authority_while_the_budget_is_open()
    {
        await using AsyncServiceScope officer = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        Result saved = await Books(officer).SaveMessageAsync(new SaveBudgetMessageRequest(_draft2027, "A new letter", "First.\n\nSecond.", "Rebecca Lang", "Mayor"));

        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(e => e.Message)));
        BudgetBookPageDto page = (await Books(officer).GetAsync(_draft2027))!;
        Assert.Equal(("A new letter", "First.\n\nSecond."), (page.MessageHeading, page.MessageBody));
        Assert.True(page.CanEditMessage);

        Assert.True((await Books(officer).SaveMessageAsync(new SaveBudgetMessageRequest(_adopted2026, null, "Too late.", null, null))).IsFailure);
        await using AsyncServiceScope viewer = _database.CreateScopeAs(Roles.Viewer, _mapleRidge);
        Assert.True((await Books(viewer).SaveMessageAsync(new SaveBudgetMessageRequest(_draft2027, null, "Not mine.", null, null))).IsFailure);
    }

    [Fact]
    public async Task The_seeded_draft_says_which_departments_have_no_narrative()
    {
        await using AsyncServiceScope scope = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);

        BudgetBookPageDto page = (await Books(scope).GetAsync(_draft2027))!;

        Assert.True(page.IsDraft);
        Assert.Contains("715 Council & Mayor", page.DepartmentsWithoutNarrative);
        Assert.DoesNotContain("110 Police", page.DepartmentsWithoutNarrative);
    }

    [Fact]
    public async Task Defaults_are_the_fiscal_authoritys_and_start_every_book()
    {
        await using AsyncServiceScope officer = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge);
        Assert.Equal(new BudgetBookOptions(true, true, false, true), await Books(officer).GetDefaultsAsync()); // before anything is saved

        Assert.True((await Books(officer).SaveDefaultsAsync(new BudgetBookOptions(false, true, true, false))).IsSuccess);
        Assert.Equal(new BudgetBookOptions(false, true, true, false), (await Books(officer).GetAsync(_draft2027))!.Defaults);

        await using AsyncServiceScope viewer = _database.CreateScopeAs(Roles.Viewer, _mapleRidge);
        Assert.True((await Books(viewer).SaveDefaultsAsync(Everything)).IsFailure);
    }

    [Fact]
    public async Task Publishing_keeps_the_book_and_the_portal_serves_that_copy()
    {
        Guid snapshotId;
        await using (AsyncServiceScope officer = _database.CreateScopeAs(Roles.FinanceDirector, _mapleRidge))
        {
            IPublishingService publishing = officer.ServiceProvider.GetRequiredService<IPublishingService>();
            SnapshotSummaryDto current = (await publishing.ListAsync()).Single(s => s.FiscalYear == 2026 && s.Status == SnapshotStatus.Active);
            Assert.True((await publishing.UnpublishAsync(current.Id)).IsSuccess);
            Result<Guid> published = await publishing.PublishAsync(_adopted2026);
            Assert.True(published.IsSuccess, string.Join("; ", published.Errors.Select(e => e.Message)));
            snapshotId = published.Value;
        }

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        PublishedBudgetBook stored = await db.PublishedBudgetBooks.SingleAsync(b => b.SnapshotId == snapshotId);

        await using AsyncServiceScope portal = _database.CreateScope();
        ISnapshotQueryService snapshots = portal.ServiceProvider.GetRequiredService<ISnapshotQueryService>();
        Assert.True((await snapshots.GetBudgetAsync("maple-ridge-oh", 2026))!.HasBook);
        Assert.Equal(stored.Content, (await snapshots.GetBookAsync("maple-ridge-oh", 2026))!.Content);
    }

    [Fact]
    public async Task Every_seeded_published_budget_has_its_book()
    {
        await using AsyncServiceScope portal = _database.CreateScope();
        ISnapshotQueryService snapshots = portal.ServiceProvider.GetRequiredService<ISnapshotQueryService>();

        foreach ((string slug, int year) in new[] { ("maple-ridge-oh", 2025), ("maple-ridge-oh", 2026), ("pine-hollow-twp-oh", 2026) })
        {
            PortalBookDto? book = await snapshots.GetBookAsync(slug, year);
            Assert.True(book is { Content.Length: > 1000 }, $"{slug} FY{year}");
        }

        Assert.Null(await snapshots.GetBookAsync("maple-ridge-oh", 2027)); // not published
    }
}
