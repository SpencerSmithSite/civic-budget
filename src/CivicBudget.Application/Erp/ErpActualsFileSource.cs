using System.Globalization;
using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Erp;

/// <summary>
/// Reads an actuals export with one row per amount. Columns (any order, case-insensitive):
/// <c>Fiscal Year</c>, <c>Type</c> (Actual, Encumbrance, or Cash), <c>Account</c> (the full
/// account number for Actual and Encumbrance rows, the fund number for Cash), <c>Period</c> (the
/// fiscal month, 1 to 12, on Actual rows), and <c>Amount</c>. The account number is written the
/// way the ERP writes it, "1000-110-5110", because that is what an ERP report prints; the
/// government's number format splits it into codes. The file covers one fiscal year and runs
/// through the latest period it has activity for.
/// </summary>
public sealed class ErpActualsFileSource(ISpreadsheetReader spreadsheetReader) : IErpActualsFileSource
{
    /// <summary>Beyond this, the upload is not an actuals export (a county's year is a few hundred thousand rows at most).</summary>
    public const int MaxRows = 500_000;

    public string Name => "ERP actuals export file";

    public Result<ErpActuals> Read(string fileName, Stream content, AccountNumberFormat format)
    {
        TabularFile file;
        try
        {
            file = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" => CsvReader.Read(content),
                ".xlsx" => spreadsheetReader.Read(content),
                _ => throw new InvalidDataException("Upload a .csv or .xlsx actuals export."),
            };
        }
        catch (InvalidDataException ex)
        {
            return Result.Failure<ErpActuals>(ex.Message);
        }

        int year = file.IndexOf("Fiscal Year"), type = file.IndexOf("Type"), account = file.IndexOf("Account");
        int period = file.IndexOf("Period"), amount = file.IndexOf("Amount");
        if (year < 0 || type < 0 || account < 0 || amount < 0)
        {
            return Result.Failure<ErpActuals>($"The file needs Fiscal Year, Type, Account, Period, and Amount columns. Found: {string.Join(", ", file.Headers)}.");
        }

        if (file.Rows.Count > MaxRows)
        {
            return Result.Failure<ErpActuals>($"The file has {file.Rows.Count:N0} rows; an actuals export for one year should have fewer than {MaxRows:N0}.");
        }

        var activity = new List<ErpActivity>();
        var encumbrances = new List<ErpOpenEncumbrance>();
        var cash = new List<ErpCash>();
        var problems = new List<string>();
        var years = new HashSet<int>();

        for (int i = 0; i < file.Rows.Count; i++)
        {
            string?[] cells = file.Rows[i];
            int row = i + 2;
            string? y = Cell(cells, year), t = Cell(cells, type), a = Cell(cells, account), p = Cell(cells, period), m = Cell(cells, amount);
            if (y is null && t is null && a is null && m is null)
            {
                continue;
            }

            var errors = new List<string>();
            if (!int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out int fiscalYear) || fiscalYear is < 1900 or > 2200)
            {
                errors.Add($"Fiscal Year \"{y}\" is not a year.");
            }

            decimal? value = MoneyText.Parse(m, "Amount", required: true, errors);
            string kind = (t ?? "").Trim().ToLowerInvariant();
            switch (kind)
            {
                case "actual":
                case "encumbrance":
                    if (!AccountNumber.TryParse(format, a, out string fundCode, out string? departmentCode, out string objectCode))
                    {
                        errors.Add($"Account \"{a}\" is not a full account number like {AccountNumber.Compose(format, "1000", "110", "5110")}.");
                    }

                    if (kind == "encumbrance")
                    {
                        if (errors.Count == 0)
                        {
                            encumbrances.Add(new ErpOpenEncumbrance(fundCode, departmentCode, objectCode, value!.Value));
                        }

                        break;
                    }

                    if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out int month) || month is < 1 or > 12)
                    {
                        errors.Add($"Period \"{p}\" must be a fiscal month from 1 to 12.");
                    }

                    if (errors.Count == 0)
                    {
                        activity.Add(new ErpActivity(fundCode, departmentCode, objectCode, month, value!.Value));
                    }

                    break;
                case "cash":
                    if (a is null)
                    {
                        errors.Add("Cash rows need the fund number in the Account column.");
                    }
                    else if (errors.Count == 0)
                    {
                        cash.Add(new ErpCash(a, value!.Value));
                    }

                    break;
                default:
                    errors.Add($"Type must be Actual, Encumbrance, or Cash; found \"{t}\".");
                    break;
            }

            if (errors.Count > 0)
            {
                problems.AddRange(errors.Select(e => $"Row {row}: {e}"));
            }
            else
            {
                years.Add(fiscalYear);
            }
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ErpActuals>(problems.Take(50).Select(p => new ValidationError(string.Empty, p)));
        }

        if (years.Count == 0)
        {
            return Result.Failure<ErpActuals>("The file has a header row but no amounts.");
        }

        // A sync replaces a whole year, so a file spanning two would silently lose one of them.
        if (years.Count > 1)
        {
            return Result.Failure<ErpActuals>($"The file mixes fiscal years ({string.Join(", ", years.Order())}). Export one year at a time.");
        }

        int through = activity.Count == 0 ? 0 : activity.Max(x => x.Period);
        return Result.Success(new ErpActuals(years.Single(), through, activity, encumbrances, cash));
    }

    private static string? Cell(string?[] cells, int index) =>
        index < 0 || index >= cells.Length || string.IsNullOrWhiteSpace(cells[index]) ? null : cells[index]!.Trim();
}
