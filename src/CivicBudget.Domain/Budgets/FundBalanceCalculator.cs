using CivicBudget.Domain.Accounts;

namespace CivicBudget.Domain.Budgets;

/// <summary>A single line's contribution to a fund balance: what kind of account, and how much.</summary>
public sealed record LineAmount(Guid FundId, AccountType AccountType, decimal Amount);

/// <summary>
/// Pure functions that roll budget lines up into <see cref="FundBalanceSummary"/> values.
/// Takes plain <see cref="LineAmount"/> inputs so it can be unit-tested and used from projections
/// without loading full entities.
/// </summary>
public static class FundBalanceCalculator
{
    public static FundBalanceSummary Calculate(Guid fundId, decimal beginningBalance, IEnumerable<LineAmount> lines)
    {
        decimal revenues = 0m, transfersIn = 0m, expenditures = 0m, transfersOut = 0m;

        foreach (LineAmount line in lines.Where(l => l.FundId == fundId))
        {
            switch (line.AccountType)
            {
                case AccountType.Revenue:
                    revenues += line.Amount;
                    break;
                case AccountType.TransferIn:
                    transfersIn += line.Amount;
                    break;
                case AccountType.Expenditure:
                    expenditures += line.Amount;
                    break;
                case AccountType.TransferOut:
                    transfersOut += line.Amount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lines), line.AccountType, "Unknown account type.");
            }
        }

        return new FundBalanceSummary(fundId, beginningBalance, revenues, transfersIn, expenditures, transfersOut);
    }

    /// <summary>
    /// Summaries for every fund that has a line or a beginning balance in the version.
    /// Requires <see cref="BudgetLine.Account"/> to be loaded.
    /// </summary>
    public static IReadOnlyList<FundBalanceSummary> CalculateAll(BudgetVersion version)
    {
        List<LineAmount> amounts = version.Lines
            .Select(l => new LineAmount(l.FundId, l.Account.Type, l.Amount))
            .ToList();

        IEnumerable<Guid> fundIds = amounts.Select(a => a.FundId)
            .Concat(version.BeginningBalances.Select(b => b.FundId))
            .Distinct();

        return fundIds
            .Select(fundId => Calculate(fundId, version.GetBeginningBalance(fundId), amounts))
            .ToList();
    }
}
