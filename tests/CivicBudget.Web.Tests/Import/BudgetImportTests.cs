using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Import;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Web.Components.Admin.Import;
using CivicBudget.Web.Components.Common;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Import;

/// <summary>
/// The preview screen against a fake import service: file errors, per-row results, the counts,
/// and the rule that the Import button only enables when the preview can be committed.
/// </summary>
public class BudgetImportTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();

    private readonly FakeImportService _import = new();

    public BudgetImportTests()
    {
        Services.AddSingleton<IBudgetEntryService>(new FakeEntryService());
        Services.AddSingleton<IBudgetImportService>(_import);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("finance");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Shows_the_file_picker_and_the_template_link_before_a_file_is_chosen()
    {
        IRenderedComponent<BudgetImport> page = Render<BudgetImport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("input#importFile")));
        Assert.Contains($"admin/export/budgets/{VersionId}/lines.xlsx", page.Find("a.btn").GetAttribute("href"));
        Assert.Empty(page.FindAll(".cb-kpis"));
    }

    [Fact]
    public async Task A_preview_with_errors_lists_each_row_and_keeps_the_import_button_disabled()
    {
        _import.Preview = Result.Success(new ImportPreviewDto(VersionId, "lines.csv",
        [
            new(2, "1000", "110", "5120", "General Fund", "Police", "Overtime", 58_000m, null, null, "Settlement", 40_510m, ImportRowAction.Update, []),
            new(3, "4901", "110", "5420", "Capital Projects", "Police", "Fuel", 750m, null, null, null, null, ImportRowAction.Add, []),
            new(4, "1000", "110", "9999", "General Fund", "Police", null, 10m, null, null, null, null, ImportRowAction.Error, ["No account has the code \"9999\"."]),
        ], []));
        IRenderedComponent<BudgetImport> page = Render<BudgetImport>(p => p.Add(x => x.VersionId, VersionId));
        page.WaitForAssertion(() => page.Find("input#importFile"));

        await page.InvokeAsync(() => page.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([new FakeFile("lines.csv")])));

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".cb-kpi").Count));
        Assert.Contains("No account has the code", page.Markup);
        Assert.Equal(3, page.FindAll("tbody tr").Count);
        Assert.Single(page.FindAll("tr.cb-row-error"));
        Assert.Contains("$40,510.00", page.Markup); // the current amount beside the new one
        Assert.True(page.Find("button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_clean_preview_enables_the_import_button_with_the_change_count()
    {
        _import.Preview = Result.Success(new ImportPreviewDto(VersionId, "lines.csv",
            [new(2, "4901", "110", "5420", "Capital Projects", "Police", "Fuel", 750m, null, null, null, null, ImportRowAction.Add, [])], []));
        IRenderedComponent<BudgetImport> page = Render<BudgetImport>(p => p.Add(x => x.VersionId, VersionId));
        page.WaitForAssertion(() => page.Find("input#importFile"));

        await page.InvokeAsync(() => page.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([new FakeFile("lines.csv")])));

        page.WaitForAssertion(() => Assert.False(page.Find("button.btn-primary").HasAttribute("disabled")));
        Assert.Contains("Import 1 lines", page.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public async Task A_file_level_failure_is_shown_as_an_alert()
    {
        _import.Preview = Result.Failure<ImportPreviewDto>("The file needs columns named Account, Amount.");
        IRenderedComponent<BudgetImport> page = Render<BudgetImport>(p => p.Add(x => x.VersionId, VersionId));
        page.WaitForAssertion(() => page.Find("input#importFile"));

        await page.InvokeAsync(() => page.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([new FakeFile("bad.csv")])));

        page.WaitForAssertion(() => Assert.Contains("columns named Account, Amount", page.Find(".alert-danger").TextContent));
        Assert.Empty(page.FindAll(".cb-kpis"));
    }

    private sealed class FakeImportService : IBudgetImportService
    {
        public Result<ImportPreviewDto> Preview { get; set; } = Result.Failure<ImportPreviewDto>("not set");

        public Task<Result<ImportPreviewDto>> PreviewAsync(Guid budgetVersionId, string fileName, Stream content, CancellationToken ct = default) => Task.FromResult(Preview);

        public Task<Result<ImportResultDto>> CommitAsync(Guid budgetVersionId, string fileName, IReadOnlyList<ImportRowInput> rows, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeEntryService : IBudgetEntryService
    {
        public Task<BudgetWorkspaceDto?> GetWorkspaceAsync(Guid versionId, CancellationToken ct = default) =>
            Task.FromResult<BudgetWorkspaceDto?>(new BudgetWorkspaceDto(
                new BudgetVersionSummaryDto(versionId, 2027, 1, "Original", BudgetStatus.Draft, null, null, 0), AccountNumberFormat.UanVillage, true, true, [], [], [], [], [], []));

        public Task<IReadOnlyList<BudgetVersionSummaryDto>> ListVersionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> UpdateLineAmountAsync(Guid versionId, Guid lineId, decimal amount, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> UpdateLineJustificationAsync(Guid versionId, Guid lineId, string? justification, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<Guid>> AddLineAsync(AddBudgetLineRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> RemoveLineAsync(Guid versionId, Guid lineId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> SetBeginningBalanceAsync(Guid versionId, Guid fundId, decimal amount, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>A browser file as InputFile sees it; the page copies it to memory and hands the bytes to the service.</summary>
    private sealed class FakeFile(string name) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => 3;
        public string ContentType => "text/csv";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream("a,b"u8.ToArray());
    }
}
