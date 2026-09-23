using Bunit.TestDoubles;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Budgets;

/// <summary>
/// Phase 9d pages over a hand-built workspace: the strip and board read statuses from the DTO, the
/// entry page shows the return note, locks a submitted request, and routes submit and return through
/// the request service; My department sends a one-department user straight to their page.
/// </summary>
public class DepartmentPagesTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly Guid Police = Guid.CreateVersion7();
    private static readonly Guid Streets = Guid.CreateVersion7();

    private readonly FakeEntryService _entry = new();
    private readonly FakeRequestService _requests = new();

    public DepartmentPagesTests()
    {
        Services.AddSingleton<IBudgetEntryService>(_entry);
        Services.AddSingleton<IDepartmentRequestService>(_requests);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("sam");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static DepartmentRequestDto Request(Guid id, string code, string name, DepartmentRequestStatus status, bool canEdit, bool canSubmit, bool canReturn, string? note = null) =>
        new(id, code, name, status, "Narrative for " + name,
            status == DepartmentRequestStatus.Submitted ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            status == DepartmentRequestStatus.Submitted ? "Chief Hale" : null,
            note, note is null ? null : DateTimeOffset.UtcNow.AddHours(-2),
            2, 90m, 100m, 110m, canEdit, canSubmit, canReturn, CanAddLines: canEdit);

    private static BudgetLineDto Line(Guid department, string departmentCode, string account, AccountType type, decimal amount, bool canEdit) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "1000", "General Fund", department, departmentCode, "Dept",
            Guid.CreateVersion7(), account, "Account " + account, $"1000-{departmentCode}-{account}", type, ReportingCategory.PersonalServices, amount, 40m, 50m, null, canEdit);

    private static BudgetWorkspaceDto Workspace(IReadOnlyList<DepartmentRequestDto> requests, IReadOnlyList<BudgetLineDto> lines, bool canAddLines = true) => new(
        new BudgetVersionSummaryDto(VersionId, 2027, 1, "Original", BudgetStatus.Draft, null, null, lines.Count),
        AccountNumberFormat.UanVillage, true, canAddLines, lines, [], [], [], [], requests);

    [Fact]
    public void Strip_shows_one_chip_per_department_with_its_status_and_the_count()
    {
        BudgetWorkspaceDto workspace = Workspace(
        [
            Request(Police, "110", "Police", DepartmentRequestStatus.Submitted, false, false, true),
            Request(Streets, "620", "Streets", DepartmentRequestStatus.Returned, true, true, false, "Trim overtime"),
        ], []);

        IRenderedComponent<DepartmentStrip> strip = Render<DepartmentStrip>(p => p.Add(x => x.Workspace, workspace));

        Assert.Contains("1 of 2 submitted", strip.Markup);
        Assert.Equal(2, strip.FindAll("a.cb-dept-chip").Count);
        Assert.Contains("submitted", strip.Find("a.cb-dept-chip").ClassList);
        Assert.Contains("returned", strip.FindAll("a.cb-dept-chip")[1].ClassList);
        Assert.EndsWith($"/departments/{Police}", strip.Find("a.cb-dept-chip").GetAttribute("href"));
    }

    [Fact]
    public void Board_lists_departments_with_status_and_offers_return_only_where_allowed()
    {
        _entry.Workspace = Workspace(
        [
            Request(Police, "110", "Police", DepartmentRequestStatus.Submitted, true, false, true),
            Request(Streets, "620", "Streets", DepartmentRequestStatus.InProgress, true, true, false),
        ], []);

        IRenderedComponent<DepartmentBoard> board = Render<DepartmentBoard>(p => p.Add(x => x.VersionId, VersionId));

        board.WaitForAssertion(() => Assert.Contains("1 of 2", board.Find(".cb-kpi .v").TextContent));
        Assert.Equal(2, board.FindAll("tbody tr").Count);
        Assert.Contains("Chief Hale", board.Find("tbody tr").TextContent);
        Assert.Single(board.FindAll("table button.dropdown-item"));    // one Return button: Police is submitted, Streets is not
        Assert.Single(board.FindAll(".cb-cards button.dropdown-item")); // the same one on the phone cards
        Assert.Equal(2, board.FindAll(".cb-list-card").Count);
    }

    [Fact]
    public async Task Entry_page_shows_the_return_note_and_submits_through_the_service()
    {
        _entry.Workspace = Workspace(
            [Request(Streets, "620", "Streets", DepartmentRequestStatus.Returned, true, true, false, "Trim overtime to last year's level.")],
            [Line(Streets, "620", "5110", AccountType.Expenditure, 110m, canEdit: true), Line(Streets, "620", "4320", AccountType.Revenue, 5m, canEdit: true)]);

        IRenderedComponent<DepartmentEntry> page = Render<DepartmentEntry>(p => p.Add(x => x.VersionId, VersionId).Add(x => x.DepartmentId, Streets));

        page.WaitForAssertion(() => Assert.Contains("Trim overtime to last year's level.", page.Find(".alert-warning").TextContent));
        Assert.Contains("Returned", page.Find(".cb-pill").TextContent);
        Assert.Contains("Revenue credited to this program", page.Markup);   // revenue lines are listed apart from appropriations
        Assert.Contains("Department total, FY2027 request", page.Markup);
        Assert.Equal("Narrative for Streets", page.Find("textarea#narrative").GetAttribute("value") ?? page.Find("textarea#narrative").TextContent);

        await page.Find("button.btn-primary.btn-sm").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()); // "Submit to fiscal officer"
        await page.Find(".modal-footer .btn-primary").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Equal((VersionId, Streets), _requests.Submitted);
    }

    [Fact]
    public void Entry_page_locks_a_submitted_request_for_the_department()
    {
        _entry.Workspace = Workspace(
            [Request(Police, "110", "Police", DepartmentRequestStatus.Submitted, false, false, false)],
            [Line(Police, "110", "5110", AccountType.Expenditure, 110m, canEdit: false)], canAddLines: false);

        IRenderedComponent<DepartmentEntry> page = Render<DepartmentEntry>(p => p.Add(x => x.VersionId, VersionId).Add(x => x.DepartmentId, Police));

        page.WaitForAssertion(() => Assert.Contains("Submitted", page.Find(".alert-success").TextContent));
        Assert.Empty(page.FindAll("textarea"));                          // narrative is read-only text
        Assert.Contains("Narrative for Police", page.Find(".cb-narrative-text").TextContent);
        Assert.Empty(page.FindAll("input.cb-amount-input"));              // no editable amounts
        Assert.DoesNotContain("Submit to fiscal officer", page.Markup);
        Assert.DoesNotContain("Add line", page.Markup);
    }

    [Fact]
    public void Entry_page_reports_a_department_the_user_cannot_see()
    {
        _entry.Workspace = Workspace([], []);

        IRenderedComponent<DepartmentEntry> page = Render<DepartmentEntry>(p => p.Add(x => x.VersionId, VersionId).Add(x => x.DepartmentId, Police));

        page.WaitForAssertion(() => Assert.Contains("Department not found", page.Markup));
    }

    [Fact]
    public void My_department_sends_a_single_department_user_straight_to_their_page()
    {
        _entry.Versions = [new BudgetVersionSummaryDto(Guid.CreateVersion7(), 2026, 1, "Original", BudgetStatus.Adopted, null, null, 3), new BudgetVersionSummaryDto(VersionId, 2027, 1, "Original", BudgetStatus.Draft, null, null, 3)];
        _entry.Workspace = Workspace([Request(Police, "110", "Police", DepartmentRequestStatus.InProgress, true, true, false)], []);
        var navigation = Services.GetRequiredService<BunitNavigationManager>();

        IRenderedComponent<MyDepartment> page = Render<MyDepartment>();

        page.WaitForAssertion(() => Assert.EndsWith($"admin/budgets/{VersionId}/departments/{Police}", navigation.Uri));
    }

    [Fact]
    public void My_department_sends_a_multi_department_user_to_the_board_and_explains_when_nothing_is_open()
    {
        _entry.Versions = [new BudgetVersionSummaryDto(VersionId, 2027, 1, "Original", BudgetStatus.Draft, null, null, 3)];
        _entry.Workspace = Workspace(
        [
            Request(Police, "110", "Police", DepartmentRequestStatus.InProgress, true, true, false),
            Request(Streets, "620", "Streets", DepartmentRequestStatus.InProgress, true, true, false),
        ], []);
        var navigation = Services.GetRequiredService<BunitNavigationManager>();

        IRenderedComponent<MyDepartment> board = Render<MyDepartment>();
        board.WaitForAssertion(() => Assert.EndsWith($"admin/budgets/{VersionId}/departments", navigation.Uri));

        _entry.Versions = [new BudgetVersionSummaryDto(VersionId, 2027, 1, "Original", BudgetStatus.Adopted, null, null, 3)];
        IRenderedComponent<MyDepartment> page = Render<MyDepartment>();
        page.WaitForAssertion(() => Assert.Contains("No budget in progress", page.Markup));
    }

    private sealed class FakeEntryService : IBudgetEntryService
    {
        public BudgetWorkspaceDto? Workspace { get; set; }
        public IReadOnlyList<BudgetVersionSummaryDto> Versions { get; set; } = [];

        public Task<BudgetWorkspaceDto?> GetWorkspaceAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Workspace);
        public Task<IReadOnlyList<BudgetVersionSummaryDto>> ListVersionsAsync(CancellationToken ct = default) => Task.FromResult(Versions);
        public Task<Result> UpdateLineAmountAsync(Guid versionId, Guid lineId, decimal amount, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> UpdateLineJustificationAsync(Guid versionId, Guid lineId, string? justification, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result<Guid>> AddLineAsync(AddBudgetLineRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> RemoveLineAsync(Guid versionId, Guid lineId, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SetBeginningBalanceAsync(Guid versionId, Guid fundId, decimal amount, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeRequestService : IDepartmentRequestService
    {
        public (Guid Version, Guid Department)? Submitted { get; private set; }

        public Task<Result> SaveNarrativeAsync(Guid versionId, Guid departmentId, string? narrative, CancellationToken ct = default) => Task.FromResult(Result.Success());

        public Task<Result> SubmitAsync(Guid versionId, Guid departmentId, CancellationToken ct = default)
        {
            Submitted = (versionId, departmentId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> ReturnAsync(Guid versionId, Guid departmentId, string note, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }
}
