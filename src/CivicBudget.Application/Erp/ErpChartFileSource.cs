using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Erp;

/// <summary>
/// Reads a chart export with one row per code. Columns (any order, case-insensitive):
/// <c>Kind</c> (Fund, Department or Program, Object or Account), <c>Code</c>, <c>Name</c>,
/// <c>Type</c> (objects: Revenue, Expenditure, TransferIn, TransferOut), <c>Category</c>
/// (funds: General, SpecialRevenue...; objects: Taxes, PersonalServices...), optional
/// <c>Description</c> and <c>Active</c> (Yes/No, blank means active). Category names are matched
/// loosely ("Special Revenue", "special_revenue" and "SpecialRevenue" are the same) because
/// export files are typed by people. This is a stand-in for the parent ERP's real export; the
/// column names are the seam to adjust when that layout is known.
/// </summary>
public sealed class ErpChartFileSource(ISpreadsheetReader spreadsheetReader) : IErpChartFileSource
{
    public string Name => "ERP chart export file";

    public Result<ErpChart> Read(string fileName, Stream content)
    {
        TabularFile file;
        try
        {
            file = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" => CsvReader.Read(content),
                ".xlsx" => spreadsheetReader.Read(content),
                _ => throw new InvalidDataException("Upload a .csv or .xlsx chart export."),
            };
        }
        catch (InvalidDataException ex)
        {
            return Result.Failure<ErpChart>(ex.Message);
        }

        int kind = file.IndexOf("Kind"), code = file.IndexOf("Code"), name = file.IndexOf("Name");
        int type = file.IndexOf("Type"), category = file.IndexOf("Category"), description = file.IndexOf("Description"), active = file.IndexOf("Active");
        if (kind < 0 || code < 0 || name < 0)
        {
            return Result.Failure<ErpChart>($"The file needs Kind, Code, and Name columns. Found: {string.Join(", ", file.Headers)}.");
        }

        var funds = new List<ErpFund>();
        var departments = new List<ErpDepartment>();
        var objects = new List<ErpObject>();
        var problems = new List<string>();

        for (int i = 0; i < file.Rows.Count; i++)
        {
            string?[] cells = file.Rows[i];
            int row = i + 2;
            string? k = Cell(cells, kind), c = Cell(cells, code), n = Cell(cells, name);
            if (k is null && c is null && n is null)
            {
                continue;
            }

            if (c is null || n is null)
            {
                problems.Add($"Row {row}: Code and Name are required.");
                continue;
            }

            bool isActive = Cell(cells, active) is not { } a || a.Equals("yes", StringComparison.OrdinalIgnoreCase) || a.Equals("true", StringComparison.OrdinalIgnoreCase) || a == "1";
            switch (Normalize(k))
            {
                case "fund":
                    if (!TryParseEnum(Cell(cells, category), out FundCategory fundCategory))
                    {
                        problems.Add($"Row {row}: fund {c} needs a Category ({string.Join(", ", Enum.GetNames<FundCategory>())}).");
                        break;
                    }

                    funds.Add(new ErpFund(c, n, fundCategory, Cell(cells, description), isActive));
                    break;
                case "department":
                case "program":
                    departments.Add(new ErpDepartment(c, n, Cell(cells, description), isActive));
                    break;
                case "object":
                case "account":
                    if (!TryParseEnum(Cell(cells, type), out AccountType accountType))
                    {
                        problems.Add($"Row {row}: object {c} needs a Type (Revenue, Expenditure, TransferIn, TransferOut).");
                        break;
                    }

                    if (!TryParseEnum(Cell(cells, category), out ReportingCategory reportingCategory) || !ReportingCategoryRules.IsValidFor(reportingCategory, accountType))
                    {
                        problems.Add($"Row {row}: object {c} needs a Category valid for {accountType} ({string.Join(", ", ReportingCategoryRules.CategoriesFor(accountType))}).");
                        break;
                    }

                    objects.Add(new ErpObject(c, n, accountType, reportingCategory, isActive));
                    break;
                default:
                    problems.Add($"Row {row}: Kind must be Fund, Department (or Program), or Object (or Account); found \"{k}\".");
                    break;
            }
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ErpChart>(problems.Select(p => new ValidationError(string.Empty, p)));
        }

        if (funds.Count + departments.Count + objects.Count == 0)
        {
            return Result.Failure<ErpChart>("The file has a header row but no chart rows.");
        }

        return Result.Success(new ErpChart(funds, departments, objects, NumberFormat: null));
    }

    private static string? Cell(string?[] cells, int index) =>
        index < 0 || index >= cells.Length || string.IsNullOrWhiteSpace(cells[index]) ? null : cells[index]!.Trim();

    private static string Normalize(string? value) =>
        (value ?? "").Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static bool TryParseEnum<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        string wanted = Normalize(text);
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (Normalize(candidate.ToString()) == wanted)
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
