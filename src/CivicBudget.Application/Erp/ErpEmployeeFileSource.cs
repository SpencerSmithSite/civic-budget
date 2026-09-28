using System.Globalization;
using System.Text.RegularExpressions;
using CivicBudget.Application.Common;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Erp;

/// <summary>
/// Reads an employee export with one row per employee. Columns (any order, case-insensitive):
/// <list type="bullet">
/// <item><c>Employee ID</c>, <c>Name</c>, <c>Title</c>, <c>Department</c> (its code): required.</item>
/// <item><c>Pay Type</c> (Salary or Hourly) and <c>Rate</c> (the yearly salary or hourly rate): required, unless
/// <c>Grade</c> and <c>Step</c> place the employee on a pay scale, which then sets both.</item>
/// <item><c>Annual Hours</c>, <c>Hire Date</c> (yyyy-MM-dd or M/d/yyyy), <c>Retirement</c> (the system's
/// name, blank for none), <c>Pick Up</c> (Yes when the employer pays the employee's share): optional.</item>
/// <item><c>Funds</c>: the labor distribution, "1000 50%; 2011 50%" (a single fund may be written alone,
/// "1000"). Required.</item>
/// <item><c>Benefits</c>: "Medical (PPO): Family; Dental: Family; Life ($25,000): Single". Optional.</item>
/// </list>
/// The layout is a reasonable guess at what a payroll export carries, written down so a real ERP's
/// adapter has one shape to translate into; a file that puts several values in one cell keeps it to
/// one row per employee, which is how payroll registers print.
/// </summary>
public sealed partial class ErpEmployeeFileSource(ISpreadsheetReader spreadsheetReader) : IErpEmployeeFileSource
{
    /// <summary>Beyond this, the upload is not a municipal payroll (a large Ohio city has a few thousand employees).</summary>
    public const int MaxRows = 20_000;

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy"];

    public string Name => "ERP employee export file";

    public Result<ErpEmployees> Read(string fileName, Stream content, DateOnly today)
    {
        TabularFile file;
        try
        {
            file = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" => CsvReader.Read(content),
                ".xlsx" => spreadsheetReader.Read(content),
                _ => throw new InvalidDataException("Upload a .csv or .xlsx employee export."),
            };
        }
        catch (InvalidDataException ex)
        {
            return Result.Failure<ErpEmployees>(ex.Message);
        }

        int id = file.IndexOf("Employee ID"), name = file.IndexOf("Name"), title = file.IndexOf("Title"), department = file.IndexOf("Department");
        int payType = file.IndexOf("Pay Type"), rate = file.IndexOf("Rate"), hours = file.IndexOf("Annual Hours"), hired = file.IndexOf("Hire Date");
        int grade = file.IndexOf("Grade"), step = file.IndexOf("Step"), retirement = file.IndexOf("Retirement"), pickUp = file.IndexOf("Pick Up");
        int funds = file.IndexOf("Funds"), benefits = file.IndexOf("Benefits");
        if (id < 0 || name < 0 || title < 0 || department < 0 || funds < 0 || (rate < 0 && grade < 0))
        {
            return Result.Failure<ErpEmployees>($"The file needs Employee ID, Name, Title, Department, Pay Type, Rate (or Grade and Step), and Funds columns. Found: {string.Join(", ", file.Headers)}.");
        }

        if (file.Rows.Count > MaxRows)
        {
            return Result.Failure<ErpEmployees>($"The file has {file.Rows.Count:N0} rows; an employee export should have fewer than {MaxRows:N0}.");
        }

        var employees = new List<ErpEmployee>();
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < file.Rows.Count; i++)
        {
            string?[] cells = file.Rows[i];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var errors = new List<string>();
            string? employeeId = Cell(cells, id), employeeName = Cell(cells, name), employeeTitle = Cell(cells, title), departmentCode = Cell(cells, department);
            if (employeeId is null || employeeName is null || employeeTitle is null || departmentCode is null)
            {
                errors.Add("Employee ID, Name, Title, and Department are required.");
            }
            else if (!seen.Add(employeeId))
            {
                errors.Add($"Employee {employeeId} appears more than once.");
            }

            string? gradeText = Cell(cells, grade);
            int? stepNumber = null;
            if (Cell(cells, step) is { } stepText)
            {
                stepNumber = int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
                if (stepNumber is null)
                {
                    errors.Add($"Step \"{stepText}\" is not a whole number.");
                }
            }

            bool onScale = gradeText is not null && stepNumber is not null;
            PayBasis basis = PayBasis.Salary;
            string? payTypeText = Cell(cells, payType);
            if (payTypeText is not null && !TryPayBasis(payTypeText, out basis))
            {
                errors.Add($"Pay Type must be Salary or Hourly; found \"{payTypeText}\".");
            }
            else if (payTypeText is null && !onScale)
            {
                errors.Add("Pay Type is required unless Grade and Step are given.");
            }

            decimal? payRate = MoneyText.Parse(Cell(cells, rate), "Rate", required: !onScale, errors);
            decimal? annualHours = null;
            if (Cell(cells, hours) is { } hoursText)
            {
                annualHours = decimal.TryParse(hoursText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal h) && h > 0m ? h : null;
                if (annualHours is null)
                {
                    errors.Add($"Annual Hours \"{hoursText}\" is not a number of hours.");
                }
            }

            DateOnly? hireDate = null;
            if (Cell(cells, hired) is { } dateText)
            {
                hireDate = DateOnly.TryParseExact(dateText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d) ? d : null;
                if (hireDate is null)
                {
                    errors.Add($"Hire Date \"{dateText}\" is not a date like 2019-06-10.");
                }
            }

            List<ErpFundShare> shares = ReadFunds(Cell(cells, funds), errors);
            List<ErpBenefit> enrolled = ReadBenefits(Cell(cells, benefits), errors);

            if (errors.Count > 0)
            {
                problems.AddRange(errors.Select(e => $"Row {i + 2}{(employeeId is null ? "" : $" ({employeeId})")}: {e}"));
                continue;
            }

            employees.Add(new ErpEmployee(
                employeeId!, employeeName!, employeeTitle!, departmentCode!, basis, payRate ?? 0m, annualHours, hireDate,
                onScale ? gradeText : null, onScale ? stepNumber : null,
                Cell(cells, retirement), IsYes(Cell(cells, pickUp)), shares, enrolled));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ErpEmployees>(problems.Take(50).Select(p => new ValidationError(string.Empty, p)));
        }

        return employees.Count == 0
            ? Result.Failure<ErpEmployees>("The file has a header row but no employees.")
            : Result.Success(new ErpEmployees(today, employees));
    }

    /// <summary>"1000 50%; 2011 50%", "1000=50; 2011=50", or a single fund code for all of it.</summary>
    public static List<ErpFundShare> ReadFunds(string? text, List<string> errors)
    {
        var shares = new List<ErpFundShare>();
        if (text is null)
        {
            errors.Add("Funds is required: the fund or funds the employee is paid from.");
            return shares;
        }

        string[] parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && !parts[0].Any(c => char.IsWhiteSpace(c) || c is '=' or '%'))
        {
            shares.Add(new ErpFundShare(parts[0], 100m));
            return shares;
        }

        foreach (string part in parts)
        {
            Match match = FundShareText().Match(part);
            if (!match.Success)
            {
                errors.Add($"Funds: \"{part}\" should be a fund and its share, like \"1000 50%\".");
                continue;
            }

            shares.Add(new ErpFundShare(match.Groups["fund"].Value, decimal.Parse(match.Groups["pct"].Value, CultureInfo.InvariantCulture)));
        }

        return shares;
    }

    /// <summary>"Medical (PPO): Family; Life ($25,000): Single": the plan name is everything before the last colon.</summary>
    public static List<ErpBenefit> ReadBenefits(string? text, List<string> errors)
    {
        var benefits = new List<ErpBenefit>();
        foreach (string part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.LastIndexOf(':');
            if (colon <= 0 || !TryTier(part[(colon + 1)..], out CoverageTier tier))
            {
                errors.Add($"Benefits: \"{part}\" should be a plan and its coverage, like \"Medical: Family\" (Single, Employee + Spouse, or Family).");
                continue;
            }

            benefits.Add(new ErpBenefit(part[..colon].Trim(), tier));
        }

        return benefits;
    }

    public static bool TryTier(string text, out CoverageTier tier)
    {
        string t = text.Trim().ToLowerInvariant().Replace("&", "+", StringComparison.Ordinal).Replace(" and ", " + ", StringComparison.Ordinal);
        (bool ok, tier) = t switch
        {
            "single" or "employee" or "employee only" => (true, CoverageTier.EmployeeOnly),
            "employee + spouse" or "employee+spouse" or "spouse" => (true, CoverageTier.EmployeeSpouse),
            "family" => (true, CoverageTier.Family),
            _ => (false, CoverageTier.EmployeeOnly),
        };
        return ok;
    }

    private static bool TryPayBasis(string text, out PayBasis basis)
    {
        (bool ok, basis) = text.Trim().ToLowerInvariant() switch
        {
            "salary" or "salaried" or "s" => (true, PayBasis.Salary),
            "hourly" or "hour" or "h" => (true, PayBasis.Hourly),
            _ => (false, PayBasis.Salary),
        };
        return ok;
    }

    private static bool IsYes(string? text) => text?.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1";

    private static string? Cell(string?[] cells, int index) =>
        index < 0 || index >= cells.Length || string.IsNullOrWhiteSpace(cells[index]) ? null : cells[index]!.Trim();

    [GeneratedRegex(@"^(?<fund>[^\s=]+)\s*(=|\s)\s*(?<pct>\d+(\.\d+)?)\s*%?$")]
    private static partial Regex FundShareText();
}
