using CivicBudget.Application.Common;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Erp;

/// <summary>A chart code CivicBudget knows, with the account type where it matters.</summary>
public sealed record ChartCode(Guid Id, string Code, string Name, AccountType? Type = null);

/// <summary>One amount matched to CivicBudget's fund, department, and account. Encumbrances carry period 0.</summary>
public sealed record MatchedAmount(Guid FundId, Guid? DepartmentId, Guid AccountId, AccountType Type, int Period, decimal Amount);

/// <summary>An <see cref="ErpActuals"/> with every code resolved and repeated rows added together.</summary>
public sealed record MatchedActuals(
    int FiscalYear,
    int ThroughPeriod,
    IReadOnlyList<MatchedAmount> Activity,
    IReadOnlyList<MatchedAmount> Encumbrances,
    IReadOnlyDictionary<Guid, decimal> Cash)
{
    /// <summary>Money in: revenue and transfers in, the way Ohio's cash-basis reports count receipts.</summary>
    public decimal Receipts => Activity.Where(a => a.Type.IsResource()).Sum(a => a.Amount);

    /// <summary>Money out: expenditures and transfers out.</summary>
    public decimal Disbursements => Activity.Where(a => !a.Type.IsResource()).Sum(a => a.Amount);

    public decimal Encumbered => Encumbrances.Sum(e => e.Amount);
    public decimal CashTotal => Cash.Values.Sum();
    public bool IsYearClosed => ThroughPeriod == 12;
}

/// <summary>
/// Resolves the ERP's codes against the chart, as a pure function so every rule has a unit test.
/// The sync refuses the whole file when any code is unknown: a year of actuals with a missing
/// account would understate every total built on it, and nobody would notice from the report.
/// </summary>
public static class ActualsMatcher
{
    private const int MaxProblemsListed = 25;

    public static Result<MatchedActuals> Match(ErpActuals actuals, IReadOnlyList<ChartCode> funds, IReadOnlyList<ChartCode> departments, IReadOnlyList<ChartCode> accounts)
    {
        if (actuals.Activity.Count == 0)
        {
            return Result.Failure<MatchedActuals>($"FY{actuals.FiscalYear} has no activity yet. Sync it once the ERP has closed its first month.");
        }

        var fundCodes = new CodeLookup(funds);
        var departmentCodes = new CodeLookup(departments);
        var accountCodes = new CodeLookup(accounts);
        var problems = new SortedSet<string>(StringComparer.Ordinal);

        var activity = new Dictionary<(Guid, Guid?, Guid, int), MatchedAmount>();
        foreach (ErpActivity a in actuals.Activity)
        {
            if (Resolve(a.FundCode, a.DepartmentCode, a.ObjectCode, fundCodes, departmentCodes, accountCodes, problems) is { } key)
            {
                (Guid, Guid?, Guid, int) k = (key.Fund, key.Department, key.Account.Id, a.Period);
                decimal before = activity.TryGetValue(k, out MatchedAmount? existing) ? existing.Amount : 0m;
                activity[k] = new MatchedAmount(key.Fund, key.Department, key.Account.Id, key.Account.Type!.Value, a.Period, before + a.Amount);
            }
        }

        var encumbrances = new Dictionary<(Guid, Guid?, Guid), MatchedAmount>();
        foreach (ErpOpenEncumbrance e in actuals.Encumbrances)
        {
            if (Resolve(e.FundCode, e.DepartmentCode, e.ObjectCode, fundCodes, departmentCodes, accountCodes, problems) is { } key)
            {
                (Guid, Guid?, Guid) k = (key.Fund, key.Department, key.Account.Id);
                decimal before = encumbrances.TryGetValue(k, out MatchedAmount? existing) ? existing.Amount : 0m;
                encumbrances[k] = new MatchedAmount(key.Fund, key.Department, key.Account.Id, key.Account.Type!.Value, 0, before + e.Amount);
            }
        }

        var cash = new Dictionary<Guid, decimal>();
        foreach (ErpCash c in actuals.Cash)
        {
            if (fundCodes.Find(c.FundCode) is { } fund)
            {
                cash[fund.Id] = cash.GetValueOrDefault(fund.Id) + c.Amount;
            }
            else
            {
                problems.Add($"Cash for fund {c.FundCode}: no fund has that number.");
            }
        }

        if (problems.Count > 0)
        {
            IEnumerable<string> listed = problems.Take(MaxProblemsListed);
            if (problems.Count > MaxProblemsListed)
            {
                listed = listed.Append($"…and {problems.Count - MaxProblemsListed} more.");
            }

            return Result.Failure<MatchedActuals>(listed.Select(p => new ValidationError(string.Empty, p)));
        }

        return Result.Success(new MatchedActuals(actuals.FiscalYear, actuals.ThroughPeriod, activity.Values.ToList(), encumbrances.Values.ToList(), cash));
    }

    private static (Guid Fund, Guid? Department, ChartCode Account)? Resolve(
        string fundCode, string? departmentCode, string objectCode, CodeLookup funds, CodeLookup departments, CodeLookup accounts, SortedSet<string> problems)
    {
        ChartCode? fund = funds.Find(fundCode);
        ChartCode? department = departmentCode is null ? null : departments.Find(departmentCode);
        ChartCode? account = accounts.Find(objectCode);
        string number = departmentCode is null ? $"{fundCode}-{objectCode}" : $"{fundCode}-{departmentCode}-{objectCode}";
        if (fund is null)
        {
            problems.Add($"{number}: no fund has the number {fundCode}.");
        }

        if (departmentCode is not null && department is null)
        {
            problems.Add($"{number}: no department or program has the code {departmentCode}.");
        }

        if (account is null)
        {
            problems.Add($"{number}: no account has the code {objectCode}.");
        }

        return fund is null || account is null || (departmentCode is not null && department is null) ? null : (fund.Id, department?.Id, account);
    }

    /// <summary>
    /// Codes match ignoring case and, for numeric codes, leading zeros: ERP reports pad codes to the
    /// segment width ("0110"), and the chart may store them unpadded ("110").
    /// </summary>
    private sealed class CodeLookup(IReadOnlyList<ChartCode> codes)
    {
        private readonly Dictionary<string, ChartCode> byKey = codes
            .GroupBy(c => Key(c.Code), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        public ChartCode? Find(string code) => byKey.GetValueOrDefault(Key(code));

        private static string Key(string code)
        {
            string trimmed = code.Trim();
            return trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit) ? trimmed.TrimStart('0') is { Length: > 0 } t ? t : "0" : trimmed;
        }
    }
}
