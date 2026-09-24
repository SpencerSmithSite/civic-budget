using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Budgets;

/// <summary>
/// A group of lines with subtotals, for the by-department view. Nested: department, fund, category.
/// <see cref="OutsideTotal"/> holds a department's revenue and transfer lines: shown with it, never
/// added into its total (<see cref="AccountTypeExtensions.CountsTowardDepartmentTotal"/>).
/// </summary>
public sealed record LineGroup(string Key, string Title, IReadOnlyList<BudgetLineDto> Lines, IReadOnlyList<LineGroup> Children, IReadOnlyList<BudgetLineDto>? OutsideTotal = null)
{
    public decimal Amount => Lines.Sum(l => l.Amount) + Children.Sum(c => c.Amount);
    public decimal PriorYearActual => Lines.Sum(l => l.PriorYearActual) + Children.Sum(c => c.PriorYearActual);
    public decimal CurrentYearBudget => Lines.Sum(l => l.CurrentYearBudget) + Children.Sum(c => c.CurrentYearBudget);
    public decimal DollarChange => Amount - CurrentYearBudget;
    public decimal? PercentChange => Domain.Common.Money.PercentChange(CurrentYearBudget, Amount);
}

/// <summary>
/// Builds the department, fund, category tree that the by-department screen renders. Pure and
/// unit-tested, so the component only walks a tree and prints numbers.
/// </summary>
public static class BudgetGrouping
{
    public static IReadOnlyList<LineGroup> ByDepartment(IEnumerable<BudgetLineDto> lines) =>
        lines
            .Where(l => l.DepartmentId is not null)
            .GroupBy(l => (l.DepartmentId, l.DepartmentCode, l.DepartmentName))
            .OrderBy(g => g.Key.DepartmentCode)
            .Select(dept => new LineGroup(
                dept.Key.DepartmentId!.Value.ToString(),
                $"{dept.Key.DepartmentCode} {dept.Key.DepartmentName}",
                [],
                ByFund(dept.Where(l => l.AccountType.CountsTowardDepartmentTotal())),
                dept.Where(l => !l.AccountType.CountsTowardDepartmentTotal()).OrderBy(l => l.FundCode).ThenBy(l => l.AccountCode).ToList()))
            .ToList();

    public static IReadOnlyList<LineGroup> ByFund(IEnumerable<BudgetLineDto> lines) =>
        lines
            .GroupBy(l => (l.FundId, l.FundCode, l.FundName))
            .OrderBy(g => g.Key.FundCode)
            .Select(fund => new LineGroup(
                fund.Key.FundId.ToString(),
                $"{fund.Key.FundCode} {fund.Key.FundName}",
                [],
                ByCategory(fund)))
            .ToList();

    public static IReadOnlyList<LineGroup> ByCategory(IEnumerable<BudgetLineDto> lines) =>
        lines
            .GroupBy(l => (l.AccountType, l.Category))
            .OrderBy(g => g.Key.AccountType)
            .ThenBy(g => g.Key.Category)
            .Select(cat => new LineGroup(
                $"{cat.Key.AccountType}:{cat.Key.Category}",
                CategoryTitle(cat.Key.AccountType, cat.Key.Category),
                cat.OrderBy(l => l.AccountCode).ToList(),
                []))
            .ToList();

    private static string CategoryTitle(AccountType type, ReportingCategory category)
    {
        string name = SplitPascalCase(category.ToString());
        return type switch
        {
            AccountType.TransferIn => "Transfers In",
            AccountType.TransferOut => "Transfers Out",
            _ => name,
        };
    }

    private static string SplitPascalCase(string value)
    {
        var chars = new List<char>(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]))
            {
                chars.Add(' ');
            }

            chars.Add(value[i]);
        }

        return new string(chars.ToArray()).Replace(" And ", " & ", StringComparison.Ordinal);
    }
}
