namespace CivicBudget.Application.Import;

/// <summary>What committing a row would do. Error rows block the whole import; nothing is written until every row is clean.</summary>
public enum ImportRowAction
{
    /// <summary>No line exists for this fund, department, and account: one will be added.</summary>
    Add = 1,

    /// <summary>A line exists and at least one value differs: it will be updated.</summary>
    Update = 2,

    /// <summary>A line exists with the same values: nothing to do.</summary>
    Unchanged = 3,

    /// <summary>The row cannot be applied; see <see cref="ImportRowDto.Errors"/>.</summary>
    Error = 4,
}

/// <summary>
/// One row of the file after analysis: the raw codes as typed, the names they resolved to, the
/// parsed figures, what the commit would do, and every problem found. Raw values are kept so the
/// preview can show exactly what the clerk wrote next to what it became.
/// </summary>
public sealed record ImportRowDto(
    int RowNumber,
    string FundCode,
    string? DepartmentCode,
    string AccountCode,
    string? FundName,
    string? DepartmentName,
    string? AccountName,
    decimal? Amount,
    decimal? PriorYearActual,
    decimal? CurrentYearBudget,
    string? Justification,
    decimal? ExistingAmount,
    ImportRowAction Action,
    IReadOnlyList<string> Errors);

/// <summary>
/// The analysed file: rows plus the counts the preview screen shows before asking for confirmation.
/// <see cref="Inputs"/> are the raw rows, kept so the page can hand them back to commit.
/// </summary>
public sealed record ImportPreviewDto(Guid BudgetVersionId, string FileName, IReadOnlyList<ImportRowDto> Rows, IReadOnlyList<ImportRowInput> Inputs)
{
    public int AddCount => Rows.Count(r => r.Action == ImportRowAction.Add);
    public int UpdateCount => Rows.Count(r => r.Action == ImportRowAction.Update);
    public int UnchangedCount => Rows.Count(r => r.Action == ImportRowAction.Unchanged);
    public int ErrorCount => Rows.Count(r => r.Action == ImportRowAction.Error);

    /// <summary>Only a file with no errors and at least one change may be committed.</summary>
    public bool CanCommit => ErrorCount == 0 && AddCount + UpdateCount > 0;

    /// <summary>Sum of the amounts that would be written (adds and updates), for the preview's headline figure.</summary>
    public decimal AmountToWrite => Rows.Where(r => r.Action is ImportRowAction.Add or ImportRowAction.Update).Sum(r => r.Amount ?? 0m);
}

/// <summary>What a commit did, for the toast and the audit event.</summary>
public sealed record ImportResultDto(int Added, int Updated, int Unchanged);

/// <summary>
/// The raw row as the file gave it. The preview hands these back to commit so the service can
/// re-analyse against the database at that moment instead of trusting a preview that may be stale.
/// </summary>
public sealed record ImportRowInput(
    int RowNumber,
    string? FundCode,
    string? DepartmentCode,
    string? AccountCode,
    string? Amount,
    string? PriorYearActual,
    string? CurrentYearBudget,
    string? Justification);
