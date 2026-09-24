using System.Security.Claims;
using System.Text;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Import;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Import against the seeded FY2027 draft: preview classifies rows against real lines and codes,
/// commit applies them through the aggregate in one save with an audit event, and the guards
/// (role, adopted version, errors) refuse before anything is written.
/// </summary>
[Collection(SqlServerTests.Name)]
public class BudgetImportServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _draft2027;
    private Guid _adopted2025;
    private Guid _policeDept;

    public async Task InitializeAsync()
    {
        // Commits change the draft, so every test gets its own seeded database.
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Import");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        await using CivicBudgetDbContext scoped = _database.CreateContext(_mapleRidge);
        _draft2027 = (await scoped.BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        _adopted2025 = (await scoped.BudgetVersions
            .Join(scoped.FiscalYears, v => v.FiscalYearId, fy => fy.Id, (v, fy) => new { v, fy })
            .Where(x => x.fy.Year == 2025).Select(x => x.v).SingleAsync()).Id;
        _policeDept = (await scoped.Departments.SingleAsync(d => d.Code == "110")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Preview_classifies_rows_against_the_versions_lines_and_the_governments_codes()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetImportService import = scope.ServiceProvider.GetRequiredService<IBudgetImportService>();
        BudgetLineDto overtime = await LineAsync(scope, "1000", "110", "5120");

        Result<ImportPreviewDto> result = await import.PreviewAsync(_draft2027, "lines.csv", Csv(
            "Fund,Department,Account,Amount,Justification",
            $"1000,110,5120,{overtime.Amount},",                 // same amount: unchanged
            $"1000,110,5120,{overtime.Amount + 1000},Duplicate", // second row for the same key
            "4901,110,5420,750.00,Fuel for the new truck",       // no such line yet: add
            "1000,110,9999,10,",                                  // unknown account
            "2011,,5420,10,"));                                  // expenditure without a department

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        ImportPreviewDto preview = result.Value;
        Assert.Equal([ImportRowAction.Unchanged, ImportRowAction.Error, ImportRowAction.Add, ImportRowAction.Error, ImportRowAction.Error], preview.Rows.Select(r => r.Action));
        Assert.Equal(overtime.Amount, preview.Rows[0].ExistingAmount);
        Assert.Equal(("Capital Projects", "Police", "Fuel"), (preview.Rows[2].FundName, preview.Rows[2].DepartmentName, preview.Rows[2].AccountName));
        Assert.Contains("\"9999\"", preview.Rows[3].Errors.Single(), StringComparison.Ordinal);
        Assert.False(preview.CanCommit);
        Assert.Equal(5, preview.Inputs.Count);
    }

    [Fact]
    public async Task Commit_adds_and_updates_lines_in_one_save_and_records_an_audit_event()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetImportService import = scope.ServiceProvider.GetRequiredService<IBudgetImportService>();
        BudgetLineDto overtime = await LineAsync(scope, "1000", "110", "5120");
        int linesBefore = (await Workspace(scope)).Lines.Count;

        ImportPreviewDto preview = (await import.PreviewAsync(_draft2027, "budget.csv", Csv(
            "Fund,Department,Account,Amount,Prior Year Actual,Current Year Budget,Justification",
            $"1000,110,5120,{overtime.Amount + 5000},,,Contract settlement",
            "4901,110,5420,750,700,725,Fuel for the new truck"))).Value;
        Assert.True(preview.CanCommit);

        Result<ImportResultDto> committed = await import.CommitAsync(_draft2027, "budget.csv", preview.Inputs);

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal(new ImportResultDto(Added: 1, Updated: 1, Unchanged: 0), committed.Value);

        BudgetWorkspaceDto after = await Workspace(scope);
        Assert.Equal(linesBefore + 1, after.Lines.Count);
        BudgetLineDto updated = after.Lines.Single(l => l.Id == overtime.Id);
        Assert.Equal(overtime.Amount + 5000, updated.Amount);
        Assert.Equal(overtime.PriorYearActual, updated.PriorYearActual); // blank column left it alone
        Assert.Equal("Contract settlement", updated.Justification);
        BudgetLineDto added = after.Lines.Single(l => l.FundCode == "4901" && l.DepartmentCode == "110" && l.AccountCode == "5420");
        Assert.Equal((750m, 700m, 725m), (added.Amount, added.PriorYearActual, added.CurrentYearBudget));

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        AuditEntry importEvent = await db.AuditEntries.SingleAsync(a => a.Kind == AuditKind.Event && a.EntityId == _draft2027 && a.Description!.StartsWith("Imported"));
        Assert.Equal("Imported budget.csv: 1 added, 1 updated, 0 unchanged", importEvent.Description);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == overtime.Id && a.PropertyName == nameof(BudgetLine.Amount))); // the interceptor still saw the field change
    }

    [Fact]
    public async Task A_file_of_full_account_numbers_imports_like_one_of_codes()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetImportService import = scope.ServiceProvider.GetRequiredService<IBudgetImportService>();
        BudgetLineDto overtime = await LineAsync(scope, "1000", "110", "5120");

        ImportPreviewDto preview = (await import.PreviewAsync(_draft2027, "numbers.csv", Csv(
            "Account Number,Amount",
            $"1000-110-5120,{overtime.Amount}",   // unchanged
            "4901-110-5420,750",                  // add
            "1000-4110,1"))).Value;               // a fund-level revenue line, two segments

        Assert.Equal([ImportRowAction.Unchanged, ImportRowAction.Add, ImportRowAction.Update], preview.Rows.Select(r => r.Action));
        Assert.Equal("Fuel", preview.Rows[1].AccountName);
        Assert.Null(preview.Rows[2].DepartmentCode);
    }

    [Fact]
    public async Task Codes_written_with_leading_zeros_commit_as_well_as_preview()
    {
        // Another system's export (or a spreadsheet that padded the column) writes 1000 as 01000. The
        // preview matches those, so commit must apply what the preview matched, not look the text up again.
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetImportService import = scope.ServiceProvider.GetRequiredService<IBudgetImportService>();
        ImportPreviewDto preview = (await import.PreviewAsync(_draft2027, "padded.csv", Csv("Fund,Department,Account,Amount", "04901,0110,05420,750"))).Value;
        Assert.Equal(ImportRowAction.Add, preview.Rows.Single().Action);

        Result<ImportResultDto> committed = await import.CommitAsync(_draft2027, "padded.csv", preview.Inputs);

        Assert.True(committed.IsSuccess, string.Join("; ", committed.Errors.Select(e => e.Message)));
        Assert.Equal(750m, (await LineAsync(scope, "4901", "110", "5420")).Amount);
    }

    [Fact]
    public async Task Commit_refuses_when_a_row_has_an_error_and_writes_nothing()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetImportService import = scope.ServiceProvider.GetRequiredService<IBudgetImportService>();
        int linesBefore = (await Workspace(scope)).Lines.Count;
        ImportPreviewDto preview = (await import.PreviewAsync(_draft2027, "bad.csv", Csv("Fund,Department,Account,Amount", "4901,110,5420,750", "1000,110,9999,10"))).Value;

        Result<ImportResultDto> committed = await import.CommitAsync(_draft2027, "bad.csv", preview.Inputs);

        Assert.True(committed.IsFailure);
        Assert.Equal(linesBefore, (await Workspace(scope)).Lines.Count);
    }

    [Fact]
    public async Task Only_the_fiscal_authority_may_import_and_only_into_an_editable_version()
    {
        await using AsyncServiceScope head = As(Roles.DepartmentHead, _policeDept);
        Result<ImportPreviewDto> denied = await head.ServiceProvider.GetRequiredService<IBudgetImportService>()
            .PreviewAsync(_draft2027, "lines.csv", Csv("Fund,Department,Account,Amount", "4901,110,5420,750"));
        Assert.Contains("Fiscal Officer", denied.Errors.Single().Message, StringComparison.Ordinal);

        await using AsyncServiceScope director = As(Roles.FinanceDirector);
        Result<ImportPreviewDto> adopted = await director.ServiceProvider.GetRequiredService<IBudgetImportService>()
            .PreviewAsync(_adopted2025, "lines.csv", Csv("Fund,Department,Account,Amount", "4901,110,5420,750"));
        Assert.Contains("adopted", adopted.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unusable_files_fail_as_a_whole_with_a_plain_message()
    {
        await using AsyncServiceScope scope = As(Roles.FinanceDirector);
        IBudgetImportService import = scope.ServiceProvider.GetRequiredService<IBudgetImportService>();

        Assert.Contains("needs an Amount column", (await import.PreviewAsync(_draft2027, "x.csv", Csv("Fund,Note", "1000,hi"))).Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains(".csv or .xlsx", (await import.PreviewAsync(_draft2027, "x.pdf", Csv("Fund"))).Errors.Single().Message, StringComparison.Ordinal);
        Assert.Contains("Excel", (await import.PreviewAsync(_draft2027, "x.xlsx", Csv("Fund,Account,Amount", "1000,4100,1"))).Errors.Single().Message, StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static MemoryStream Csv(params string[] lines) => new(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    private async Task<BudgetWorkspaceDto> Workspace(AsyncServiceScope scope) =>
        (await scope.ServiceProvider.GetRequiredService<IBudgetEntryService>().GetWorkspaceAsync(_draft2027))!;

    private async Task<BudgetLineDto> LineAsync(AsyncServiceScope scope, string fund, string department, string account) =>
        (await Workspace(scope)).Lines.Single(l => l.FundCode == fund && l.DepartmentCode == department && l.AccountCode == account);

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
