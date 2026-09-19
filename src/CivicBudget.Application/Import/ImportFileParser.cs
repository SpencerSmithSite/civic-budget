using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Import;

/// <summary>
/// Turns a <see cref="TabularFile"/> into raw import rows by header name. Only the shape of the
/// file is judged here (are the required columns present); the values are judged per row by
/// <see cref="ImportAnalyzer"/> so one bad cell never hides the rest of the file.
/// </summary>
public static class ImportFileParser
{
    public const string AccountNumberHeader = "Account Number";
    public const string FundHeader = "Fund";
    public const string DepartmentHeader = "Department";
    public const string AccountHeader = "Account";
    public const string AmountHeader = "Amount";
    public const string PriorYearActualHeader = "Prior Year Actual";
    public const string CurrentYearBudgetHeader = "Current Year Budget";
    public const string JustificationHeader = "Justification";

    public const string AccountNameHeader = "Account Name";

    /// <summary>
    /// The header row the workspace export writes. The import needs either Account Number or the
    /// three code columns, plus Amount; Account Name is written for the reader and ignored on the way in.
    /// </summary>
    public static readonly IReadOnlyList<string> Headers =
        [AccountNumberHeader, FundHeader, DepartmentHeader, AccountHeader, AccountNameHeader, AmountHeader, PriorYearActualHeader, CurrentYearBudgetHeader, JustificationHeader];

    public static Result<IReadOnlyList<ImportRowInput>> Parse(TabularFile file, AccountNumberFormat format)
    {
        if (file.Headers.Count == 0)
        {
            return Result.Failure<IReadOnlyList<ImportRowInput>>("The file is empty.");
        }

        int number = file.IndexOf(AccountNumberHeader);
        int fund = file.IndexOf(FundHeader);
        int department = file.IndexOf(DepartmentHeader);
        int account = file.IndexOf(AccountHeader);
        int amount = file.IndexOf(AmountHeader);
        int prior = file.IndexOf(PriorYearActualHeader);
        int current = file.IndexOf(CurrentYearBudgetHeader);
        int justification = file.IndexOf(JustificationHeader);

        bool hasCodes = fund >= 0 && account >= 0;
        if (amount < 0 || (number < 0 && !hasCodes))
        {
            return Result.Failure<IReadOnlyList<ImportRowInput>>(
                $"The file needs an Amount column and either an {AccountNumberHeader} column or {FundHeader}, {DepartmentHeader}, and {AccountHeader} columns. Found: {string.Join(", ", file.Headers)}.");
        }

        // Row numbers are the spreadsheet's (header is row 1), so an error message points at the cell the clerk sees.
        // A full number in the row wins over the code columns; a number that does not parse is left as the
        // raw text in the fund column so the analyser can report it against the row.
        List<ImportRowInput> rows = file.Rows
            .Select((cells, i) =>
            {
                string? fundCode = Cell(cells, fund), departmentCode = Cell(cells, department), objectCode = Cell(cells, account);
                string? full = Cell(cells, number);
                if (full is not null)
                {
                    if (AccountNumber.TryParse(format, full, out string f, out string? d, out string o))
                    {
                        (fundCode, departmentCode, objectCode) = (f, d, o);
                    }
                    else
                    {
                        (fundCode, departmentCode, objectCode) = (full, null, null);
                    }
                }

                return new ImportRowInput(i + 2, fundCode, departmentCode, objectCode,
                    Cell(cells, amount), Cell(cells, prior), Cell(cells, current), Cell(cells, justification));
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.FundCode) || !string.IsNullOrWhiteSpace(r.AccountCode) || !string.IsNullOrWhiteSpace(r.Amount))
            .ToList();

        return rows.Count == 0
            ? Result.Failure<IReadOnlyList<ImportRowInput>>("The file has a header row but no data rows.")
            : Result.Success<IReadOnlyList<ImportRowInput>>(rows);
    }

    private static string? Cell(string?[] cells, int index) =>
        index < 0 || index >= cells.Length || string.IsNullOrWhiteSpace(cells[index]) ? null : cells[index]!.Trim();
}
