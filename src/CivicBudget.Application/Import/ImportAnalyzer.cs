using System.Globalization;
using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;

namespace CivicBudget.Application.Import;

/// <summary>A fund, department, or account the analyser may resolve a code to.</summary>
public sealed record ImportLookup(Guid Id, string Code, string Name, bool IsActive, AccountType? AccountType = null);

/// <summary>A line already in the version, so the analyser can tell an add from an update.</summary>
public sealed record ExistingLine(Guid LineId, Guid FundId, Guid? DepartmentId, Guid AccountId, decimal Amount, decimal PriorYearActual, decimal CurrentYearBudget, string? Justification);

/// <summary>
/// The import rules, as a pure function over rows and lookups so every rule has a unit test with
/// no database. The service supplies lookups and existing lines and applies what comes back.
/// Rules mirror <see cref="BudgetVersion.AddLine"/> (active codes, a department on expenditure
/// lines, no duplicate key) plus the file-level ones (parseable numbers, no duplicate rows).
/// </summary>
public static class ImportAnalyzer
{
    public static IReadOnlyList<ImportRowDto> Analyze(
        IReadOnlyList<ImportRowInput> rows,
        IReadOnlyList<ImportLookup> funds,
        IReadOnlyList<ImportLookup> departments,
        IReadOnlyList<ImportLookup> accounts,
        IReadOnlyList<ExistingLine> existing)
    {
        Dictionary<string, ImportLookup> fundsByCode = funds.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ImportLookup> departmentsByCode = departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ImportLookup> accountsByCode = accounts.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        Dictionary<(Guid, Guid?, Guid), ExistingLine> existingByKey = existing.ToDictionary(l => (l.FundId, l.DepartmentId, l.AccountId));
        var seen = new HashSet<(Guid, Guid?, Guid)>();

        var result = new List<ImportRowDto>(rows.Count);
        foreach (ImportRowInput row in rows)
        {
            var errors = new List<string>();

            ImportLookup? fund = Resolve(row.FundCode, fundsByCode, "fund", errors);
            ImportLookup? department = row.DepartmentCode is null ? null : Resolve(row.DepartmentCode, departmentsByCode, "department", errors);
            ImportLookup? account = Resolve(row.AccountCode, accountsByCode, "account", errors);
            if (account is { AccountType: AccountType.Expenditure } && row.DepartmentCode is null)
            {
                errors.Add("Expenditure lines need a department.");
            }

            decimal? amount = MoneyText.Parse(row.Amount, "Amount", required: true, errors);

            decimal? prior = MoneyText.Parse(row.PriorYearActual, ImportFileParser.PriorYearActualHeader, required: false, errors);
            decimal? current = MoneyText.Parse(row.CurrentYearBudget, ImportFileParser.CurrentYearBudgetHeader, required: false, errors);
            // Revenues and expenditures are both entered as positive numbers (the account type says
            // which way they count), so a negative in any money column is a mistake in the file.
            foreach ((decimal? value, string column) in new[] { (amount, "Amount"), (prior, ImportFileParser.PriorYearActualHeader), (current, ImportFileParser.CurrentYearBudgetHeader) })
            {
                if (value < 0m)
                {
                    errors.Add($"{column} cannot be negative. Enter revenues and expenditures as positive numbers.");
                }
            }

            if (new[] { amount, prior, current }.Any(a => a is { } value && !Money.IsStorable(value)))
            {
                errors.Add(Money.TooLargeMessage);
            }

            string? justification = row.Justification;
            if (justification is { Length: > BudgetLine.JustificationMaxLength })
            {
                errors.Add($"Justification is longer than {BudgetLine.JustificationMaxLength} characters.");
            }

            ImportRowAction action = ImportRowAction.Error;
            decimal? existingAmount = null;
            if (errors.Count == 0)
            {
                (Guid, Guid?, Guid) key = (fund!.Id, department?.Id, account!.Id);
                if (!seen.Add(key))
                {
                    errors.Add("This fund, department, and account appear more than once in the file.");
                }
                else if (existingByKey.TryGetValue(key, out ExistingLine? line))
                {
                    existingAmount = line.Amount;
                    // Blank optional columns mean "leave as is", so a file with only Amount filled in updates amounts alone.
                    bool changed = line.Amount != amount
                        || (prior is not null && line.PriorYearActual != prior)
                        || (current is not null && line.CurrentYearBudget != current)
                        || (justification is not null && line.Justification != justification);
                    action = changed ? ImportRowAction.Update : ImportRowAction.Unchanged;
                }
                else
                {
                    action = ImportRowAction.Add;
                }
            }

            result.Add(new ImportRowDto(
                row.RowNumber, row.FundCode ?? "", row.DepartmentCode, row.AccountCode ?? "",
                fund?.Name, department?.Name, account?.Name,
                amount, prior, current, justification, existingAmount,
                errors.Count == 0 ? action : ImportRowAction.Error, errors,
                fund?.Id, department?.Id, account?.Id));
        }

        return result;
    }

    private static ImportLookup? Resolve(string? code, Dictionary<string, ImportLookup> byCode, string kind, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errors.Add($"The {kind} code is missing.");
            return null;
        }

        // Exports pad numeric codes to the account number format ("101" is written "0101" in a
        // four-digit fund segment), so a file that came out of CivicBudget must match on the way back in.
        if (!byCode.TryGetValue(code, out ImportLookup? match) && code.All(char.IsAsciiDigit))
        {
            string unpadded = code.TrimStart('0');
            match = byCode.Values.FirstOrDefault(l => l.Code.All(char.IsAsciiDigit) && l.Code.TrimStart('0') == unpadded);
        }

        if (match is null)
        {
            errors.Add($"No {kind} has the code \"{code}\".");
            return null;
        }

        if (!match.IsActive)
        {
            errors.Add($"The {kind} \"{code}\" is inactive.");
            return null;
        }

        return match;
    }
}
