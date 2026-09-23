using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Import;

public sealed class BudgetImportService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    ISpreadsheetReader spreadsheetReader,
    TimeProvider clock) : IBudgetImportService
{
    /// <summary>A generous ceiling: a village budget is a hundred rows, a county a few thousand.</summary>
    public const int MaxRows = 10_000;

    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can import budget lines.";

    public async Task<Result<ImportPreviewDto>> PreviewAsync(Guid budgetVersionId, string fileName, Stream content, CancellationToken ct = default)
    {
        // Checked before the upload is parsed, so a user who may not import cannot make the server read ten thousand rows.
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<ImportPreviewDto>(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == currentUser.GovernmentId, ct);
        Result<IReadOnlyList<ImportRowInput>> parsed = ReadFile(fileName, content, government.AccountNumberFormat);
        if (parsed.IsFailure)
        {
            return Result.Failure<ImportPreviewDto>(parsed.Errors);
        }

        Result<Analysis> analysed = await AnalyzeAsync(db, budgetVersionId, parsed.Value, ct);
        return analysed.IsFailure
            ? Result.Failure<ImportPreviewDto>(analysed.Errors)
            : Result.Success(new ImportPreviewDto(budgetVersionId, fileName, analysed.Value.Rows, parsed.Value));
    }

    public async Task<Result<ImportResultDto>> CommitAsync(Guid budgetVersionId, string fileName, IReadOnlyList<ImportRowInput> rows, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Result<Analysis> analysed = await AnalyzeAsync(db, budgetVersionId, rows, ct);
        if (analysed.IsFailure)
        {
            return Result.Failure<ImportResultDto>(analysed.Errors);
        }

        (BudgetVersion version, IReadOnlyList<ImportRowDto> analysedRows, Lookups lookups) = analysed.Value;
        if (analysedRows.Any(r => r.Action == ImportRowAction.Error))
        {
            // The database moved under the preview (a code was deactivated, a line was added by hand). Show the preview again.
            return Result.Failure<ImportResultDto>("Some rows have errors. Fix them in the file and upload it again.");
        }

        int added = 0, updated = 0, unchanged = 0;
        try
        {
            foreach (ImportRowDto row in analysedRows)
            {
                Fund fund = lookups.Funds[row.FundCode];
                Department? department = row.DepartmentCode is null ? null : lookups.Departments[row.DepartmentCode];
                Account account = lookups.Accounts[row.AccountCode];

                switch (row.Action)
                {
                    case ImportRowAction.Add:
                        version.AddLine(fund, department, account, row.Amount!.Value, row.PriorYearActual ?? 0m, row.CurrentYearBudget ?? 0m, row.Justification);
                        added++;
                        break;
                    case ImportRowAction.Update:
                        BudgetLine line = version.Lines.Single(l => l.FundId == fund.Id && l.DepartmentId == department?.Id && l.AccountId == account.Id);
                        version.UpdateLineAmount(line.Id, row.Amount!.Value);
                        version.UpdateLineComparatives(line.Id, row.PriorYearActual ?? line.PriorYearActual, row.CurrentYearBudget ?? line.CurrentYearBudget);
                        if (row.Justification is not null)
                        {
                            version.UpdateLineJustification(line.Id, row.Justification);
                        }

                        updated++;
                        break;
                    default:
                        unchanged++;
                        break;
                }
            }
        }
        catch (DomainException ex)
        {
            // The analyser mirrors the aggregate's rules, so this is a belt-and-braces path; the message is still the clerk's.
            return Result.Failure<ImportResultDto>(ex.Message);
        }

        if (added + updated == 0)
        {
            return Result.Failure<ImportResultDto>("Every row already matches the budget; there is nothing to import.");
        }

        // One event for the import as a whole; the interceptor still records each line's field changes.
        db.AuditEntries.Add(AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id,
            $"Imported {fileName}: {added} added, {updated} updated, {unchanged} unchanged",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success(new ImportResultDto(added, updated, unchanged));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private sealed record Lookups(Dictionary<string, Fund> Funds, Dictionary<string, Department> Departments, Dictionary<string, Account> Accounts);

    private sealed record Analysis(BudgetVersion Version, IReadOnlyList<ImportRowDto> Rows, Lookups Lookups);

    private Result<IReadOnlyList<ImportRowInput>> ReadFile(string fileName, Stream content, AccountNumberFormat format)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        TabularFile file;
        try
        {
            file = extension switch
            {
                ".csv" => CsvReader.Read(content),
                ".xlsx" => spreadsheetReader.Read(content),
                _ => throw new InvalidDataException("Upload a .csv or .xlsx file."),
            };
        }
        catch (InvalidDataException ex)
        {
            return Result.Failure<IReadOnlyList<ImportRowInput>>(ex.Message);
        }

        if (file.Rows.Count > MaxRows)
        {
            return Result.Failure<IReadOnlyList<ImportRowInput>>($"The file has more than {MaxRows:N0} rows.");
        }

        return ImportFileParser.Parse(file, format);
    }

    /// <summary>Loads the version and every code the government has, then runs the pure analyser.</summary>
    private async Task<Result<Analysis>> AnalyzeAsync(ICivicBudgetDbContext db, Guid budgetVersionId, IReadOnlyList<ImportRowInput> rows, CancellationToken ct)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<Analysis>(NotAllowed);
        }

        BudgetVersion? version = await db.BudgetVersions
            .Include(v => v.Lines)
            .Include(v => v.BeginningBalances)
            .FirstOrDefaultAsync(v => v.Id == budgetVersionId, ct);
        if (version is null)
        {
            return Result.Failure<Analysis>("Budget version was not found.");
        }

        if (!version.IsEditable)
        {
            return Result.Failure<Analysis>($"{version.Label} is adopted and cannot be changed; create an amendment and import into that.");
        }

        // Inactive codes are loaded too so the analyser can say "inactive" rather than "unknown".
        List<Fund> funds = await db.Funds.ToListAsync(ct);
        List<Department> departments = await db.Departments.ToListAsync(ct);
        List<Account> accounts = await db.Accounts.ToListAsync(ct);

        IReadOnlyList<ImportRowDto> analysed = ImportAnalyzer.Analyze(
            rows,
            funds.Select(f => new ImportLookup(f.Id, f.Code, f.Name, f.IsActive)).ToList(),
            departments.Select(d => new ImportLookup(d.Id, d.Code, d.Name, d.IsActive)).ToList(),
            accounts.Select(a => new ImportLookup(a.Id, a.Code, a.Name, a.IsActive, a.Type)).ToList(),
            version.Lines.Select(l => new ExistingLine(l.Id, l.FundId, l.DepartmentId, l.AccountId, l.Amount, l.PriorYearActual, l.CurrentYearBudget, l.Justification)).ToList());

        var lookups = new Lookups(
            funds.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase),
            departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase),
            accounts.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase));
        return Result.Success(new Analysis(version, analysed, lookups));
    }
}
