using CivicBudget.Application.Common;
using CivicBudget.Application.Export;

namespace CivicBudget.Application.Import;

/// <summary>
/// Turns a <see cref="TabularFile"/> into raw import rows by header name. Only the shape of the
/// file is judged here (are the required columns present); the values are judged per row by
/// <see cref="ImportAnalyzer"/> so one bad cell never hides the rest of the file.
/// </summary>
public static class ImportFileParser
{
    public const string FundHeader = "Fund";
    public const string DepartmentHeader = "Department";
    public const string AccountHeader = "Account";
    public const string AmountHeader = "Amount";
    public const string PriorYearActualHeader = "Prior Year Actual";
    public const string CurrentYearBudgetHeader = "Current Year Budget";
    public const string JustificationHeader = "Justification";

    /// <summary>The header row the workspace export writes and the import expects, in this order.</summary>
    public static readonly IReadOnlyList<string> Headers =
        [FundHeader, DepartmentHeader, AccountHeader, AmountHeader, PriorYearActualHeader, CurrentYearBudgetHeader, JustificationHeader];

    public static Result<IReadOnlyList<ImportRowInput>> Parse(TabularFile file)
    {
        if (file.Headers.Count == 0)
        {
            return Result.Failure<IReadOnlyList<ImportRowInput>>("The file is empty.");
        }

        int fund = file.IndexOf(FundHeader);
        int department = file.IndexOf(DepartmentHeader);
        int account = file.IndexOf(AccountHeader);
        int amount = file.IndexOf(AmountHeader);
        int prior = file.IndexOf(PriorYearActualHeader);
        int current = file.IndexOf(CurrentYearBudgetHeader);
        int justification = file.IndexOf(JustificationHeader);

        List<string> missing = new[] { (FundHeader, fund), (AccountHeader, account), (AmountHeader, amount) }
            .Where(h => h.Item2 < 0).Select(h => h.Item1).ToList();
        if (missing.Count > 0)
        {
            return Result.Failure<IReadOnlyList<ImportRowInput>>(
                $"The file needs columns named {string.Join(", ", missing)}. Found: {string.Join(", ", file.Headers)}.");
        }

        // Row numbers are the spreadsheet's (header is row 1), so an error message points at the cell the clerk sees.
        List<ImportRowInput> rows = file.Rows
            .Select((cells, i) => new ImportRowInput(
                i + 2,
                Cell(cells, fund), Cell(cells, department), Cell(cells, account),
                Cell(cells, amount), Cell(cells, prior), Cell(cells, current), Cell(cells, justification)))
            .Where(r => !string.IsNullOrWhiteSpace(r.FundCode) || !string.IsNullOrWhiteSpace(r.AccountCode) || !string.IsNullOrWhiteSpace(r.Amount))
            .ToList();

        return rows.Count == 0
            ? Result.Failure<IReadOnlyList<ImportRowInput>>("The file has a header row but no data rows.")
            : Result.Success<IReadOnlyList<ImportRowInput>>(rows);
    }

    private static string? Cell(string?[] cells, int index) =>
        index < 0 || index >= cells.Length || string.IsNullOrWhiteSpace(cells[index]) ? null : cells[index]!.Trim();
}
