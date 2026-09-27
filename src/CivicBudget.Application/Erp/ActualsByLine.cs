namespace CivicBudget.Application.Erp;

/// <summary>A budget line's identity: fund, department (null for a fund-level line), account.</summary>
public readonly record struct LineKey(Guid FundId, Guid? DepartmentId, Guid AccountId);

/// <summary>
/// Adds the ERP's amounts up per budget line. Most lines match the ERP one to one. The exception is
/// a line budgeted at fund level (revenue with no department) while the ERP records the same
/// receipts against a department: the line then takes every amount on that fund and account that
/// no department-level line in the budget claims, so the money is counted once and is never lost.
/// </summary>
public static class ActualsByLine
{
    public static Dictionary<LineKey, decimal> Sum(IEnumerable<LineKey> lines, IEnumerable<(LineKey Key, decimal Amount)> amounts)
    {
        var keys = lines.ToHashSet();
        var totals = keys.ToDictionary(k => k, _ => 0m);
        foreach ((LineKey key, decimal amount) in amounts)
        {
            if (keys.Contains(key))
            {
                totals[key] += amount;
                continue;
            }

            var fundLevel = new LineKey(key.FundId, null, key.AccountId);
            if (key.DepartmentId is not null && keys.Contains(fundLevel))
            {
                totals[fundLevel] += amount;
            }
        }

        return totals;
    }
}
