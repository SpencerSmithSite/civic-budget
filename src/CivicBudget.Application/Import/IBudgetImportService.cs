using CivicBudget.Application.Common;

namespace CivicBudget.Application.Import;

/// <summary>
/// CSV/XLSX import of budget lines into an editable version, in two steps so nothing is written
/// until a person has seen the preview. Finance Director only (<c>Policies.CanImport</c>).
/// <para>
/// File contract: a header row with Fund, Department, Account, Amount, and optionally Prior Year
/// Actual, Current Year Budget, Justification (any order, case-insensitive). Codes, not ids, so a
/// clerk can build the file in Excel. The workspace's "Export lines" produces this exact layout,
/// which makes export, edit, import the natural round trip. Rows match existing lines by fund,
/// department, and account; the import adds or updates and never deletes.
/// </para>
/// </summary>
public interface IBudgetImportService
{
    /// <summary>Parses and analyses the file. Fails (not per row) when the file itself is unusable or the version cannot be imported into.</summary>
    Task<Result<ImportPreviewDto>> PreviewAsync(Guid budgetVersionId, string fileName, Stream content, CancellationToken ct = default);

    /// <summary>Re-analyses the rows against the database now and applies them in one transaction, or refuses if any row has an error.</summary>
    Task<Result<ImportResultDto>> CommitAsync(Guid budgetVersionId, string fileName, IReadOnlyList<ImportRowInput> rows, CancellationToken ct = default);
}
